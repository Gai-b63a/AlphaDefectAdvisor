import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("ci_plan", Path(__file__).resolve().parents[1] / ".github/ci/plan.py")
plan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plan)


class WorkflowChecks(unittest.TestCase):
    def test_pr_scopes(self):
        cases = [
            (["README.md"], (False, False, 0)),
            (["tools/Play-Local.ps1"], (True, False, 0)),
            (["advisor/locales/ja.json"], (False, True, 0)),
            (["tools/Update-Workshop.ps1", "advisor/companion.py"], (True, True, 2)),
            ([".github/runtime/build_macos_runtime.py"], (False, False, 1)),
            ([".github/workflows/workshop.yml"], (True, True, 2)),
        ]
        for paths, expected in cases:
            with self.subTest(paths=paths):
                result = plan.select("pull_request", "refs/pull/1/merge", paths, (0, 2, 8))
                self.assertFalse(result["publish"])
                self.assertEqual((result["scripts"], result["advice"], len(result["matrix"]["include"])), expected)
                if expected[2] == 1:
                    self.assertEqual(result["matrix"]["include"][0]["rid"], "osx-arm64")

    def test_version_and_manual_gates(self):
        for event, ref, before, release, runtime in [
            ("push", "main", (0, 2, 7), True, True),
            ("push", "main", (0, 2, 8), False, False),
            ("push", "main", (0, 2, 9), False, False),
            ("workflow_dispatch", "main", None, True, True),
            ("workflow_dispatch", "feature", None, False, True),
        ]:
            result = plan.select(event, "refs/heads/" + ref, [], (0, 2, 8), before)
            self.assertEqual((result["publish"], result["runtime"]), (release, runtime))
