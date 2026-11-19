import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from tempfile import TemporaryDirectory

from release_entry_gate import evaluate_gate


class ReleaseEntryGateTests(unittest.TestCase):
    def evaluate(self, text, offset=0):
        with TemporaryDirectory() as scratch:
            directory = Path(scratch)
            day = (datetime.now(timezone.utc) + timedelta(days=offset)).date().isoformat()
            (directory / f"{day}.md").write_text(text, encoding="utf-8")
            return evaluate_gate("Metadata", directory, "Verdict", 7, ["pass"])

    def test_accepts_explicit_pass_with_notes(self):
        self.assertTrue(self.evaluate("- Verdict: PASS (3 provider requests)").passed)

    def test_rejects_negative_verdicts_that_mention_pass(self):
        for state in ("NOT PASS", "BLOCKED until tests pass", "PASSING"):
            with self.subTest(state=state):
                self.assertFalse(self.evaluate(f"Verdict: {state}").passed)

    def test_uses_only_the_designated_verdict_field(self):
        result = self.evaluate("Overall threshold verdict: PASS\nVerdict: BLOCKED")
        self.assertEqual(result.verdict, "BLOCKED")
        self.assertFalse(result.passed)

    def test_rejects_stale_and_future_snapshots(self):
        for offset in (-8, 1):
            with self.subTest(offset=offset):
                self.assertFalse(self.evaluate("Verdict: PASS", offset).passed)
