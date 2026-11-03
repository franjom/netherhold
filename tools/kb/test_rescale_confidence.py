# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""python -m unittest discover -s tools/kb -p test_rescale_confidence.py"""
import unittest

from rescale_confidence import rescale

RECORD = """address: 0x0050EA70
name: WorldLog_Dump
role: Prints every world field.
confidence: {confidence}
status: READ
evidence: docs/research/debug-dumpers.md

Body.

## History
- 2026-09-30 claude: created.
"""


class RescaleTests(unittest.TestCase):
    def test_old_words_move_and_gain_a_history_line(self):
        for old, new in (("CONFIRMED", "STRONGLY INFERRED"), ("LIKELY", "HYPOTHESIS"), ("GUESS", "HYPOTHESIS")):
            out = rescale(RECORD.format(confidence=old), "claude", "2026-11-03")
            self.assertIn(f"confidence: {new}\n", out)
            self.assertIn(f"- 2026-11-03 claude: Confidence {old} is {new} on the one scale (ADR-0642);", out)
            self.assertTrue(out.endswith("unchanged.\n"))

    def test_a_second_run_changes_nothing(self):
        once = rescale(RECORD.format(confidence="LIKELY"), "claude", "2026-11-03")
        self.assertIsNone(rescale(once, "claude", "2026-11-03"))

    def test_a_record_the_reference_keeps_confirmed_is_left_alone(self):
        self.assertIsNone(rescale(RECORD.format(confidence="CONFIRMED"), "claude", "2026-11-03", keep_confirmed=True))

    def test_a_kept_confirmed_record_that_was_moved_once_is_never_demoted(self):
        moved = RECORD.format(confidence="CONFIRMED") + "- 2026-11-03 claude: kept CONFIRMED (ADR-0642).\n"
        self.assertIsNone(rescale(moved, "claude", "2026-11-04"))

    def test_new_scale_words_are_left_alone(self):
        for word in ("STRONGLY INFERRED", "HYPOTHESIS", "UNKNOWN", "DISPUTED"):
            self.assertIsNone(rescale(RECORD.format(confidence=word), "claude", "2026-11-03"))


if __name__ == "__main__":
    unittest.main()
