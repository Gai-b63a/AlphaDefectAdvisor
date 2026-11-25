using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AlphaDefectAdvisor;

internal static class AdvisorRuntime
{
    const string Source = "https://raw.githubusercontent.com/hmty-CHEN/AlphaDefect-Public/800db3039162647bf0ad13ed379f155cc5806f5f/";
    static readonly (string Path, string Hash)[] Tables = [
        ("engine/catalog/vocab.txt", "B9BA21326C3D90DC85284EB51BC6CD1B95739C56CABD458D502019DC7C6AB0A1"),
        ("engine/catalog/aliases.txt", "0A9FF41A14E58B998901CAE8D4157C4E8763892DBB1CEC1E9DAE2FAE050FF85A"),
        ("engine/catalog/no_downside_relics.txt", "66A465343A09B5E48675816631A83E31320FA0193D992FFA9F198AEAD4339935"),
        ("engine/catalog/v0.113.1/catalog.json", "E6F949F74D79CF1507E96DCF3B1C10BA7952A2707D5CF09FADBC80330915CC91"),
        ("python/sts2rl/enchant_effects.json", "5A3AB92D7401537ECEE241947BC11B1A4618DA629823474B9C9A45A0A4009A86")
    ];
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };

    internal static Task<string> Prepare(string packageDir)
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlphaDefectAdvisor", "runtime");
        return Task.Run(() => InstallAsync(packageDir, cache, Client));
    }

    // Each revision gets its own directory. Only a complete installation becomes visible.
    internal static async Task<string> InstallAsync(string packageDir, string cache, HttpClient client)
    {
        Directory.CreateDirectory(cache);
        string home = Path.Combine(cache, PayloadInfo.Hash[..24]);
        if (Directory.Exists(home)) {
            await EnsureTables(home, client);
            return home;
        }
        string staging = Directory.CreateDirectory(Path.Combine(cache, Guid.NewGuid().ToString("N"))).FullName;
        try {
            await EnsureTables(staging, client);
            using (var file = File.OpenRead(Path.Combine(packageDir, "runtime.zip"))) {
                if (Convert.ToHexString(await SHA256.HashDataAsync(file)) != PayloadInfo.Hash)
                    throw new InvalidDataException("配布ファイルの照合に失敗しました");
                file.Position = 0;
                ZipFile.ExtractToDirectory(file, staging);
            }
            try { Directory.Move(staging, home); }
            catch (IOException) when (Directory.Exists(home)) { /* Another game instance installed it. */ }
            return home;
        } finally {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    static async Task EnsureTables(string home, HttpClient client)
    {
        foreach (var (relative, expected) in Tables) {
            string path = Path.Combine(home, "upstream", relative);
            if (File.Exists(path)) {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (Convert.ToHexString(await SHA256.HashDataAsync(stream)) == expected) continue;
            }
            byte[] bytes = await client.GetByteArrayAsync(Source + relative);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != expected)
                throw new InvalidDataException($"初回データの照合に失敗しました ({Path.GetFileName(path)})");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path, true);
            } finally { File.Delete(temporary); }
        }
    }

    internal static Process StartWorker(string home)
    {
        var info = new ProcessStartInfo(Path.Combine(home, "python", "python.exe")) {
            WorkingDirectory = home, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false)
        };
        info.Environment["PATH"] = Environment.SystemDirectory;
        foreach (var arg in new[] { "-B", "-I", "-X", "utf8", Path.Combine(home, "advisor", "companion.py") })
            info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("推論プロセスを開始できません");
    }
}


