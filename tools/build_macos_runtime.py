"""Package uv's relocatable macOS arm64 CPython and a private, hash-locked wheel set."""
import argparse
import platform
from pathlib import Path
import subprocess
import sys
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_runtime import ROOT, write_model


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if sys.platform != "darwin" or platform.machine() != "arm64" or sys.version_info[:2] != (3, 12):
        raise SystemExit("Run with the standalone macOS arm64 CPython 3.12 interpreter.")
    base = Path(sys.base_prefix)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.output.parent) as temp:
        wheels = Path(temp) / "packages"
        subprocess.run(["uv", "pip", "install", "--python", sys.executable, "--target", str(wheels),
                        "--only-binary", ":all:", "--require-hashes", "-r", str(ROOT / "tools/runtime-requirements.txt")],
                       check=True)
        with ZipFile(args.output, "w", ZIP_DEFLATED, compresslevel=3) as out:
            out.write(base / "bin/python3.12", "python/bin/python3.12")
            for part in ("lib", "licenses"):
                for file in sorted((base / part).rglob("*")):
                    rel = file.relative_to(base)
                    if file.is_file() and not {"site-packages", "__pycache__"}.intersection(rel.parts) and file.suffix != ".a":
                        out.write(file, "python/" + rel.as_posix())
            for file in sorted(wheels.rglob("*")):
                if file.is_file():
                    out.write(file, "python/lib/python3.12/site-packages/" + file.relative_to(wheels).as_posix())
            write_model(out, ROOT / "ironclad_ema_v24782.pt")


if __name__ == "__main__":
    main()
