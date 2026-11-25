"""Keep only the recommendation for the most recently observed decision."""
import base64
import json
import os
import time


def read_snapshot(path):
    try:
        with path.open("rb") as stream:
            if time.time() - os.fstat(stream.fileno()).st_mtime <= 3:
                return json.load(stream)
    except FileNotFoundError:
        pass
    return {"status": "ゲームからの更新を待っています"}


class LiveAdvice:
    def __init__(self, executor, predict):
        self.executor = executor
        self.predict = predict
        self.snapshot = None
        self.future = None

    def clear(self):
        if self.future is not None:
            self.future.cancel()
        self.snapshot = None
        self.future = None

    def update(self, snapshot):
        if snapshot.get("status") != "ok":
            self.clear()
            return None
        if snapshot != self.snapshot:
            self.clear()
            frame = base64.b64decode(snapshot["frame"], validate=True)
            self.future = self.executor.submit(self.predict, frame, snapshot["labels"])
            self.snapshot = snapshot
        if self.future.done():
            return self.future.result()
        return None
