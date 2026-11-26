using System.Diagnostics;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Localization;
using static AlphaDefectAdvisor.AdvisorText;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using Sts2Headless;

namespace AlphaDefectAdvisor;

[ModInitializer(nameof(Start))]
public static class Entry
{
    static Task<string>? installation;
    static string? home;
    static AdviceOverlay overlay = null!;
    static RunSession? session;
    static Process? worker;
    static Task<RankedAction[]>? pending;
    static RankedAction[]? ranking;
    static RunState? forecastRun;
    static Task<RunClearHistory>? history;
    static readonly AfterAdviceForecast forecast = new();
    static string? current;
    static long revision, requested;
    static ulong next, retry;
    static string errorKey = "adviceRetry";
    public static void Start() => Callable.From(Attach).CallDeferred();

    static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        overlay = new AdviceOverlay(tree);
        overlay.Hide();
        tree.ProcessFrame += Tick;
        tree.Root.TreeExiting += StopWorker;
    }

    static string PackageDirectory()
    {
        var assembly = typeof(Entry).Assembly;
        string? folder = Path.GetDirectoryName(assembly.Location);
        if (folder != null && File.Exists(Path.Combine(folder, "runtime.zip"))) return folder;

        // Some loaders read assemblies into memory. Use the active mod's registered path.
        var mod = ModManager.Mods.FirstOrDefault(m => m.assemblies?.Contains(assembly) == true)
            ?? ModManager.Mods.Single(m => m.manifest?.id == "AlphaDefectAdvisor");
        string path = ProjectSettings.GlobalizePath(mod.path);
        folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder == null || !File.Exists(Path.Combine(folder, "runtime.zip")))
            throw new FileNotFoundException("ワークショップの配布フォルダーに runtime.zip が見つかりません");
        return folder;
    }

    static void StopWorker()
    {
        if (worker != null) {
            try { if (!worker.HasExited) worker.Kill(); }
            finally { worker.Dispose(); worker = null; }
        }
        pending = null;
        forecastRun = null;
        history = null;
        ranking = null;
        current = null;
        revision++;
    }

    static void Tick()
    {
        var now = Time.GetTicksMsec();
        if (now < next) return;
        next = now + 250;
        try {
            SetLanguage(LocManager.Instance?.Language);
            overlay.RefreshHeader();
            if (now < retry) {
                overlay.Show(T(errorKey));
                return;
            }
            if (home == null) {
                if (installation == null) {
                    installation = AdvisorRuntime.Prepare(PackageDirectory());
                }
                if (!installation.IsCompleted) {
                    overlay.Show(T("setup"));
                    return;
                }
                home = installation.GetAwaiter().GetResult();
            }
            if (NRun.Instance == null) {
                session = null;
                if (worker != null) StopWorker();
                overlay.Hide();
                return;
            }
            var state = RunSession.State;
            if (state == null || state.Players.Count != 1 || state.Players[0].Character.Id.Entry != "IRONCLAD") {
                Wait(T("ironclad")); return;
            }
            if (!ReferenceEquals(forecastRun, state)) {
                current = null;
                history = null;
                forecastRun = state;
            }
            if (session == null) {
                var catalog = Path.Combine(home, "upstream", "engine", "catalog");
                Vocab.Load(Path.Combine(catalog, "vocab.txt"));
                if (Vocab.Count < 2557) throw new InvalidDataException("語彙表を読み込めません");
                AppContext.SetData("Sts2NoDownsideRelics", Path.Combine(catalog, "no_downside_relics.txt"));
                Observation.FormatOverride = 28;
                Observation.RouteSummary = true;
                session = RunSession.Live();
            }
            var d = session.Inspect();
            if (d == null || d.Choices.Count == 0) { Wait(session.WaitReason); return; }
            var labels = d.Choices.Select(RunSession.Describe).ToArray();
            var frame = Convert.ToBase64String(Observation.Build(session, d, false).Serialize());
            if (frame != current) {
                current = frame;
                revision++;
                ranking = null;
            }
            if (worker?.HasExited == true) throw new IOException("Inference worker exited");
            if (pending?.IsCompleted == true) {
                var result = pending.GetAwaiter().GetResult();
                pending = null;
                if (requested == revision) ranking = result;
            }
            if (ranking != null) {
                var advice = AdvisorText.FormatRanking(state.TotalFloor, labels, ranking);
                if (forecast.Poll(revision, () => EstimateClearChance(state)) is { } value)
                    advice += "\n" + (value.Chance is { } chance ?
                        T(value.Reference ? "clearReference" : "clearChance", chance) : T("clearUnknown"));
                overlay.Show(advice);
                return;
            }
            overlay.Show(T("thinking"));
            if (pending == null) {
                worker ??= AdvisorRuntime.StartWorker(home);
                requested = revision;
                var request = JsonSerializer.Serialize(new { frame, labels = Enumerable.Range(0, labels.Length) });
                pending = Predict(worker, request, labels.Length).WaitAsync(TimeSpan.FromMinutes(2));
            }
        } catch (Exception ex) {
            StopWorker();
            installation = null;
            errorKey = ex switch {
                HttpRequestException => "networkError",
                FileNotFoundException when home == null => "missingFiles",
                InvalidDataException when home == null => "damagedFiles",
                PlatformNotSupportedException => "platformError",
                TimeoutException => "timeout",
                _ => home == null ? "setupRetry" : "adviceRetry"
            };
            GD.PrintErr($"AlphaDefect Advisor: {ex.GetBaseException()}");
            overlay.Show(T(errorKey));
            retry = now + 5000;
        }
    }

    static Task<ClearEstimate> EstimateClearChance(RunState state)
    {
        if (state.GameMode != GameMode.Standard || state.IsCustomGame || state.Modifiers.Count != 0)
            return Task.FromResult(new ClearEstimate(null));

        // Only read game objects on the game thread. History IO and matching run independently of the action worker.
        var player = state.Players[0];
        var snapshot = new ClearState(state.AscensionLevel, state.TotalFloor, player.Creature.CurrentHp,
            player.Creature.MaxHp, player.Gold, (state.CurrentRoom as CombatRoom)?.Encounter?.Id.ToString());
        if (history == null) {
            int profile = SaveManager.Instance.CurrentProfileId;
            var paths = new[] { false, true }.Select(modded => ProjectSettings.GlobalizePath(
                UserDataPathProvider.GetAccountScopedBasePath(RunHistorySaveManager.GetHistoryPath(profile, modded)))).ToArray();
            history = Task.Run(() => RunClearHistory.Load(paths));
        }
        var loaded = history;
        return Task.Run(async () => (await loaded.ConfigureAwait(false)).Estimate(snapshot));
    }

    static void Wait(string message)
    {
        if (current != null) { current = null; ranking = null; revision++; }
        overlay.Show(message);
    }

    static async Task<RankedAction[]> Predict(Process process, string request, int count)
    {
        await process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
        using var json = JsonDocument.Parse(line ?? throw new EndOfStreamException("推論が終了しました"));
        var rows = json.RootElement.EnumerateArray()
            .Select(r => new RankedAction(r[0].GetInt32(), r[1].GetDouble())).ToArray();
        if (rows.Length != Math.Min(3, count) || rows.Any(r => r.Index < 0 || r.Index >= count ||
                !double.IsFinite(r.Probability) || r.Probability < 0 || r.Probability > 1))
            throw new InvalidDataException("Invalid inference ranking");
        return rows;
    }

}
