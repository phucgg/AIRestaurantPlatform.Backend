"""Checks for the fail-safe CI reporting contract using isolated synthetic TRX files."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("test-report.py").resolve()
NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


class ReportTests(unittest.TestCase):
    def run_report(self, results=None, minimum=1, failure=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "ci").mkdir()
            (root / "artifacts").mkdir()
            (root / "ci/required-tests.txt").write_text(f"BusinessTest|{minimum}\n")
            if results is not None:
                (root / "artifacts/result.trx").write_text(
                    f'<TestRun xmlns="{NS}"><Results>{results}</Results></TestRun>')
            env = dict(os.environ, GITHUB_ACTIONS="true")
            env.pop("GITHUB_STEP_SUMMARY", None)
            for stage in ("setup", "database", "restore", "build", "test"):
                env[stage.upper() + "_OUTCOME"] = "failure" if stage == failure else "success"
            if failure:
                (root / f"artifacts/{failure}.log").write_text("Actual command error: simulated diagnostic")
            result = subprocess.run([sys.executable, str(SCRIPT), "--verify"], cwd=root, env=env,
                                    capture_output=True, text=True)
            return result.returncode, (root / "artifacts/summary.md").read_text(encoding="utf-8")

    def test_all_required_cases_pass(self):
        code, summary = self.run_report('<UnitTestResult testName="Suite.BusinessTest" outcome="Passed"/>')
        self.assertEqual(0, code)
        self.assertIn("**PASS**", summary)

    def test_no_tests_is_failure(self):
        code, summary = self.run_report()
        self.assertEqual(1, code)
        self.assertIn("Tests chưa chạy", summary)
        self.assertNotIn("**PASS**", summary)

    def test_failed_test_has_name_message_and_stack(self):
        code, summary = self.run_report('<UnitTestResult testName="Suite.BusinessTest" outcome="Failed">'
            '<Output><ErrorInfo><Message>Actual assertion error</Message><StackTrace>at BusinessTest:42</StackTrace>'
            '</ErrorInfo></Output></UnitTestResult>')
        self.assertEqual(1, code)
        for expected in ("Suite.BusinessTest", "Actual assertion error", "at BusinessTest:42", "fail: 1"):
            self.assertIn(expected, summary)

    def test_skipped_test_is_failure(self):
        code, summary = self.run_report('<UnitTestResult testName="Suite.BusinessTest" outcome="NotExecuted"/>')
        self.assertEqual(1, code)
        self.assertIn("skipped: 1", summary)

    def test_missing_theory_cases_is_failure(self):
        code, summary = self.run_report('<UnitTestResult testName="Suite.BusinessTest(case: 1)" outcome="Passed"/>', minimum=6)
        self.assertEqual(1, code)
        self.assertIn("BusinessTest (1/6)", summary)

    def test_pretest_failure_reports_stage_and_actual_log(self):
        code, summary = self.run_report(failure="restore")
        self.assertEqual(1, code)
        for expected in ("restore: failure", "Actual command error: simulated diagnostic", "Tests chưa chạy"):
            self.assertIn(expected, summary)


if __name__ == "__main__":
    unittest.main()
