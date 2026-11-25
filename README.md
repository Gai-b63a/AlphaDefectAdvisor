# AlphaDefect Advisor

AlphaDefect Advisor gives **Japanese-language advice for solo Ironclad runs** in Slay the Spire 2. The in-game panel recommends a move and shows the model's selection probabilities for up to three actions. You choose the cards and targets and decide when to end your turn.

The mod targets Steam's **public-beta branch, game version v0.114.0**. The panel starts at the center-right of the screen. Drag its header with the left mouse button to move it. The mod remembers the position for your next session.

## Install from Steam

1. [Subscribe to AlphaDefect Advisor on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3845626411).
2. Wait for Steam to finish downloading the mod, then launch the game from Steam. Keep an internet connection for the first launch. The panel shows setup progress.
3. Once setup is complete, start a solo Ironclad run.

The mod installs its own Python runtime, CPU inference libraries and official weights-v6 model. It downloads five data files, including the vocabulary, from a pinned GitHub revision and verifies their SHA-256 hashes. On Windows, the runtime lives in `%LOCALAPPDATA%/AlphaDefectAdvisor/runtime`. Subsequent launches of the same release reuse the verified files. Subscription and first launch handle setup, including the model download through Steam.

## What the advisor covers

- **Combat and cards.** Get advice on plays, targets, card rewards, Discovery, hand selections and upgrades, cards in combat piles, and card bundles. When selecting multiple cards, the advisor reevaluates the remaining choices after each pick.
- **Map and rooms.** Get recommendations on routes, events, rest sites, shops and rewards, including which card to smith, remove, transform or enchant. Confirm a card preview in the game or go back to change your selection.
- **Potion rewards.** When your belt is full, the advisor can identify a potion to discard by name and slot, suggest drinking a usable potion, or recommend leaving the remaining rewards. It reevaluates the reward when a slot opens.

The panel waits during enemy turns and on unsupported selection screens. The [acceptance criteria and VM test checklist](REQUIREMENTS.md) are available in Japanese.

## Build on Windows

Install the .NET 9 SDK, the supported game version, standard Windows x64 CPython 3.12 with pip, and the Visual Studio x64 redistributable CRT. Place the official weights-v6 file `ironclad_ema_v24782.pt` in the repository root. From the **Steam Workshop** GitHub Actions run for your commit, extract `upstream-source-<commit SHA>` into `upstream`. The build downloads CPU wheels from a hash-pinned list.

```powershell
$game = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\tools\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64"
.\tools\Install-Advisor.ps1 -GameDir $game
```

The build creates the Workshop upload workspace in `dist/workshop`. Use `-BuildPython` to select the build interpreter, `-Wheelhouse` for previously downloaded wheels, or `-CrtDir` for the CRT directory.

## Playtest the repository build on Windows

With the Windows build prerequisites above, double-click `tools/Play-Local.cmd`, or run the following in PowerShell 7 from the repository root:

```powershell
.\tools\Start-LocalGame.ps1
```

The launcher builds the advisor from your checkout, copies the installed v0.114.0 game to `runtime/local-playtest/game`, installs the build in that copy's `mods/AlphaDefectAdvisor`, and launches it with Steam integration disabled. It uses a separate offline game profile under `runtime/local-playtest/profile`. Launch the game normally from the Steam library to play with your subscriptions and Steam profile.

Close the test game and rerun the command after editing the C# adapter. The launcher reuses the packaged inference runtime in `runtime/package/runtime.zip` when present. Pass `-RebuildRuntime` after editing the Python worker or changing its dependencies. Use `-GameDir` for a different Steam install location, or `-SmokeTest` to check local DLL initialization and exit automatically. Each launch writes a timestamped game log in `runtime/local-playtest`.

## Test a release candidate on Windows and macOS

The development build includes an Apple Silicon inference runtime. In the **Steam Workshop** workflow, check both `vm-runtime-test` jobs for the candidate commit. They test recovery from a failed initial setup, offline restart, and the model's ranked actions and probabilities for the seeded opening, card selections and potion rewards. To verify the Steam installation and overlay in the running game:

1. On the Windows build PC, check out the candidate commit and install the supported game version. Extract `upstream-source-<commit SHA>` from that workflow run into `upstream` so that `upstream/engine/Sts2Headless` exists. Extract `workshop-runtime-<commit SHA>` from the same run into a separate folder.
2. Pass the folder containing `runtime.json` and `runtime.zip` to the build. It checks the commit and archive hash before building the DLL. Use the same `dist/AlphaDefectAdvisor` folder for both guests.

   ```powershell
   .\tools\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64" -RuntimeArtifactDir C:\AdvisorTest\artifact
   .\tools\Test-WorkshopPackage.ps1 -Workspace .\dist\workshop
   ```

3. Install Steam and game v0.114.0 in a Windows 11 x64 guest and an Apple Silicon macOS guest. Disconnect shared folders, verify rendering and input in the base game, and save a snapshot before installing or subscribing to the mod. To test an unpublished candidate, clone the snapshot and copy the distribution folder into the game's `mods` directory.
4. For the subscription test, restore the clean snapshot after the candidate is published. Subscribe in the guest's Steam client and launch the game. Record first-launch advice, the seven gameplay scenarios, and network failure and recovery. Keep manual-install results separate from subscription results. [Acceptance criteria and VM test checklist](REQUIREMENTS.md)

## Publish to the Steam Workshop

**Update the existing item.** Bump the mod version and merge it into `main`. GitHub Actions creates a release. When you are ready to publish, run `Update-Workshop.cmd` under the Windows user account used for the first upload. It verifies the latest release, builds the mod, and submits it to Workshop item `3845626411`. The publishing PC needs the .NET 9 SDK, Python 3.11 or later, PowerShell 7 and the supported game version.

The updater writes its log to `%LOCALAPPDATA%/AlphaDefectAdvisor/publisher/update.log`. If the upload result is unclear, run the updater again. It checks SteamCMD's completion record for the previous build and item. A matching record marks the version as published without sending it again. If verification is inconclusive, the updater pauses so you can check the item's Workshop change history. If you previously installed the scheduled task named `AlphaDefect Advisor Workshop Update`, disable it in Windows Task Scheduler.

**Create an item for the first time.** Sign in to Steam and drag the verified `dist/workshop` folder from Windows Explorer onto the official `ModUploader.exe`. After a successful upload, commit the generated `mod_id.txt` to `main` as `workshop/mod_id.txt`. The file contains just the numeric Workshop item ID. [Official ModUploader releases](https://github.com/megacrit/sts2-mod-uploader/releases)

## License

See [LICENSE](LICENSE) for the original code. The distribution includes `AlphaDefect-LICENSE.txt` and `AlphaDefect-NOTICE.md` for the AlphaDefect-derived components. The distribution excludes the game, extracted game images, and game-derived vocabulary and catalog files. Slay the Spire 2 is a game by Mega Crit. AlphaDefect Advisor is a fan-made mod.
