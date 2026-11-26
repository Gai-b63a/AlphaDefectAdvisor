"""Build the private Windows x64 CPython/CPU inference payload for the Workshop."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[2]
WEIGHT_HASH = "78ba58312335b1a49823fdaf6aa263ee46f9dae46b5d4753bc609eb671588f20"


def write_model(out, weight):
    for f in sorted((ROOT / "upstream/python").glob("*/*.py")):
        out.write(f, f.relative_to(ROOT).as_posix())
    for rel in ("upstream/tools/ability_test.py", "upstream/engine/game_version.txt", "advisor/companion.py"):
        out.write(ROOT / rel, rel)
    out.write(weight, weight.name)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--wheelhouse", type=Path)
    parser.add_argument("--crt", type=Path)
    args = parser.parse_args()
    base = Path(sys.base_prefix)
    if sys.version_info[:2] != (3, 12) or not (base / "python312.dll").is_file() or sys.maxsize < 2**32:
        raise SystemExit("Build with Windows x64 CPython 3.12.")
    crt = args.crt
    if crt is None:
        candidates = sorted(p for key in ("ProgramFiles", "ProgramFiles(x86)")
                            for p in Path(os.environ[key]).glob(
                                "Microsoft Visual Studio/*/*/VC/Redist/MSVC/*/x64/Microsoft.VC*.CRT"))
        if not candidates:
            raise SystemExit("Specify the Visual C++ x64 redistributable CRT directory with -CrtDir.")
        crt = candidates[-1]
    if not (crt / "msvcp140.dll").is_file():
        raise SystemExit("Missing x64 redistributable CRT.")
    weight = ROOT / "ironclad_ema_v24782.pt"
    with weight.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != WEIGHT_HASH:
            raise SystemExit("Unexpected weights-v6 checkpoint.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.output.parent) as scratch:
        scratch = Path(scratch)
        wheels = scratch / "wheels"
        cmd = [sys.executable, "-I", "-m", "pip", "download", "--disable-pip-version-check",
               "--only-binary=:all:", "--require-hashes", "--dest", str(wheels),
               "-r", str(ROOT / ".github/runtime/runtime-requirements.txt")]
        if args.wheelhouse:
            cmd += ["--no-index", "--find-links", str(args.wheelhouse)]
        subprocess.run(cmd, check=True)
        payload = scratch / "runtime.zip"
        with ZipFile(payload, "w", ZIP_DEFLATED, compresslevel=3) as out:
            for name in ("python.exe", "python3.dll", "python312.dll", "LICENSE.txt"):
                out.write(base / name, "python/" + name)
            for f in sorted(crt.glob("*.dll")):
                out.write(f, "python/" + f.name)
            for f in sorted((base / "DLLs").iterdir()):
                if f.suffix.lower() in {".dll", ".pyd"} and not f.name.startswith(("_test", "_tkinter", "_ctypes_test", "tcl", "tk86")):
                    out.write(f, "python/DLLs/" + f.name)
            for f in sorted((base / "Lib").rglob("*.py")):
                rel = f.relative_to(base / "Lib")
                if not {"site-packages", "test", "tests", "ensurepip", "idlelib", "tkinter"}.intersection(rel.parts):
                    out.write(f, "python/Lib/" + rel.as_posix())
            out.writestr("python/python312._pth", ".\nDLLs\nLib\nLib/site-packages\n")
            for wheel in sorted(wheels.glob("*.whl")):
                with ZipFile(wheel) as source:
                    for member in source.infolist():
                        if member.is_dir():
                            continue
                        dest = member.filename
                        if ".data/" in dest:
                            category, _, dest = dest.split(".data/", 1)[1].partition("/")
                            if category not in ("purelib", "platlib"):
                                continue
                        with source.open(member) as src, out.open("python/Lib/site-packages/" + dest, "w") as dst:
                            shutil.copyfileobj(src, dst, 1024 * 1024)
            write_model(out, weight)
        os.replace(payload, args.output)


if __name__ == "__main__":
    main()
