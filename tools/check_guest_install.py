"""Inspect a guest's Steam library from the tester's machine (read-only)."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from zipfile import ZipFile, BadZipFile

APP = "2868840"
ITEM = "3845626411"


def inspect(steamapps, version, platform):
    report = {"platform": platform, "expected_version": version, "item": ITEM,
              "errors": [], "source": str(steamapps)}
    errors = report["errors"]
    manifest = steamapps / f"appmanifest_{APP}.acf"
    if manifest.is_file():
        match = re.search(r'"buildid"\s+"(\d+)"', manifest.read_text(encoding="utf-8-sig"))
        if match:
            report["steam_build_id"] = match[1]
        else:
            errors.append("Steam build ID missing")
    else:
        errors.append("Game manifest missing")
    folder = steamapps / "workshop" / "content" / APP / ITEM
    try:
        mod = json.loads((folder / "AlphaDefectAdvisor.json").read_text(encoding="utf-8-sig"))
        report["installed_version"] = mod.get("version")
        if mod.get("id") != "AlphaDefectAdvisor" or mod.get("version") != version:
            errors.append("Mod ID or version mismatch")
        for name in ("AlphaDefectAdvisor.dll", "runtime.zip"):
            with (folder / name).open("rb") as f:
                report[name + "_sha256"] = hashlib.file_digest(f, "sha256").hexdigest()
        with ZipFile(folder / "runtime.zip") as archive:
            names = set(archive.namelist())
            compatible = platform + ".zip" in names
            if platform == "win-x64" and "python/python.exe" in names:
                compatible = True  # Published Windows-only v0.2.1 layout.
            if not compatible:
                errors.append("Runtime for the selected guest is missing")
    except (OSError, ValueError, BadZipFile) as exc:
        errors.append(str(exc))
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("steamapps", type=Path)
    parser.add_argument("version")
    parser.add_argument("platform", choices=["win-x64", "osx-arm64"])
    args = parser.parse_args()
    result = inspect(args.steamapps, args.version, args.platform)
    print(json.dumps(result, indent=2))
    sys.exit(bool(result["errors"]))
