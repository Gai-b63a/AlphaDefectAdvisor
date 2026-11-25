import base64
import json
import os
import sys
import tempfile
import time
import unittest
from concurrent.futures import Future
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "advisor"))
from live_advice import LiveAdvice, read_snapshot


class ControlledExecutor:
    def submit(self, fn, *args):
        self.latest = Future()
        self.latest.set_running_or_notify_cancel()
        return self.latest


class LiveAdviceTests(unittest.TestCase):
    def test_finished_old_decision_is_never_returned_after_move_or_disconnect(self):
        executor = ControlledExecutor()
        advice = LiveAdvice(executor, None)
        a = {"status":"ok", "frame":base64.b64encode(b"A").decode(), "labels":["Strike"]}
        b = dict(a, labels=["Defend"])
        self.assertIsNone(advice.update(a))
        old = executor.latest
        self.assertIsNone(advice.update(b))
        old.set_result(["Strike"])
        self.assertIsNone(advice.update(b))
        executor.latest.set_result(["Defend"])
        self.assertEqual(advice.update(b), ["Defend"])
        self.assertIsNone(advice.update({"status":"waiting"}))
        self.assertIsNone(advice.update(b))

    def test_missing_stale_and_fresh_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "live.json"
            self.assertNotEqual(read_snapshot(path)["status"], "ok")
            data = {"status":"ok"}
            path.write_text(json.dumps(data), encoding="utf-8")
            self.assertEqual(read_snapshot(path), data)
            past = time.time() - 10
            os.utime(path, (past, past))
            self.assertNotEqual(read_snapshot(path)["status"], "ok")
