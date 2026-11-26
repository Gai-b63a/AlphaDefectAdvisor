"""Replay the A10 opening and retail v0.114 card and potion observations against the installed worker."""
import base64
import gzip
import json
import math
from pathlib import Path
import subprocess
import sys


def main():
    fixtures = json.loads(Path(__file__).with_name("seed-24VQ8UQR7J.json").read_text())
    fixtures.extend(json.loads(Path(__file__).with_name("card-selection-v114.json").read_text()))
    fixtures.extend(json.loads(Path(__file__).with_name("potion-rewards-v114.json").read_text()))
    requests = []
    for scene in fixtures:
        frame = gzip.decompress(base64.b64decode(scene["frame"]))
        requests.append(json.dumps({"frame": base64.b64encode(frame).decode(),
                                    "labels": list(range(scene["count"]))}))
    result = subprocess.run(sys.argv[1:], input="\n".join(requests) + "\n", text=True,
                            stdout=subprocess.PIPE, timeout=300, check=True)
    lines = result.stdout.splitlines()
    if len(lines) != len(fixtures):
        raise ValueError(f"Expected {len(fixtures)} decisions, received {len(lines)}")
    for scene, line in zip(fixtures, lines):
        ranked = json.loads(line)
        if any(type(index) is not int or not 0 <= index < scene["count"] for index, _ in ranked):
            raise ValueError(f"Invalid action index: {ranked}")
        actual = [(str(index), probability) for index, probability in ranked]
        expected = scene["expected"]
        # Identical card copies can exchange rank across CPU backends. Their target must still match.
        equivalents = {label: group[0] for group in scene.get("identical_actions", []) for label in group}
        if len(actual) != len(expected) or len({label for label, _ in actual}) != len(actual):
            raise ValueError(f"Invalid top three: {actual}")
        scores = [p for _, p in actual]
        if scores != sorted(scores, reverse=True) or not all(0 <= p <= 1 for p in scores):
            raise ValueError(f"Invalid probabilities: {actual}")
        for (label, probability), (reference, target) in zip(actual, expected):
            # CPU backends can split probability differently between indistinguishable copies.
            tolerance = 0.01 if reference in equivalents else 0.0001
            if equivalents.get(label, label) != equivalents.get(reference, reference) or not math.isclose(probability, target, abs_tol=tolerance):
                raise ValueError(f"{scene['scene']}: expected {expected}, received {actual}")
        for group in scene.get("identical_actions", []):
            if set(group).issubset(label for label, _ in expected):
                expected_mass = sum(p for label, p in expected if label in group)
                actual_mass = sum(p for label, p in actual if label in group)
                if not math.isclose(expected_mass, actual_mass, abs_tol=0.001):
                    raise ValueError(f"{scene['scene']}: probability for {group} was {actual_mass}, expected {expected_mass}")
        print(f"{scene['scene']}: matched {len(expected)} ranked actions")


if __name__ == "__main__":
    main()
