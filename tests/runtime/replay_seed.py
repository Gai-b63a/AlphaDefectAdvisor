"""Replay numeric observations from the v0.113.1 A10 seed against the installed worker."""
import base64
import gzip
import json
import math
from pathlib import Path
import subprocess
import sys


def main():
    fixtures = json.loads(Path(__file__).with_name("seed-24VQ8UQR7J.json").read_text())
    requests = []
    for scene in fixtures:
        frame = gzip.decompress(base64.b64decode(scene["frame"]))
        requests.append(json.dumps({"frame": base64.b64encode(frame).decode(),
                                    "labels": [str(i) for i in range(scene["count"])]}))
    result = subprocess.run(sys.argv[1:], input="\n".join(requests) + "\n", text=True,
                            stdout=subprocess.PIPE, timeout=300, check=True)
    lines = result.stdout.splitlines()
    if len(lines) != len(fixtures):
        raise ValueError(f"Expected {len(fixtures)} decisions, received {len(lines)}")
    for scene, line in zip(fixtures, lines):
        actual = json.loads(line)
        expected = scene["expected"]
        if len(actual) != len(expected) or any(
            label != ref or not math.isclose(probability, target, abs_tol=0.0001)
            for (label, probability), (ref, target) in zip(actual, expected)
        ):
            raise ValueError(f"{scene['scene']}: expected {expected}, received {actual}")
        print(f"{scene['scene']}: matched {len(expected)} ranked actions")


if __name__ == "__main__":
    main()
