"""Check the packaged worker against the recorded v6 A10 opening and an alternate play."""
import base64
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.getenv("ADVISOR_MODEL_TEST"), "requires the local model and headless game")
class WorkerIntegrationTest(unittest.TestCase):
    def test_new_decision_after_playing_defend(self):
        sys.path.insert(0, str(ROOT / "upstream/python"))
        from sts2env.env import Sts2Env
        from sts2env.protocol import parse

        env = Sts2Env(labels=True)
        self.addCleanup(env._proc.p.stdout.close)
        self.addCleanup(env._proc.p.stdin.close)
        self.addCleanup(env.close)
        check_project = ROOT / "tests/runtime/RuntimeCheck.csproj"
        subprocess.run(["dotnet", "build", str(check_project), "-c", "Release", "--nologo", "-p:NuGetAudit=false"], check=True)
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        package = Path(temp.name) / "workshop" / "2868840" / "test-item"
        shutil.copytree(ROOT / "dist/workshop/content", package)
        child_env = dict(os.environ, ALPHADEFECT_HOME="X:/unavailable", ALPHADEFECT_PYTHON="X:/missing.exe",
                         PYTHONHOME="X:/no-system-python", PYTHONPATH="X:/no-site-packages")
        worker = subprocess.Popen(
            ["dotnet", str(check_project.parent / "bin/Release/net9.0/RuntimeCheck.dll"),
             str(package), os.getenv("ADVISOR_TEST_TABLES", str(ROOT / "upstream")), str(Path(temp.name) / "新規環境")],
            cwd=temp.name, env=child_env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8",
        )
        try:
            request = dict(op="reset", mode="run", character="IRONCLAD", ascension=10,
                           seed="24VQ8UQR7J", labels=True, obs_format=28, route_summary=True)
            for count, expected, probability in [
                (3, "LARGE_CAPSULE", 0.9486), (3, "Monster", 0.9084),
                (8, "STRIKE_IRONCLAD", 0.4697), (7, "STRIKE_IRONCLAD", 0.4795),
            ]:
                env._proc.send(request)
                frame = env._proc.recv()
                labels = parse(frame).labels
                self.assertEqual(len(labels), count)
                worker.stdin.write(json.dumps({"frame": base64.b64encode(frame).decode(), "labels": labels}) + "\n")
                worker.stdin.flush()
                rows = json.loads(worker.stdout.readline())
                self.assertEqual(len(rows), 3)
                self.assertTrue(all(name in labels for name, _ in rows))
                self.assertIn(expected, rows[0][0])
                self.assertAlmostEqual(rows[0][1], probability, places=4)
                action = next(i for i, name in enumerate(labels) if "DEFEND_IRONCLAD" in name) if count == 8 else labels.index(rows[0][0])
                request = dict(op="step", action=action, labels=True)
        finally:
            worker.stdin.close()
            try:
                self.assertEqual(worker.wait(timeout=30), 0)
            finally:
                worker.kill()
                worker.stdout.close()

