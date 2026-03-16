#!/usr/bin/env python3
"""
Live provider enrichment for media folders lacking both metadata.json and OPF.

Strategy:
1. Discover media folders missing both metadata.json and any .opf file.
2. Extract the best local identity from embedded tags, filenames, and path data.
3. Query Open Library iteratively using app-style query construction.
4. Fall back to Inventaire when Open Library cannot produce a high-confidence match.
5. Accept only high-confidence matches.
6. Write metadata.json for accepted matches.
7. Emit detailed reports for accepted and unresolved folders.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from difflib import SequenceMatcher
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple
from urllib.error import HTTPError, URLError
from urllib.parse import quote_plus
from urllib.request import urlopen

try:
    from mutagen import File as MutagenFile
except ImportError:
    MutagenFile = None

MEDIA_EXTENSIONS = {".mp3", ".m4b", ".flac", ".aac", ".ogg", ".epub", ".mobi", ".azw3", ".pdf"}
AUDIO_EXTENSIONS = {".mp3", ".m4b", ".flac", ".aac", ".ogg"}
NOISE_TOKENS = {
    "unabridged",
    "abridged",
    "audiobook",
    "audio",
    "ebook",
    "retail",
    "scan",
    "drmfree",
    "drm-free",
    "vbr",
    "kbps",
    "mp3",
    "m4b",
    "epub",
    "azw3",
    "pdf",
    "narrated",
    "narrator",
}
AUTHOR_BY_RE = re.compile(r"\bby\s+(.+)$", re.IGNORECASE)
ISBN13_RE = re.compile(r"(?<!\d)(97[89][\d-]{10,16})(?!\d)", re.IGNORECASE)
ISBN10_RE = re.compile(r"(?<![\dXx])([\d-]{9}[\dXx])(?![\dXx])", re.IGNORECASE)
ASIN_RE = re.compile(r"\b(B0[0-9A-Z]{8})\b", re.IGNORECASE)
YEAR_RE = re.compile(r"\b(19\d{2}|20\d{2}|2100)\b")
BRACKETED_RE = re.compile(r"\[[^\]]*\]|\([^\)]*\)")
NON_ALNUM = re.compile(r"[^a-z0-9]+")
SERIES_SUFFIX_RE = re.compile(r"(:|\-|,)?\s*(book|volume|vol\.?|part|series|world|cycle)\s+\d+.*$", re.IGNORECASE)
AUTHOR_DESCRIPTION_RE = re.compile(r"\bby\s+([^.;]+)$", re.IGNORECASE)
INVENTAIRE_LABEL_RE = re.compile(r"^(.*?)\s*:\s*(.+)$")

AUTHOR_ALIAS_MAP = {
    "terry mancour": ["t l mancour", "t. l. mancour", "tl mancour"],
    "t l mancour": ["terry mancour", "t. l. mancour", "tl mancour"],
    "a j jacobs": ["a.j. jacobs", "aj jacobs"],
    "a.j. jacobs": ["a j jacobs", "aj jacobs"],
    "aj jacobs": ["a j jacobs", "a.j. jacobs"],
}


@dataclass
class FolderCandidate:
    folder: str
    root: str
    author_guess: str
    title_guess: str
    media_files: List[str]
    isbn_guess: Optional[str]
    asin_guess: Optional[str]
    tag_title_guess: Optional[str]
    tag_author_guess: Optional[str]
    local_identity_source: str


@dataclass
class QueryAttempt:
    provider: str
    strategy: str
    query: str
    num_found: int
    accepted: bool
    confidence: Optional[float]
    match_title: Optional[str]
    match_author: Optional[str]
    match_key: Optional[str]
    note: Optional[str] = None


@dataclass
class ProviderMatch:
    provider: str
    title: str
    author: Optional[str]
    key: str
    raw: Dict[str, Any]
    isbn_values: List[str]
    first_publish_year: Optional[Any]


def clean_component(value: str) -> str:
    value = value.replace("_", " ").replace(".", " ").replace("-", " ")
    value = BRACKETED_RE.sub(" ", value)
    value = YEAR_RE.sub(" ", value)

    tokens = []
    for token in value.split():
        lowered = token.strip().lower()
        if lowered in NOISE_TOKENS:
            continue
        tokens.append(token)

    value = " ".join(tokens)
    return re.sub(r"\s+", " ", value).strip(" -")


def normalize_text(value: Optional[str]) -> str:
    if not value:
        return ""

    return NON_ALNUM.sub(" ", value.lower()).strip()


def alias_variants(author: Optional[str]) -> List[str]:
    if not author:
        return []

    base = normalize_text(author)
    if not base:
        return []

    variants = {base}
    variants.update(AUTHOR_ALIAS_MAP.get(base, []))

    tokens = base.split()
    if len(tokens) >= 2:
        initials = " ".join(token[0] for token in tokens[:-1] if token) + f" {tokens[-1]}"
        variants.add(initials.strip())
        variants.add(initials.replace(" ", ""))

    return [variant for variant in variants if variant]


def dedupe_preserve_order(values: Iterable[str]) -> List[str]:
    seen = set()
    result = []

    for value in values:
        key = value.lower()
        if key in seen:
            continue
        seen.add(key)
        result.append(value)

    return result


def similarity(left: Optional[str], right: Optional[str]) -> float:
    left_n = normalize_text(left)
    right_n = normalize_text(right)
    if not left_n or not right_n:
        return 0.0

    return SequenceMatcher(None, left_n, right_n).ratio()


def author_similarity(left: Optional[str], right: Optional[str]) -> float:
    left_variants = alias_variants(left)
    right_variants = alias_variants(right)
    if not left_variants or not right_variants:
        return 0.0

    score = 0.0
    for left_variant in left_variants:
        for right_variant in right_variants:
            score = max(score, SequenceMatcher(None, left_variant, right_variant).ratio())

    return score


def contains_tokenized(doc_title: Optional[str], guessed_title: Optional[str]) -> bool:
    doc_norm = normalize_text(doc_title)
    guess_norm = normalize_text(guessed_title)
    if not doc_norm or not guess_norm:
        return False

    return guess_norm in doc_norm or doc_norm in guess_norm


def normalize_isbn(raw: str) -> Optional[str]:
    digits = re.sub(r"[^0-9Xx]", "", raw).upper()
    if len(digits) == 13 and digits.isdigit():
        return digits
    if len(digits) == 10 and re.match(r"^[0-9]{9}[0-9X]$", digits):
        return digits
    return None


def extract_identifiers(text: str) -> Tuple[Optional[str], Optional[str]]:
    isbn = None
    asin = None

    m13 = ISBN13_RE.search(text)
    if m13:
        isbn = normalize_isbn(m13.group(1))

    if not isbn:
        m10 = ISBN10_RE.search(text)
        if m10:
            isbn = normalize_isbn(m10.group(1))

    ma = ASIN_RE.search(text)
    if ma:
        asin = ma.group(1).upper()

    return isbn, asin


def strip_subtitle(title: Optional[str]) -> List[str]:
    if not title:
        return []

    stripped = clean_component(title)
    if not stripped:
        return []

    variants = [stripped]
    for delimiter in [":", " - ", " – ", " — ", ","]:
        if delimiter in stripped:
            left = stripped.split(delimiter)[0].strip()
            if left:
                variants.append(left)

    suffix_removed = SERIES_SUFFIX_RE.sub("", stripped).strip(" -:,")
    if suffix_removed:
        variants.append(suffix_removed)

    return dedupe_preserve_order(variant for variant in variants if variant)


def parse_title_author_from_filename(file_name: str) -> Tuple[Optional[str], Optional[str], Optional[str], Optional[str]]:
    stem = os.path.splitext(file_name)[0]
    isbn, asin = extract_identifiers(stem)
    cleaned = clean_component(stem)

    if not cleaned:
        return None, None, isbn, asin

    if " - " in stem:
        parts = [clean_component(part) for part in stem.split(" - ") if clean_component(part)]
        if len(parts) >= 2:
            author = parts[-1]
            title = " - ".join(parts[:-1])
            return title or None, author or None, isbn, asin

    by_match = AUTHOR_BY_RE.search(cleaned)
    if by_match:
        author = by_match.group(1).strip()
        title = cleaned[:by_match.start()].strip(" -")
        if title and author:
            return title, author, isbn, asin

    return cleaned, None, isbn, asin


def build_app_query(title: str, author: Optional[str]) -> str:
    query = title.lower().strip()
    if author and author.strip():
        query += " " + author.strip()
    return query


def http_json(url: str) -> Dict[str, Any]:
    with urlopen(url, timeout=20) as response:
        return json.loads(response.read().decode("utf-8", errors="replace"))


def openlibrary_search(query: str, limit: int = 10) -> Dict[str, Any]:
    url = f"https://openlibrary.org/search.json?q={quote_plus(query)}&limit={limit}"
    return http_json(url)


def openlibrary_by_isbn(isbn: str) -> Optional[ProviderMatch]:
    url = f"https://openlibrary.org/isbn/{quote_plus(isbn)}.json"

    try:
        data = http_json(url)
    except HTTPError as exc:
        if exc.code == 404:
            return None
        raise

    title = data.get("title")
    authors = data.get("authors") or []
    author = None
    if authors:
        first_author = authors[0]
        if isinstance(first_author, dict):
            author = first_author.get("name")
        else:
            author = first_author

    return ProviderMatch(
        provider="openlibrary",
        title=title or "",
        author=author,
        key=data.get("key") or data.get("ocaid") or f"isbn:{isbn}",
        raw=data,
        isbn_values=[isbn],
        first_publish_year=data.get("publish_date"),
    )


def inventaire_search(query: str, limit: int = 10) -> Dict[str, Any]:
    url = f"https://inventaire.io/api/search?types=works&search={quote_plus(query)}&limit={limit}"
    return http_json(url)


def google_books_search(query: str, limit: int = 10) -> Dict[str, Any]:
    api_key = os.environ.get("GOOGLE_BOOKS_API_KEY", "").strip()
    url = f"https://www.googleapis.com/books/v1/volumes?q={quote_plus(query)}&maxResults={limit}"
    if api_key:
        url += f"&key={quote_plus(api_key)}"
    return http_json(url)


def inventaire_author_from_result(result: Dict[str, Any]) -> Optional[str]:
    label = result.get("label") or ""
    description = result.get("description") or ""

    label_match = INVENTAIRE_LABEL_RE.match(label)
    if label_match:
        tail = label_match.group(2).strip()
        if tail and len(tail.split()) <= 5:
            return tail

    description_match = AUTHOR_DESCRIPTION_RE.search(description)
    if description_match:
        return description_match.group(1).strip()

    return None


def inventaire_title_from_result(result: Dict[str, Any]) -> str:
    label = result.get("label") or ""
    label_match = INVENTAIRE_LABEL_RE.match(label)
    if label_match:
        return label_match.group(1).strip()

    return label


def provider_matches_from_openlibrary(data: Dict[str, Any]) -> List[ProviderMatch]:
    matches = []
    for doc in data.get("docs") or []:
        matches.append(
            ProviderMatch(
                provider="openlibrary",
                title=doc.get("title") or "",
                author=(doc.get("author_name") or [None])[0],
                key=doc.get("key") or doc.get("cover_edition_key") or (doc.get("edition_key") or [None])[0] or "",
                raw=doc,
                isbn_values=doc.get("isbn") or [],
                first_publish_year=doc.get("first_publish_year"),
            )
        )

    return matches


def provider_matches_from_inventaire(data: Dict[str, Any]) -> List[ProviderMatch]:
    matches = []
    for result in data.get("results") or []:
        matches.append(
            ProviderMatch(
                provider="inventaire",
                title=inventaire_title_from_result(result) or "",
                author=inventaire_author_from_result(result),
                key=result.get("uri") or result.get("id") or "",
                raw=result,
                isbn_values=[],
                first_publish_year=None,
            )
        )

    return matches


def provider_matches_from_googlebooks(data: Dict[str, Any]) -> List[ProviderMatch]:
    matches = []
    for item in data.get("items") or []:
        volume = item.get("volumeInfo") or {}
        identifiers = volume.get("industryIdentifiers") or []
        isbn_values = []
        for identifier in identifiers:
            raw = identifier.get("identifier") or ""
            normalized = normalize_isbn(raw)
            if normalized:
                isbn_values.append(normalized)

        title = volume.get("title") or item.get("id") or ""
        authors = volume.get("authors") or []
        author = authors[0] if authors else None

        matches.append(
            ProviderMatch(
                provider="googlebooks",
                title=title,
                author=author,
                key=item.get("id") or "",
                raw=item,
                isbn_values=isbn_values,
                first_publish_year=volume.get("publishedDate"),
            )
        )

    return matches


def binary_available(binary: str) -> bool:
    for directory in os.environ.get("PATH", "").split(os.pathsep):
        candidate = Path(directory) / binary
        if candidate.exists() and os.access(candidate, os.X_OK):
            return True

    return False


def extract_tags_with_ffprobe(file_path: Path) -> Tuple[Optional[str], Optional[str]]:
    if not binary_available("ffprobe"):
        return None, None

    cmd = [
        "ffprobe",
        "-v",
        "error",
        "-show_entries",
        "format_tags=title,artist,album,album_artist,composer",
        "-of",
        "json",
        str(file_path),
    ]

    try:
        completed = subprocess.run(cmd, check=True, capture_output=True, text=True)
        payload = json.loads(completed.stdout or "{}")
    except Exception:
        return None, None

    tags = (payload.get("format") or {}).get("tags") or {}
    title = tags.get("title") or tags.get("album")
    author = tags.get("album_artist") or tags.get("artist")
    return clean_component(title) if title else None, clean_component(author) if author else None


def extract_tags_with_mutagen(file_path: Path) -> Tuple[Optional[str], Optional[str]]:
    if MutagenFile is None:
        return None, None

    try:
        audio = MutagenFile(str(file_path), easy=True)
    except Exception:
        return None, None

    if audio is None or not getattr(audio, "tags", None):
        return None, None

    tags = audio.tags
    title_values = tags.get("title") or tags.get("album") or []
    author_values = tags.get("albumartist") or tags.get("artist") or []

    title = title_values[0] if title_values else None
    author = author_values[0] if author_values else None
    return clean_component(title) if title else None, clean_component(author) if author else None


def extract_embedded_tags(file_path: Path) -> Tuple[Optional[str], Optional[str], str]:
    if file_path.suffix.lower() not in AUDIO_EXTENSIONS:
        return None, None, "not-audio"

    title, author = extract_tags_with_ffprobe(file_path)
    if title or author:
        return title, author, "ffprobe"

    title, author = extract_tags_with_mutagen(file_path)
    if title or author:
        return title, author, "mutagen"

    return None, None, "none"


def discover_targets(root: Path) -> List[FolderCandidate]:
    targets: List[FolderCandidate] = []
    quarantine_markers = {"_dupes", "_unidentified"}

    for dirpath, _, filenames in os.walk(root):
        folder = Path(dirpath)
        rel_parts = folder.relative_to(root).parts
        if any(marker in rel_parts for marker in quarantine_markers):
            continue

        media_files = sorted([file_name for file_name in filenames if Path(file_name).suffix.lower() in MEDIA_EXTENSIONS])
        if not media_files:
            continue

        has_json = "metadata.json" in filenames
        has_opf = any(file_name.lower().endswith(".opf") for file_name in filenames)
        if has_json or has_opf:
            continue

        title_guess = folder.name
        author_guess = folder.parent.name if folder.parent != root else ""
        isbn_guess = None
        asin_guess = None
        tag_title_guess = None
        tag_author_guess = None
        local_identity_source = "path"

        first_media = folder / media_files[0]
        tag_title, tag_author, tag_source = extract_embedded_tags(first_media)
        if tag_title:
            tag_title_guess = tag_title
            title_guess = tag_title
            local_identity_source = tag_source
        if tag_author:
            tag_author_guess = tag_author
            author_guess = tag_author
            local_identity_source = tag_source

        parsed_title, parsed_author, parsed_isbn, parsed_asin = parse_title_author_from_filename(media_files[0])
        if not tag_title_guess and parsed_title:
            title_guess = parsed_title
            local_identity_source = "filename"
        if not tag_author_guess and parsed_author:
            author_guess = parsed_author
            local_identity_source = "filename"

        isbn_guess = parsed_isbn
        asin_guess = parsed_asin

        targets.append(
            FolderCandidate(
                folder=str(folder),
                root=str(root),
                author_guess=clean_component(author_guess),
                title_guess=clean_component(title_guess),
                media_files=media_files,
                isbn_guess=isbn_guess,
                asin_guess=asin_guess,
                tag_title_guess=tag_title_guess,
                tag_author_guess=tag_author_guess,
                local_identity_source=local_identity_source,
            )
        )

    return targets


def title_similarity(local_title: Optional[str], provider_title: Optional[str]) -> float:
    local_variants = strip_subtitle(local_title)
    provider_variants = strip_subtitle(provider_title)
    if not local_variants or not provider_variants:
        return 0.0

    score = 0.0
    for left in local_variants:
        for right in provider_variants:
            score = max(score, similarity(left, right))

    return score


def score_match(candidate: FolderCandidate, match: ProviderMatch, query_strategy: str) -> Tuple[float, str]:
    title_score = title_similarity(candidate.title_guess, match.title)
    author_score = author_similarity(candidate.author_guess, match.author)
    contains_score = 1.0 if contains_tokenized(match.title, candidate.title_guess) else 0.0
    provider_bonus = 0.05 if match.provider == "inventaire" and match.author else 0.0

    isbn_bonus = 0.0
    if candidate.isbn_guess and candidate.isbn_guess in set(match.isbn_values):
        isbn_bonus = 0.25

    author_weight = 0.30 if candidate.author_guess else 0.10
    title_weight = 0.60
    contains_weight = 0.10 if query_strategy != "title_only" else 0.20

    confidence = (title_score * title_weight) + (author_score * author_weight) + (contains_score * contains_weight) + isbn_bonus + provider_bonus
    confidence = min(confidence, 1.0)

    note = (
        f"provider={match.provider}, title={title_score:.2f}, author={author_score:.2f}, "
        f"contains={contains_score:.2f}, isbn_bonus={isbn_bonus:.2f}, provider_bonus={provider_bonus:.2f}"
    )
    return confidence, note


def should_accept(candidate: FolderCandidate, match: ProviderMatch, confidence: float) -> bool:
    title_score = title_similarity(candidate.title_guess, match.title)
    author_score = author_similarity(candidate.author_guess, match.author)

    if candidate.isbn_guess and candidate.isbn_guess in set(match.isbn_values):
        return True

    if title_score >= 0.97 and (not candidate.author_guess or author_score >= 0.80):
        return True

    if title_score >= 0.92 and author_score >= 0.90:
        return True

    if title_score >= 0.88 and author_score >= 0.96:
        return True

    if match.provider == "inventaire" and title_score >= 0.95 and author_score >= 0.85:
        return True

    if confidence >= 0.94:
        return True

    return False


def query_plan(candidate: FolderCandidate) -> List[Tuple[str, str, str]]:
    title_variants = strip_subtitle(candidate.title_guess)
    author_variants = dedupe_preserve_order([candidate.author_guess] + [variant for variant in alias_variants(candidate.author_guess) if variant != normalize_text(candidate.author_guess)])
    plan: List[Tuple[str, str, str]] = []
    seen = set()

    if candidate.isbn_guess:
        item = ("openlibrary", "isbn", candidate.isbn_guess)
        plan.append(item)
        seen.add(item)

    for provider in ["openlibrary", "inventaire", "googlebooks"]:
        for title_variant in title_variants:
            if candidate.author_guess:
                item = (provider, "primary", build_app_query(title_variant, candidate.author_guess))
                if item not in seen:
                    plan.append(item)
                    seen.add(item)

                for alias_variant in author_variants[1:]:
                    alias_author = alias_variant.replace(".", " ")
                    item = (provider, "primary_alias", build_app_query(title_variant, alias_author))
                    if item not in seen:
                        plan.append(item)
                        seen.add(item)

            item = (provider, "title_only", build_app_query(title_variant, None))
            if item not in seen:
                plan.append(item)
                seen.add(item)

        if candidate.author_guess:
            item = (provider, "author_only", build_app_query(candidate.author_guess, None))
            if item not in seen:
                plan.append(item)
                seen.add(item)

    return plan


def write_metadata_json(folder: Path, match: ProviderMatch, confidence: float, accepted_by: str, attempts: List[QueryAttempt], candidate: FolderCandidate) -> None:
    metadata = {
        "title": match.title,
        "authors": [match.author] if match.author else [],
        "isbn": match.isbn_values[0] if match.isbn_values else candidate.isbn_guess,
        "provider": match.provider,
        "provider_key": match.key,
        "provider_confidence": round(confidence, 4),
        "provider_acceptance_strategy": accepted_by,
        "provider_attempts": [asdict(attempt) for attempt in attempts],
        "first_publish_year": match.first_publish_year,
        "local_identity_source": candidate.local_identity_source,
        "local_tag_title": candidate.tag_title_guess,
        "local_tag_author": candidate.tag_author_guess,
    }

    (folder / "metadata.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")


def enrich_root(root: Path, report_dir: Path) -> Dict[str, Any]:
    targets = discover_targets(root)
    report_dir.mkdir(parents=True, exist_ok=True)

    accepted = []
    unresolved = []

    for candidate in targets:
        attempts: List[QueryAttempt] = []
        accepted_match = None
        accepted_confidence = None
        accepted_strategy = None

        for provider, strategy_name, query in query_plan(candidate):
            try:
                if provider == "openlibrary" and strategy_name == "isbn":
                    isbn_match = openlibrary_by_isbn(query)
                    matches = [isbn_match] if isbn_match is not None else []
                    num_found = 1 if isbn_match is not None else 0
                elif provider == "openlibrary":
                    openlibrary_data = openlibrary_search(query)
                    matches = provider_matches_from_openlibrary(openlibrary_data)[:10]
                    num_found = int(openlibrary_data.get("numFound") or 0)
                elif provider == "inventaire":
                    inventaire_data = inventaire_search(query)
                    matches = provider_matches_from_inventaire(inventaire_data)[:10]
                    num_found = int(inventaire_data.get("total") or len(inventaire_data.get("results") or []))
                else:
                    google_data = google_books_search(query)
                    matches = provider_matches_from_googlebooks(google_data)[:10]
                    num_found = int(google_data.get("totalItems") or len(google_data.get("items") or []))
            except (HTTPError, URLError, TimeoutError, ValueError) as exc:
                attempts.append(QueryAttempt(provider, strategy_name, query, 0, False, None, None, None, None, str(exc)))
                continue

            best_match = None
            best_confidence = None
            best_note = None

            for match in matches:
                confidence, note = score_match(candidate, match, strategy_name)
                if best_confidence is None or confidence > best_confidence:
                    best_match = match
                    best_confidence = confidence
                    best_note = note

            is_accepted = False
            match_title = None
            match_author = None
            match_key = None

            if best_match is not None:
                match_title = best_match.title
                match_author = best_match.author
                match_key = best_match.key
                is_accepted = should_accept(candidate, best_match, best_confidence)
                if is_accepted:
                    accepted_match = best_match
                    accepted_confidence = best_confidence
                    accepted_strategy = f"{provider}:{strategy_name}"

            attempts.append(
                QueryAttempt(
                    provider,
                    strategy_name,
                    query,
                    num_found,
                    is_accepted,
                    round(best_confidence, 4) if best_confidence is not None else None,
                    match_title,
                    match_author,
                    match_key,
                    best_note,
                )
            )

            if is_accepted:
                break

        folder = Path(candidate.folder)
        if accepted_match is not None:
            write_metadata_json(folder, accepted_match, accepted_confidence, accepted_strategy, attempts, candidate)
            accepted.append(
                {
                    "candidate": asdict(candidate),
                    "accepted_strategy": accepted_strategy,
                    "confidence": round(accepted_confidence, 4),
                    "match": {
                        "provider": accepted_match.provider,
                        "title": accepted_match.title,
                        "author": accepted_match.author,
                        "key": accepted_match.key,
                    },
                    "attempts": [asdict(attempt) for attempt in attempts],
                }
            )
        else:
            unresolved.append(
                {
                    "candidate": asdict(candidate),
                    "attempts": [asdict(attempt) for attempt in attempts],
                }
            )

    summary = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "root": str(root),
        "targets": len(targets),
        "accepted": len(accepted),
        "unresolved": len(unresolved),
        "providers": ["openlibrary", "inventaire", "googlebooks"],
        "ffprobe_available": binary_available("ffprobe"),
        "mutagen_available": MutagenFile is not None,
    }

    json_path = report_dir / f"{root.name}_live_enrichment_report.json"
    md_path = report_dir / f"{root.name}_live_enrichment_report.md"

    json_path.write_text(json.dumps({"summary": summary, "accepted": accepted, "unresolved": unresolved}, indent=2), encoding="utf-8")

    lines = [
        f"# Live Provider Enrichment Report: {root}",
        "",
        f"- Timestamp (UTC): {summary['timestamp_utc']}",
        f"- Targets: {summary['targets']}",
        f"- Accepted: {summary['accepted']}",
        f"- Unresolved: {summary['unresolved']}",
        f"- Providers: {', '.join(summary['providers'])}",
        f"- ffprobe available: {summary['ffprobe_available']}",
        f"- mutagen available: {summary['mutagen_available']}",
        "",
        "## Accepted",
        "",
    ]

    if accepted:
        for item in accepted:
            lines.append(f"- {item['candidate']['folder']}")
            lines.append(f"  - local guess: {item['candidate']['title_guess']} / {item['candidate']['author_guess']}")
            lines.append(f"  - local source: {item['candidate']['local_identity_source']}")
            lines.append(f"  - provider match: {item['match']['title']} / {item['match']['author']}")
            lines.append(f"  - provider: {item['match']['provider']}")
            lines.append(f"  - strategy: {item['accepted_strategy']}")
            lines.append(f"  - confidence: {item['confidence']}")
    else:
        lines.append("- None")

    lines.extend(["", "## Unresolved", ""])
    if unresolved:
        for item in unresolved:
            lines.append(f"- {item['candidate']['folder']}")
            lines.append(f"  - local guess: {item['candidate']['title_guess']} / {item['candidate']['author_guess']}")
            lines.append(f"  - local source: {item['candidate']['local_identity_source']}")
            for attempt in item['attempts'][:6]:
                lines.append(
                    f"  - {attempt['provider']}:{attempt['strategy']}: q={attempt['query']} "
                    f"numFound={attempt['num_found']} confidence={attempt['confidence']} "
                    f"match={attempt['match_title']} / {attempt['match_author']}"
                )
    else:
        lines.append("- None")

    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"Root: {root}")
    print(f"Targets: {summary['targets']}")
    print(f"Accepted: {summary['accepted']}")
    print(f"Unresolved: {summary['unresolved']}")
    print(f"Providers: {', '.join(summary['providers'])}")
    print(f"Report JSON: {json_path}")
    print(f"Report MD: {md_path}")

    return summary


def main() -> int:
    parser = argparse.ArgumentParser(description="Enrich missing media metadata via live provider responses")
    parser.add_argument("--root", required=True)
    parser.add_argument("--report-dir", default="/opt/Bibliophilarr/_artifacts/live-provider-enrich")
    args = parser.parse_args()

    enrich_root(Path(args.root), Path(args.report_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
