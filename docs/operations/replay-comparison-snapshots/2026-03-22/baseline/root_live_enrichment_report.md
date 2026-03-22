# Live Provider Enrichment Report: _artifacts/replay-baseline-2026-03-22-001214/root

- Timestamp (UTC): 2026-03-22T00:12:30.109765+00:00
- Discovered targets before sampling: 4
- Targets: 4
- Accepted: 1
- Unresolved: 3
- Sample size requested: 64
- Sample seed: 20260316
- Providers: openlibrary, inventaire, googlebooks
- ffprobe available: True
- mutagen available: True

## Accepted

- _artifacts/replay-baseline-2026-03-22-001214/root/audiobooks/Octavia Butler - Kindred
  - local guess: kindred / audiobooks
  - local source: filename
  - provider match: Kindred / A Graphic Novel Adaptation
  - provider: inventaire
  - cover provider: inventaire
  - cover url: /img/entities/08b54f6a7e516cfd56fbb885ccb913022de2a4e0
  - strategy: inventaire:title_only
  - confidence: 0.9543

## Unresolved

- _artifacts/replay-baseline-2026-03-22-001214/root/audiobooks/Ursula K Le Guin - A Wizard of Earthsea
  - local guess: earthsea / audiobooks
  - local source: filename
  - openlibrary:primary: q=earthsea audiobooks numFound=0 confidence=None match=None / None
  - openlibrary:title_only: q=earthsea numFound=54 confidence=0.6261 match=Tales from Earthsea / Ursula K. Le Guin
  - openlibrary:author_only: q=audiobooks numFound=13560 confidence=0.3375 match=La Peste / Albert Camus
  - inventaire:primary: q=earthsea audiobooks numFound=16 confidence=0.4556 match=Tales from Earthsea / None
  - inventaire:title_only: q=earthsea numFound=9 confidence=0.5556 match=Tales from Earthsea / None
  - inventaire:author_only: q=audiobooks numFound=7 confidence=0.2143 match=News That Stays News (Faber Penguin audiobooks) / None
- _artifacts/replay-baseline-2026-03-22-001214/root/ebooks/Frank Herbert - Dune
  - local guess: dune / ebooks
  - local source: filename
  - openlibrary:primary: q=dune ebooks numFound=41 confidence=0.4036 match=Sandworms of Dune / Kevin J. Anderson
  - openlibrary:title_only: q=dune numFound=48711 confidence=0.8857 match=Dune / Frank Herbert
  - openlibrary:author_only: q=ebooks numFound=23848 confidence=0.2457 match=Bleak House / Charles Dickens
  - inventaire:primary: q=dune ebooks numFound=3514 confidence=0.8447 match=Dune / House Corrino
  - inventaire:title_only: q=dune numFound=3506 confidence=0.9357 match=Dune / Frank Herbert
  - inventaire:author_only: q=ebooks numFound=12 confidence=0.24 match=Demons / None
- _artifacts/replay-baseline-2026-03-22-001214/root/ebooks/Isaac Asimov - Foundation
  - local guess: foundation / ebooks
  - local source: filename
  - openlibrary:primary: q=foundation ebooks numFound=154 confidence=0.2561 match=Orthodoxy / Gilbert Keith Chesterton
  - openlibrary:title_only: q=foundation numFound=170815 confidence=0.8462 match=Foundation / Isaac Asimov
  - openlibrary:author_only: q=ebooks numFound=23848 confidence=0.2443 match=The Jewel of Seven Stars / Bram Stoker
  - inventaire:primary: q=foundation ebooks numFound=2063 confidence=0.7962 match=Foundation / Isaac Asimov
  - inventaire:title_only: q=foundation numFound=2052 confidence=0.8962 match=Foundation / Isaac Asimov
  - inventaire:author_only: q=ebooks numFound=12 confidence=0.225 match=Demons / None
