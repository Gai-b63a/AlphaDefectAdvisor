"""SteamCMD publishing on a disposable GitHub-hosted Windows runner."""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request

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


def state_api(method="GET", payload=None):
    url = f"https://api.github.com/repos/{os.environ['GITHUB_REPOSITORY']}/contents/workshop/mod_id.txt"
    if method == "GET":
        url += "?ref=main"
    req = urllib.request.Request(url, method=method, data=json.dumps(payload).encode() if payload else None,
                                 headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"],
                                          "Content-Type": "application/json", "User-Agent": "AlphaDefectAdvisor"})
    try:
        with urllib.request.urlopen(req, timeout=30) as res:
            return json.load(res)
    except urllib.error.HTTPError as err:
        if method == "GET" and err.code == 404:
            return None
        raise


def save_state(value, previous=None):
    body = {"branch": "main", "message": "Record Steam Workshop publication state",
            "content": base64.b64encode((value + "\n").encode()).decode()}
    if previous:
        body["sha"] = previous
    return state_api("PUT", body)["content"]["sha"]


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


def publish(exe, workspace, mode):
    state = state_api()
    item = base64.b64decode(state["content"]).decode().strip() if state else ""
    if mode == "create" and state:
        raise ValueError("Workshop ID exists or creation is pending; check workshop/mod_id.txt")
    if mode == "update" and not valid_id(item):
        raise ValueError("Update requires a numeric ID in workshop/mod_id.txt on main")
    vdf = exe.parent / "advisor.vdf"
    write_vdf(vdf, workspace, item or "0")
    reservation = save_state("pending:" + os.environ["GITHUB_RUN_ID"]) if mode == "create" else None
    log = steam(exe, ["+workshop_build_item", str(vdf)])
    result = re.search(r'"publishedfileid"\s+"([0-9]+)"', vdf.read_text(encoding="utf-8-sig"), re.I)
    if not result or not valid_id(result[1]) or (item and item != result[1]) or not re.search(r"(?im)^\s*Success\.", log):
        raise RuntimeError("Steam upload could not be confirmed. Inspect your Workshop items before retrying")
    url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result[1]
    print(url, flush=True)
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(f"[Steam Workshop item {result[1]}]({url})\n")
    if reservation:
        save_state(result[1], reservation)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["download", "create", "update"])
    parser.add_argument("exe", type=Path)
    args = parser.parse_args()
    exe = args.exe.resolve(strict=True)
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
        publish(exe, Path("dist/workshop").resolve(), args.mode)
