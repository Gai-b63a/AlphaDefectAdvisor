using System.Diagnostics;
using System.Net;
using System.Reflection;

// Use the actual packaged DLL. Only the upstream data HTTP responses are simulated.
var package = Path.GetFullPath(args[0]);
var fixture = Path.GetFullPath(args[1]);
var cache = Path.GetFullPath(args[2]);
#if VM_PROBE
var type = typeof(AlphaDefectAdvisor.AdvisorRuntime);
#else
var type = Assembly.LoadFrom(Path.Combine(package, "AlphaDefectAdvisor.dll")).GetType("AlphaDefectAdvisor.AdvisorRuntime", true)!;
#endif
var install = type.GetMethod("InstallAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
var start = type.GetMethod("StartWorker", BindingFlags.Static | BindingFlags.NonPublic)!;
Task<string> Install(HttpClient client) => (Task<string>)install.Invoke(null, [package, cache, client])!;
using var responses = new Fixture(fixture);
using var http = new HttpClient(responses);
try {
    await Install(http);
    throw new Exception("A damaged first download was accepted");
} catch (InvalidDataException) { }
if (Directory.EnumerateFileSystemEntries(cache).Any()) throw new Exception("Partial installation visible");
responses.Corrupt = false;
string home = await Install(http);
if (responses.Count != 6) throw new Exception("Unexpected number of initial requests");
responses.Offline = true;
if (await Install(http) != home || responses.Count != 6) throw new Exception("Offline restart failed");
using var worker = (Process)start.Invoke(null, [home])!;
try {
    while (await Console.In.ReadLineAsync() is string request) {
        await worker.StandardInput.WriteLineAsync(request);
        await worker.StandardInput.FlushAsync();
        Console.WriteLine(await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(2)) ??
            throw new EndOfStreamException("Packaged worker stopped"));
    }
    worker.StandardInput.Close();
    await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
    if (worker.ExitCode != 0) throw new Exception($"Worker exit code {worker.ExitCode}");
} finally {
    if (!worker.HasExited) worker.Kill();
}

sealed class Fixture(string root) : HttpMessageHandler
{
    public bool Corrupt = true;
    public bool Offline;
    public int Count;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Count++;
        if (Offline) throw new HttpRequestException("Offline");
        const string prefix = "https://raw.githubusercontent.com/hmty-CHEN/AlphaDefect-Public/800db3039162647bf0ad13ed379f155cc5806f5f/";
        var url = request.RequestUri!.AbsoluteUri;
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) throw new Exception(url);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, url[prefix.Length..]), ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Corrupt ? [0] : bytes) };
    }
}


#if VM_PROBE
namespace AlphaDefectAdvisor
{
    internal static class PayloadInfo
    {
        internal static readonly string Hash = Environment.GetEnvironmentVariable("PROBE_PAYLOAD_HASH")
            ?? throw new InvalidOperationException("Missing test payload checksum");
    }
}
#endif
