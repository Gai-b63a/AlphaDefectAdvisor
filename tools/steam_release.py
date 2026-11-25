"""SteamCMD publishing on a disposable GitHub-hosted Windows runner."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

APP_ID = "2868840"
DEPOT = "2868841"
MANIFEST = "8445808044584180628"


def steam(exe, args, authenticate=False):
    login = ["+login", os.environ["STEAM_USERNAME"]]
    if authenticate:
        password = os.environ.get("STEAM_PASSWORD")
        if not password:
            raise ValueError("Configure STEAM_PASSWORD in the steam-workshop environment")
        login.append(password)
    env = {k: v for k, v in os.environ.items() if k not in {"GH_TOKEN", "STEAM_PASSWORD"}}
    print("Connecting to Steam. Approve the sign-in in Steam Mobile if prompted.", flush=True)
    # Keep Steam output private: login arguments and account information must stay off the CI log.
    try:
        proc = subprocess.run([str(exe), "+@ShutdownOnFailedCommand", "1", *login, *args, "+quit"],
                          cwd=exe.parent, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              timeout=1200, check=False)
    except (subprocess.TimeoutExpired, OSError):
        raise RuntimeError("SteamCMD timed out or could not start; check Steam Mobile approval") from None
    log = proc.stdout.decode("utf-8", "replace")
    if proc.returncode or re.search(r"(?im)^\s*(?:ERROR!|FAILED\s*\()", log):
        raise RuntimeError(f"SteamCMD failed (exit {proc.returncode}); check Steam Guard, account access and Workshop terms")
    return log


def valid_id(value):
    return bool(re.fullmatch(r"[1-9][0-9]{0,19}", value)) and int(value) < 2**64


def write_vdf(path, workspace, item):
    config = json.loads((workspace / "workshop.json").read_text(encoding="utf-8-sig"))
    fields = {"appid": APP_ID, "publishedfileid": item, "contentfolder": (workspace / "content").as_posix(),
              "previewfile": (workspace / "image.png").as_posix(), "visibility": "0",
              "title": config["title"], "description": config["description"], "changenote": config["changeNote"]}
    def quote(value):
        return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'
    path.write_text('"workshopitem"\n{\n' + "".join(f"  {quote(k)} {quote(v)}\n" for k, v in fields.items()) + '}\n',
                    encoding="utf-8")


def publish(exe, workspace):
    item = Path("workshop/mod_id.txt").read_text(encoding="utf-8-sig").strip()
    if not valid_id(item):
        raise ValueError("Register the existing Steam Workshop item ID in workshop/mod_id.txt")
    vdf = exe.parent / "advisor.vdf"
    write_vdf(vdf, workspace, item)
    log = steam(exe, ["+workshop_build_item", str(vdf)])
    result = re.search(r'"publishedfileid"\s+"([0-9]+)"', vdf.read_text(encoding="utf-8-sig"), re.I)
    if not result or not valid_id(result[1]) or item != result[1] or not re.search(r"(?im)^\s*Success\.", log):
        raise RuntimeError("Steam upload could not be confirmed. Inspect your Workshop items before retrying")
    url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result[1]
    print(url, flush=True)
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(f"[Steam Workshop item {result[1]}]({url})\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["download", "update", "prepare-create-vdf"])
    parser.add_argument("path", type=Path)
    parser.add_argument("--workspace", type=Path, default=Path("dist/workshop"))
    args = parser.parse_args()
    if args.mode == "prepare-create-vdf":
        write_vdf(args.path.resolve(), args.workspace.resolve(strict=True), "0")
        raise SystemExit(0)
    exe = args.path.resolve(strict=True)
    if not os.environ.get("STEAM_USERNAME"):
        parser.error("Configure STEAM_USERNAME in the steam-workshop environment")
    if args.mode == "download":
        steam(exe, ["+download_depot", APP_ID, DEPOT, MANIFEST], authenticate=True)
        game = exe.parent / "steamapps/content" / ("app_" + APP_ID) / ("depot_" + DEPOT) / "data_sts2_windows_x86_64"
        if not (game / "sts2.dll").is_file():
            raise FileNotFoundError("Steam did not supply the required v0.113.1 Windows depot")
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"game={game}\n")
    else:
        publish(exe, Path("dist/workshop").resolve())
