# AlphaDefect Advisor

AlphaDefect Advisor gives **advice for solo Ironclad runs** in Slay the Spire 2. The in-game panel recommends a move and shows the model's selection probabilities for up to three actions. You choose the cards and targets and decide when to end your turn.

The v0.2.8 development build follows the in-game language. It includes advisor text for all 17 language entries in game v0.114.0: Arabic, Brazilian Portuguese, English, French, German, Indonesian, Italian, Japanese, Korean, Polish, Russian, Simplified Chinese, Spanish (Latin America), Spanish (Spain), Thai, Traditional Chinese, and Turkish. Arabic is hidden in the game's normal language selector. Card, enemy and item names come from the game. Unrecognized languages use English advisor text. In-game visual verification is pending. Version 0.2.7 displays advice in Japanese.

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

## Current run win estimate

Once the ranked choices are visible, a separate task estimates the chance of clearing the **entire run**. A new choice appears immediately even if the previous estimate is still being calculated. The estimate is displayed below the action probabilities and does not change their ranking.

For a standard solo Ironclad run, the advisor reads completed, non-abandoned games from the same local profile and Ascension. It compares runs that reached this floor using HP, maximum HP, gold and the current encounter when known. It uses the previous completed room's stats for historical comparisons. It does not simulate the remaining fights or estimate the effect of each card. Personal records stay on the device.

With limited history on A10, the panel labels a separate **AI floor baseline** from 1,000 upstream Ironclad runs on v0.113.1. It depends only on the floor reached. At other Ascensions with insufficient history, or in unsupported modes, the panel reports that the estimate is unavailable. Calibration for individual play on v0.114.0 is unverified.

## Playtest the repository build on Windows

Install PowerShell 7 on Windows x64 and the supported Steam game version. From the repository root run:

```powershell
pwsh -File ./tools/Play-Local.ps1
```

The first launch downloads a pinned .NET SDK and verified build inputs from the v0.2.7 release into `runtime/toolchain`. A ZIP checkout works. The build uses the adapter and Python worker from your checkout. Python, Git, Visual Studio and a manually downloaded model are unnecessary on the playtest PC.

The launcher builds the advisor from your checkout, copies the installed v0.114.0 game to `runtime/local-playtest/game`, installs the build in that copy's `mods/AlphaDefectAdvisor`, and launches it with Steam integration disabled. It uses a separate offline game profile under `runtime/local-playtest/profile`. Launch the game normally from the Steam library to play with your subscriptions and Steam profile.

Each launch prepares the active offline modded profile for full-content testing. Start a new standard run to use the revealed timeline (including Neow), unlocked characters and ascensions, and completed tutorials. Existing runs keep their original map. A fresh test profile gets one recorded Ironclad loss to skip the scripted first run. This does not create a run history file or grant Steam achievements.

Close the test game and rerun the command after editing the C# adapter. Worker changes automatically regenerate the Windows playtest runtime. Use `-GameDir` for a different Steam install location, `-PrepareOnly` to build and install without starting the game, or `-SmokeTest` to check local DLL initialization and exit automatically. Each launch writes a timestamped game log in `runtime/local-playtest`.

## Test a release candidate on Windows and macOS

The development build includes an Apple Silicon inference runtime. In the **Build and test release** workflow, check both `runtime-test` jobs for the candidate commit. They test recovery from a failed initial setup, offline restart, and the model's ranked actions and probabilities for the seeded opening, card selections and potion rewards. To verify the Steam installation and overlay in the running game:

1. Use the candidate revision’s upstream-source artifact and the two tested platform archives from a full workflow run. The versioned GitHub Release contains the assembled runtime and build source.
2. Build that source against the installed game with the bundled SDK and verify the resulting Workshop package. For a published GitHub release, run `pwsh -File ./tools/Update-Workshop.ps1 -VerifyOnly` to perform these steps without contacting Steam.

3. Install Steam and game v0.114.0 in a Windows 11 x64 guest and an Apple Silicon macOS guest. Disconnect shared folders, verify rendering and input in the base game, and save a snapshot before installing or subscribing to the mod. To test an unpublished candidate, clone the snapshot and copy the distribution folder into the game's `mods` directory.
4. For the subscription test, restore the clean snapshot after the candidate is published. Subscribe in the guest's Steam client and launch the game. Record first-launch advice, the seven gameplay scenarios, and network failure and recovery. Keep manual-install results separate from subscription results. [Acceptance criteria and VM test checklist](REQUIREMENTS.md)

## Publish to the Steam Workshop

**Update the existing item.** Bump the mod version and merge it into `main`. PRs run checks for the affected components. A version increase on main triggers both platform builds and offline tests before GitHub Actions creates the release. Use Run workflow to repeat full verification, or to retry an unpublished version on main. When you are ready to publish, run `pwsh -File ./tools/Update-Workshop.ps1` under the Windows user account used for the first upload. It verifies the latest release, builds the mod, and submits it to Workshop item `3845626411`. The publishing PC needs PowerShell 7, the supported game version and Steam access to the existing item.

The updater writes its log to `%LOCALAPPDATA%/AlphaDefectAdvisor/publisher/update.log`. If the upload result is unclear, run the updater again. It checks SteamCMD's completion record for the previous build and item. A matching record marks the version as published without sending it again. If verification is inconclusive, the updater pauses so you can check the item's Workshop change history. If you previously installed the scheduled task named `AlphaDefect Advisor Workshop Update`, disable it in Windows Task Scheduler.

**Create an item for the first time.** Sign in to Steam and drag the verified `dist/workshop` folder from Windows Explorer onto the official `ModUploader.exe`. After a successful upload, commit the generated `mod_id.txt` to `main` as `workshop/mod_id.txt`. The file contains just the numeric Workshop item ID. [Official ModUploader releases](https://github.com/megacrit/sts2-mod-uploader/releases)

## License

See [LICENSE](LICENSE) for the original code. The distribution includes `AlphaDefect-LICENSE.txt` and `AlphaDefect-NOTICE.md` for the AlphaDefect-derived components. The distribution excludes the game, extracted game images, and game-derived vocabulary and catalog files. Slay the Spire 2 is a game by Mega Crit. AlphaDefect Advisor is a fan-made mod.
