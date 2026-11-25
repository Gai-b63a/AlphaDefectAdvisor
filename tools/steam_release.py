"""Update an existing Workshop item through the local SteamCMD session."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

APP_ID = "2868840"


def steam(exe, args):
    login = ["+login", os.environ["STEAM_USERNAME"]]
    print("Connecting to Steam. Approve the sign-in in Steam Mobile if prompted.", flush=True)
    # Keep Steam output private: login arguments and account information must stay off the CI log.
    try:
        proc = subprocess.run([str(exe), "+@ShutdownOnFailedCommand", "1", *login, *args, "+quit"],
                          cwd=exe.parent, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
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


def receipt(logfile, item, preview, offset=0):
    """Match one completed Steam upload to the preview in its unique build directory."""
    prefix = f"[AppID {APP_ID}] "
    started = False
    matched = False
    finished = False
    try:
        with logfile.open("rb") as stream:
            stream.seek(offset)
            for raw in stream:
                _, sep, message = raw.decode("utf-8", "replace").strip().partition(prefix)
                if not sep:
                    continue
                if message.startswith("Upload starting for workshop item "):
                    started = message == f"Upload starting for workshop item {item} by AppID {APP_ID}"
                    matched = finished = False
                elif started and message.startswith("Uploaded new workshop preview file "):
                    matched = Path(message.removeprefix("Uploaded new workshop preview file ")) == preview
                elif started and message.startswith("Upload finished for workshop item "):
                    finished = matched and message == f"Upload finished for workshop item {item} : OK"
                    started = False
    except OSError:
        return False
    return finished


def recover(exe, version):
    store = exe.parent.parent
    item = "3845626411"
    vdf = (exe.parent / "advisor.vdf").read_text(encoding="utf-8-sig")
    preview_field = re.search(r'^\s*"previewfile"\s+"([^"]+)"\s*$', vdf, re.M | re.I)
    if not preview_field or not re.search(r'^\s*"publishedfileid"\s+"' + item + r'"\s*$', vdf, re.M | re.I):
        return False
    preview = Path(preview_field[1])
    try:
        relative = preview.relative_to(store).as_posix()
    except ValueError:
        return False
    if not re.fullmatch(r"build-[0-9a-f-]{36}/source/dist/workshop/image[.]png", relative):
        return False
    hold = (store / "update-needs-attention.txt").read_text(encoding="utf-8-sig").strip()
    if hold.startswith("{"):
        record = json.loads(hold)
        if record != {"version": version, "item": item, "preview": str(preview)}:
            return False
    else:
        # v0.2.1 used a text marker. Bind it to the VDF version and submission time.
        if hold != f"Check Steam item {item} and SteamCMD login before retrying version {version}.":
            return False
        if not re.search(r'^\s*"changenote"\s+"v' + re.escape(version) + r'(?![.0-9])', vdf, re.M):
            return False
        times = [(store / "update-needs-attention.txt").stat().st_mtime, (exe.parent / "advisor.vdf").stat().st_mtime]
        if not -2 <= times[1] - times[0] <= 120:
            return False
    return receipt(exe.parent / "logs/workshop_log.txt", item, preview)


def publish(exe, workspace):
    item = Path("workshop/mod_id.txt").read_text(encoding="utf-8-sig").strip()
    if not valid_id(item):
        raise ValueError("Register the existing Steam Workshop item ID in workshop/mod_id.txt")
    vdf = exe.parent / "advisor.vdf"
    write_vdf(vdf, workspace, item)
    logfile = exe.parent / "logs/workshop_log.txt"
    offset = logfile.stat().st_size if logfile.exists() else 0
    log = steam(exe, ["+workshop_build_item", str(vdf)])
    result = re.search(r'"publishedfileid"\s+"([0-9]+)"', vdf.read_text(encoding="utf-8-sig"), re.I)
    if not result or not valid_id(result[1]) or item != result[1] or not (
        receipt(logfile, item, workspace / "image.png", offset) or re.search(r"(?im)^\s*Success[.!]", log)
    ):
        raise RuntimeError("Steam upload could not be confirmed. Inspect your Workshop items before retrying")
    url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result[1]
    print(url, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["confirm", "update", "prepare-create-vdf"])
    parser.add_argument("path", type=Path)
    parser.add_argument("--workspace", type=Path, default=Path("dist/workshop"))
    parser.add_argument("--version")
    args = parser.parse_args()
    if args.mode == "confirm":
        raise SystemExit(0 if args.version and recover(args.path.resolve(), args.version) else 1)
    if args.mode == "prepare-create-vdf":
        write_vdf(args.path.resolve(), args.workspace.resolve(strict=True), "0")
        raise SystemExit(0)
    exe = args.path.resolve(strict=True)
    if not os.environ.get("STEAM_USERNAME"):
        parser.error("The local updater needs the account name used for the first upload")
    publish(exe, args.workspace.resolve())
