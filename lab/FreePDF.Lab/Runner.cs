using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreePDF.Lab.Fixtures;

namespace FreePDF.Lab;

public static partial class Runner
{
    public const string ReportKind = "freepdf-lab-v1";
    public const int Dpi = 144, Tolerance = 8;
    [GeneratedRegex(@"^\d{8}T\d{9}Z-[a-f0-9]{8}$")] public static partial Regex RunIdPattern();
    public static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }

    public static async Task<int> Run(LabConfig config)
    {
        if (config.TimeoutSeconds is < 1 or > 120 || config.RetainRuns < 1 || config.RetainBytes < 1_000_000) throw new ArgumentException("Invalid runner limits.");
        Directory.CreateDirectory(config.DataDirectory);
        using var runLock = new FileStream(Path.Combine(config.DataDirectory, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var started = DateTimeOffset.UtcNow;
        var id = started.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "-" + Guid.NewGuid().ToString("N")[..8];
        var root = Path.Combine(config.DataDirectory, id); Directory.CreateDirectory(root);
        var renderer = await Execute(new Command(config.Renderer, ["-v"]), [], config.TimeoutSeconds);
        if (renderer.ExitCode != 0) throw new InvalidOperationException("Renderer unavailable: " + renderer.Output);
        var cases = new List<CaseResult>();
        foreach (var spec in CaseCatalog.All)
        {
            var pages = new List<PageResult>();
            var caseRoot = Path.Combine(root, spec.Id); Directory.CreateDirectory(caseRoot);
            var referenceError = await Generate(config.Reference, "reference");
            var candidateError = await Generate(config.Candidate, "candidate");
            var errors = new List<string>();
            var a = Directory.GetFiles(Path.Combine(caseRoot, "reference"), "page-*.png").Order().ToArray();
            var b = Directory.GetFiles(Path.Combine(caseRoot, "candidate"), "page-*.png").Order().ToArray();
            if (candidateError == null)
            {
                ValidateStructure(Path.Combine(caseRoot, "candidate", "structure.json"), spec.Id, b.Length, errors);
                if (referenceError == null)
                {
                    if (a.Length != b.Length) errors.Add($"Page count differs: reference {a.Length}, candidate {b.Length}.");
                    var refStructure = JsonNode.Parse(File.ReadAllText(Path.Combine(caseRoot, "reference", "structure.json")))!;
                    var candidateStructure = JsonNode.Parse(File.ReadAllText(Path.Combine(caseRoot, "candidate", "structure.json")))!;
                    foreach (var key in new[] { "pages", "title", "attachments", "attachmentText", "fields", "metadata" })
                        if (!JsonNode.DeepEquals(refStructure[key], candidateStructure[key])) errors.Add("Structure differs: " + key);
                }
            }
            for (var p = 0; p < Math.Max(a.Length, b.Length); p++)
            {
                var name = $"page-{p + 1:000}"; PixelMetrics? metrics = null; string? difference = null;
                if (referenceError == null && candidateError == null && p < a.Length && p < b.Length)
                {
                    var result = Pixels.Compare(Pixels.ReadPpm(Path.ChangeExtension(a[p], ".ppm")), Pixels.ReadPpm(Path.ChangeExtension(b[p], ".ppm")), Tolerance);
                    metrics = result.Metrics;
                    if (metrics.SameDimensions) { difference = $"{spec.Id}/{name}-diff.png"; Pixels.WritePng(Path.Combine(root, difference), result.Difference); }
                }
                pages.Add(new PageResult(p + 1, p < a.Length ? $"{spec.Id}/reference/{name}.png" : null, p < b.Length ? $"{spec.Id}/candidate/{name}.png" : null, difference, metrics));
            }
            // Preserve PNGs, source PDFs and logs; intermediate PPMs are reproducible and much larger.
            foreach (var ppm in Directory.GetFiles(caseRoot, "*.ppm", SearchOption.AllDirectories)) File.Delete(ppm);
            var changed = errors.Count > 0 || pages.Any(p => p.Pixels is { SameDimensions: false } || p.Pixels?.ChangedPixels > 0);
            var status = candidateError != null ? "failed" : referenceError != null || changed ? string.IsNullOrEmpty(spec.KnownReferenceDefect) ? "failed" : "review" : "match";
            // Candidate invariant violations are always failures, even for a known reference defect.
            if (errors.Any(e => e.StartsWith("Candidate:"))) status = "failed";
            cases.Add(new CaseResult(spec.Id, spec.Title, status, spec.KnownReferenceDefect, referenceError, candidateError, errors.ToArray(), pages));
            Console.WriteLine($"{spec.Id}: {status}; {b.Length} candidate page(s)");

            async Task<string?> Generate(Command command, string engine)
            {
                var directory = Path.Combine(caseRoot, engine); Directory.CreateDirectory(directory);
                try
                {
                    var generation = await Execute(command, [spec.Id, directory], config.TimeoutSeconds);
                    File.WriteAllText(Path.Combine(directory, "generator.log"), generation.Output);
                    if (generation.ExitCode != 0) return $"Generator exited {generation.ExitCode}; see generator.log.";
                    var pdf = Path.Combine(directory, "document.pdf");
                    if (!File.Exists(pdf) || new FileInfo(pdf).Length > 20_000_000) throw new InvalidDataException("Missing PDF or PDF exceeds 20 MB.");
                    var structure = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "structure.json")))!;
                    var count = structure["pages"]!.GetValue<int>();
                    if (count is < 1 or > 20) throw new InvalidDataException("Page count outside 1–20 limit.");
                    var draw = await Execute(new Command(config.Renderer, []), ["draw", "-q", "-r", Dpi.ToString(), "-c", "rgb", "-o", Path.Combine(directory, "page-%03d.ppm"), pdf], config.TimeoutSeconds);
                    File.WriteAllText(Path.Combine(directory, "renderer.log"), draw.Output);
                    if (draw.ExitCode != 0) return $"Renderer exited {draw.ExitCode}; see renderer.log.";
                    var ppms = Directory.GetFiles(directory, "page-*.ppm");
                    if (ppms.Length != count) throw new InvalidDataException("Rendered page count does not match document structure.");
                    foreach (var ppm in ppms) Pixels.WritePng(Path.ChangeExtension(ppm, ".png"), Pixels.ReadPpm(ppm));
                    return null;
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(directory, "generator.log"), "\n" + ex); return ex.Message; }
            }
        }
        var font = Path.Combine(AppContext.BaseDirectory, "DejaVuSans.ttf");
        var report = new RunReport(ReportKind, id, started, config.SourceRevision, config.Profile, renderer.Output.Trim(), Hash(config.Renderer), File.Exists(font) ? Hash(font) : "unavailable", Dpi, Tolerance,
            config.Reference.FileName + " " + string.Join(" ", config.Reference.Arguments), config.Candidate.FileName + " " + string.Join(" ", config.Candidate.Arguments), cases);
        Json.Write(Path.Combine(root, "report.json"), report);
        Retain(config, id);
        Console.WriteLine("Report: " + Path.Combine(root, "report.json"));
        return cases.Any(c => c.Status == "failed") ? 1 : 0;
    }

    static void ValidateStructure(string file, string id, int pages, List<string> errors)
    {
        var value = JsonNode.Parse(File.ReadAllText(file))!;
        void Expect(bool valid, string text) { if (!valid) errors.Add("Candidate: " + text); }
        Expect(value["pages"]!.GetValue<int>() == pages && pages > 0, "page count");
        if (id is "paragraphs" or "tables") Expect(pages >= 2, "pagination fixture must span multiple pages");
        Expect(value["title"]!.GetValue<string>() == "FreePDF lab / " + id, "title round trip");
        Expect(value["attachments"]!.GetValue<int>() == (id == "attachments" ? 1 : 0), "attachment count");
        Expect(value["fields"]!.GetValue<int>() == (id == "forms" ? 1 : 0), "field count");
        if (id == "attachments") Expect(value["attachmentText"]?.GetValue<string>() == "<fixture>original synthetic data</fixture>", "attachment bytes");
        if (id is "attachments" or "pdfa") Expect(value["metadata"]!.GetValue<bool>(), "XMP metadata missing");
    }

    public static async Task<ProcessResult> Execute(Command command, string[] extra, int timeout)
    {
        var start = new ProcessStartInfo(command.FileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in command.Arguments.Concat(extra)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start process.");
        var stdout = ReadBounded(process.StandardOutput); var stderr = ReadBounded(process.StandardError);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); throw new TimeoutException($"Process exceeded {timeout} seconds."); }
        return new ProcessResult(process.ExitCode, await stdout + await stderr);
    }
    static async Task<string> ReadBounded(StreamReader reader)
    {
        var output = new System.Text.StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer)) > 0) if (output.Length < 64_000) output.Append(buffer, 0, Math.Min(read, 64_000 - output.Length));
        return output.ToString();
    }
    static void Retain(LabConfig config, string current)
    {
        var runs = Directory.GetDirectories(config.DataDirectory).Where(d => RunIdPattern().IsMatch(Path.GetFileName(d)) && File.Exists(Path.Combine(d, "report.json")))
            .Where(d => Json.Read<RunReport>(Path.Combine(d, "report.json")).Kind == ReportKind).OrderDescending().ToList();
        long bytes = runs.Sum(d => new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length));
        while (runs.Count > 1 && (runs.Count > config.RetainRuns || bytes > config.RetainBytes))
        {
            var old = runs[^1]; if (Path.GetFileName(old) == current) break;
            var info = new DirectoryInfo(old);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Any(f => (f.Attributes & FileAttributes.ReparsePoint) != 0)) throw new IOException("Retention refuses links.");
            bytes -= info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            Directory.Delete(old, true); runs.RemoveAt(runs.Count - 1);
        }
    }
}
