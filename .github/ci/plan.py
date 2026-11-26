"""Select CI checks from the PR diff, or compare manifest versions on main."""
import json
import os
from pathlib import Path
import re
import subprocess

MANIFEST = "advisor/AlphaDefectAdvisor.json"
PLATFORMS = {"windows-2022": "win-x64", "macos-15": "osx-arm64"}


def version(text):
    value = json.loads(text)["version"]
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", value, re.ASCII):
        raise ValueError(f"Invalid release version: {value!r}")
    return tuple(map(int, value.split(".")))


def select(event, ref, paths, current, previous=None):
    manual = event == "workflow_dispatch"
    publish = ref == "refs/heads/main" and (manual or event == "push" and current > previous)
    full = manual or publish or ".github/workflows/workshop.yml" in paths or ".github/ci/plan.py" in paths
    # Ordinary main updates have already been checked on the PR.
    paths = paths if event == "pull_request" else []
    scripts = full or any(p.startswith(("tools/", "workshop/")) or p.endswith(".ps1") for p in paths)
    advice = full or any(p.startswith(("advisor/", "tests/")) and not p.endswith(".ps1") for p in paths)
    mac_builder = ".github/runtime/build_macos_runtime.py"
    package_inputs = {
        "advisor/companion.py", "advisor/AdvisorRuntime.cs", "advisor/AlphaDefectAdvisor.csproj",
        MANIFEST, "tools/package/Build-Advisor.ps1", "tools/package/Test-WorkshopPackage.ps1",
    }
    shared = full or any(p in package_inputs or
                         (p.startswith((".github/runtime/", "tests/runtime/")) and p != mac_builder)
                         for p in paths)
    platforms = [{"os": os_name, "rid": rid} for os_name, rid in PLATFORMS.items()
                 if shared or (rid == "osx-arm64" and mac_builder in paths)]
    return dict(publish=publish, scripts=scripts, advice=advice, runtime=bool(platforms),
                matrix={"include": platforms})


def main():
    event = os.environ["GITHUB_EVENT_NAME"]
    payload = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    current = version(Path(MANIFEST).read_text())
    previous = None
    paths = []
    if event == "pull_request":
        pr = payload["pull_request"]
        diff = subprocess.check_output(["git", "diff", "--no-renames", "--name-only", "-z",
                                        pr["base"]["sha"] + "..." + pr["head"]["sha"]])
        paths = os.fsdecode(diff).split("\0")
    elif event == "push":
        previous = version(subprocess.check_output(["git", "show", payload["before"] + ":" + MANIFEST]))
    result = select(event, os.environ["GITHUB_REF"], paths, current, previous)
    print(result)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        print("version=" + ".".join(map(str, current)), file=output)
        for key, value in result.items():
            print(f"{key}={json.dumps(value, separators=(',', ':'))}", file=output)


if __name__ == "__main__":
    main()
