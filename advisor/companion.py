import hashlib
import json
import contextlib
import sys
import base64
from pathlib import Path

# Windows native loaders need extended paths when the private cache is deep in a user profile.
if sys.platform == "win32":
    for index, directory in enumerate(sys.path):
        if Path(directory).name == "site-packages" and not directory.startswith("\\\\?\\"):
            sys.path[index] = ("\\\\?\\UNC\\" + directory[2:] if directory.startswith("\\\\")
                               else "\\\\?\\" + directory)

import numpy as np
import torch

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT / "upstream/python"), str(ROOT / "upstream/tools")]
from ability_test import load_net
from sts2env.protocol import parse
from sts2rl.actor import MODE_RUN
from sts2rl.features import Ragged, collate_ragged

@torch.inference_mode()
def rank(net, frame, labels):
    obs = parse(frame)
    if obs.header[0] != 28 or len(labels) != obs.num_actions or not labels:
        raise ValueError("候補と観測形式が一致しません")
    batch = collate_ragged(Ragged.from_obs([obs], np.array([MODE_RUN]))).to("cpu")
    logits, _ = net(batch)
    probs = logits[0, :len(labels)].float().softmax(-1)
    return [(labels[i], float(probs[i])) for i in torch.argsort(probs, descending=True, stable=True).tolist()]

def main():
    torch.set_num_threads(2)
    checkpoint = ROOT / "ironclad_ema_v24782.pt"
    with checkpoint.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != "78ba58312335b1a49823fdaf6aa263ee46f9dae46b5d4753bc609eb671588f20":
        raise ValueError("公式 weights-v6 のアイアンクラッド用チェックポイントを指定してください")
    with contextlib.redirect_stdout(sys.stderr):
        net, _ = load_net(str(checkpoint))

    for line in sys.stdin:
        request = json.loads(line)
        frame = base64.b64decode(request["frame"], validate=True)
        ranked = rank(net, frame, request["labels"])[:3]
        print(json.dumps(ranked, ensure_ascii=True, allow_nan=False), flush=True)


if __name__ == "__main__":
    main()

