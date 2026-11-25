"""Check the Workshop workspace and the model shipped in its private runtime."""
import argparse
import hashlib
import json
import shutil
import tempfile
from pathlib import Path, PurePosixPath
from zipfile import ZipFile

from sys import path as import_path
import_path.insert(0, str(Path(__file__).resolve().parent))
from build_runtime import WEIGHT_HASH


def verify_runtime(archive, platform=None):
    with ZipFile(archive) as payload:
        names = set(payload.namelist())
        if platform is None and "win-x64.zip" in names:
            if sorted(payload.namelist()) != ["osx-arm64.zip", "win-x64.zip"]:
                raise ValueError("Expected exactly two platform archives")
            for rid in ("win-x64", "osx-arm64"):
                with tempfile.TemporaryFile() as inner, payload.open(rid + ".zip") as source:
                    shutil.copyfileobj(source, inner)
                    verify_runtime(inner, rid)
            return
        required = {"advisor/companion.py", "ironclad_ema_v24782.pt", "upstream/tools/ability_test.py"}
        if platform == "osx-arm64":
            required.update({"python/bin/python3.12", "python/lib/libpython3.12.dylib",
                             "python/lib/python3.12/site-packages/torch/lib/libtorch_cpu.dylib"})
        else:
            required.update({"python/python.exe", "python/python312._pth", "python/msvcp140.dll",
                             "python/LICENSE.txt", "python/Lib/site-packages/torch/lib/torch_cpu.dll"})
        if required - names:
            raise ValueError(f"Missing runtime files: {sorted(required - names)}")
        if len(names) != len(payload.infolist()):
            raise ValueError("Duplicate runtime entry")
        for name in names:
            parts = PurePosixPath(name.lower()).parts
            if (".." in parts or "\\" in name or ":" in name or name.startswith("/")
                    or "catalog" in parts or parts[-1] in {"sts2.dll", "godotsharp.dll", "enchant_effects.json"}
                    or name.lower().endswith(".pck")):
                raise ValueError(f"Unexpected runtime entry: {name}")
        if bad := payload.testzip():
            raise ValueError(f"Damaged runtime entry: {bad}")
        with payload.open("ironclad_ema_v24782.pt") as weights:
            if hashlib.file_digest(weights, "sha256").hexdigest() != WEIGHT_HASH:
                raise ValueError("Unexpected model checksum")


def verify_workspace(root):
    content = root / "content"
    expected = {"AlphaDefectAdvisor.dll", "AlphaDefectAdvisor.json", "LICENSE", "README.md",
                "AlphaDefect-LICENSE.txt", "AlphaDefect-NOTICE.md", "runtime.zip"}
    if {p.name for p in content.iterdir()} != expected or not all((content / n).is_file() for n in expected):
        raise ValueError("Unexpected Workshop content file list")
    manifest = json.loads((content / "AlphaDefectAdvisor.json").read_text(encoding="utf-8-sig"))
    config = json.loads((root / "workshop.json").read_text(encoding="utf-8-sig"))
    if manifest["id"] != "AlphaDefectAdvisor" or not manifest["version"]:
        raise ValueError("Invalid mod manifest")
    if config["visibility"] != "public" or not all(config.get(k) for k in ("title", "description", "changeNote")):
        raise ValueError("Incomplete public Workshop metadata")
    image = (root / "image.png").read_bytes()
    if not image.startswith(b"\x89PNG\r\n\x1a\n") or len(image) >= 1_000_000:
        raise ValueError("Expected a PNG preview under 1 MB")
    verify_runtime(content / "runtime.zip")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    option = parser.add_mutually_exclusive_group(required=True)
    option.add_argument("--runtime", type=Path)
    option.add_argument("--workspace", type=Path)
    args = parser.parse_args()
    if args.runtime:
        verify_runtime(args.runtime)
    else:
        verify_workspace(args.workspace)
    print("Workshop verification passed")
