// Corpus round trip for FreePDF: open, save, reopen and compare every PDF in a folder.
//
//   CorpusCheck run <corpus-dir> <output-dir> [--timeout <seconds>] [--no-render]
//   CorpusCheck check <pdf> <resaved-pdf>          (child process; one file)
//
// Each file is checked in its own child process, so a stack overflow or a hang in the reader fails that file only.
// The parent renders page 1 of the original and the re-saved file with Poppler (pdftoppm, 40 dpi, grey) and
// compares them, which catches content lost on save. Results go to results.jsonl and summary.md.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

return args.FirstOrDefault() switch
{
    "check" when args.Length == 3 => Child.Check(args[1], args[2]),
    "run" when args.Length >= 3 => Parent.Run(args[1], args[2], args.Skip(3).ToArray()),
    "fuzz" when args.Length >= 3 => Fuzzer.Run(args[1], args[2], args.Skip(3).ToArray()),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage: CorpusCheck run <corpus-dir> <output-dir> [--timeout <seconds>] [--no-render]");
    Console.Error.WriteLine("       CorpusCheck fuzz <seed-dir> <output-dir> [--minutes <n>] [--seed <n>] [--workers <n>]");
    return 2;
}

static class Outcome
{
    public const int Ok = 0, Rejected = 11, CrashOpen = 12, CrashSave = 13, CrashReopen = 14, PagesChanged = 15, Password = 16;

    public static string Name(int code) => code switch
    {
        Ok => "ok", Rejected => "rejected", CrashOpen => "crash-open", CrashSave => "crash-save",
        CrashReopen => "crash-reopen", PagesChanged => "pages-changed", Password => "password", _ => "process-" + code,
    };
}

static class Child
{
    public static int Check(string path, string resaved)
    {
        int pages;
        try
        {
            using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            pages = document.PageCount;
            try { document.Save(resaved); }
            catch (Exception ex) { return Report(Outcome.CrashSave, ex); }
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        { return Report(Outcome.Password, ex); }
        catch (PdfReaderException ex) { return Report(Outcome.Rejected, ex); }
        catch (Exception ex) { return Report(Outcome.CrashOpen, ex); }

        try
        {
            using var reopened = PdfReader.Open(resaved, PdfDocumentOpenMode.Import);
            if (reopened.PageCount != pages)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { pages, resavedPages = reopened.PageCount }));
                return Outcome.PagesChanged;
            }
        }
        catch (Exception ex) { return Report(Outcome.CrashReopen, ex); }

        Console.WriteLine(JsonSerializer.Serialize(new { pages }));
        return Outcome.Ok;
    }

    static int Report(int code, Exception ex)
    {
        var frame = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("at PdfSharp.", StringComparison.Ordinal)) ?? "";
        var at = frame.IndexOf(" in ", StringComparison.Ordinal);
        if (at > 0) frame = frame[..at];
        Console.WriteLine(JsonSerializer.Serialize(new { error = ex.GetType().FullName, message = ex.Message, frame }));
        return code;
    }
}

static class Parent
{
    record Result(string File, string Outcome, double Seconds, string Detail, double? RenderDiff);

    public static int Run(string corpus, string output, string[] options)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var render = true;
        for (var i = 0; i < options.Length; i++)
        {
            if (options[i] == "--timeout") timeout = TimeSpan.FromSeconds(int.Parse(options[++i]));
            else if (options[i] == "--no-render") render = false;
        }
        corpus = Path.GetFullPath(corpus);
        Directory.CreateDirectory(output);
        var resavedDir = Directory.CreateDirectory(Path.Combine(output, "resaved")).FullName;
        var renderDir = Directory.CreateDirectory(Path.Combine(output, "render")).FullName;
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var selfArgs = self.EndsWith("dotnet", StringComparison.Ordinal) ? new[] { typeof(Parent).Assembly.Location } : Array.Empty<string>();

        var files = Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
        var results = new List<Result>();
        using var jsonl = new StreamWriter(Path.Combine(output, "results.jsonl"));
        var index = 0;
        foreach (var file in files)
        {
            index++;
            var relative = Path.GetRelativePath(corpus, file);
            var resaved = Path.Combine(resavedDir, index.ToString("D6") + ".pdf");
            var watch = Stopwatch.StartNew();
            var (code, text) = Execute(self, [.. selfArgs, "check", file, resaved], timeout);
            watch.Stop();
            var outcome = code is null ? "timeout" : Outcome.Name(code.Value);
            double? diff = null;
            if (render && outcome == "ok")
            {
                diff = RenderDiff(file, resaved, Path.Combine(renderDir, index.ToString("D6")));
                if (diff is null) outcome = "render-failed";
                else if (diff > 0.005) outcome = "render-diff";
            }
            var result = new Result(relative, outcome, Math.Round(watch.Elapsed.TotalSeconds, 2), text.Trim(), diff);
            results.Add(result);
            jsonl.WriteLine(JsonSerializer.Serialize(result));
            jsonl.Flush();
            if (outcome != "ok" && outcome != "rejected" && outcome != "password")
                Console.WriteLine($"[{index}/{files.Count}] {outcome,-14} {relative} {Truncate(text.Trim(), 160)}");
        }
        File.WriteAllText(Path.Combine(output, "summary.md"), Summary(corpus, results));
        Console.WriteLine(string.Join(", ", results.GroupBy(r => r.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
        return 0;
    }

    internal static (int? Code, string Output) Execute(string file, string[] arguments, TimeSpan timeout, IDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (environment != null) foreach (var (key, value) in environment) info.Environment[key] = value;
        foreach (var a in arguments) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit();
            return (null, "");
        }
        var text = stdout.Result;
        if (process.ExitCode is not (0 or 11 or 12 or 13 or 14 or 15 or 16))
            text += " " + Truncate(stderr.Result.Replace('\n', ' '), 400);
        return (process.ExitCode, text);
    }

    // Fraction of pixels on page 1 whose grey level differs by more than 32 between original and re-saved file.
    static double? RenderDiff(string original, string resaved, string prefix)
    {
        var a = Render(original, prefix + "-a");
        var b = Render(resaved, prefix + "-b");
        if (a is null || b is null) return null;
        if (a.Value.Width != b.Value.Width || a.Value.Height != b.Value.Height) return 1.0;
        var differing = 0;
        for (var i = 0; i < a.Value.Pixels.Length; i++)
            if (Math.Abs(a.Value.Pixels[i] - b.Value.Pixels[i]) > 32) differing++;
        return (double)differing / a.Value.Pixels.Length;
    }

    static (int Width, int Height, byte[] Pixels)? Render(string pdf, string prefix)
    {
        var (code, _) = Execute("pdftoppm", ["-gray", "-r", "40", "-f", "1", "-l", "1", "-singlefile", pdf, prefix], TimeSpan.FromSeconds(60));
        var path = prefix + ".pgm";
        if (code != 0 || !File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        // Binary PGM: "P5\n<w> <h>\n<max>\n" then w*h bytes.
        var fields = new List<string>();
        var pos = 0;
        while (fields.Count < 4)
        {
            while (pos < bytes.Length && char.IsWhiteSpace((char)bytes[pos])) pos++;
            if (bytes[pos] == '#') { while (bytes[pos] != '\n') pos++; continue; }
            var start = pos;
            while (pos < bytes.Length && !char.IsWhiteSpace((char)bytes[pos])) pos++;
            fields.Add(Encoding.ASCII.GetString(bytes, start, pos - start));
        }
        pos++;
        int width = int.Parse(fields[1]), height = int.Parse(fields[2]);
        return (width, height, bytes.AsSpan(pos, width * height).ToArray());
    }

    static string Summary(string corpus, List<Result> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Corpus round trip: {results.Count} files under {corpus}").AppendLine();
        sb.AppendLine("| Outcome | Files |").AppendLine("| --- | --- |");
        foreach (var g in results.GroupBy(r => r.Outcome).OrderByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key} | {g.Count()} |");
        sb.AppendLine().AppendLine("## Failures grouped by exception and FreePDF frame").AppendLine();
        var failures = results.Where(r => r.Outcome is "crash-open" or "crash-save" or "crash-reopen" or "timeout" or "pages-changed" or "render-diff" or "render-failed" or "process-134" || r.Outcome.StartsWith("process-", StringComparison.Ordinal));
        foreach (var g in failures.GroupBy(r => r.Outcome + " | " + Signature(r.Detail)).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"### {g.Key} ({g.Count()})");
            foreach (var r in g.Take(5)) sb.AppendLine($"- {r.File}" + (r.RenderDiff is { } d ? $" (diff {d:P1})" : ""));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    internal static string Signature(string detail)
    {
        try
        {
            using var doc = JsonDocument.Parse(detail.Split('\n')[0]);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                return error.GetString() + " @ " + root.GetProperty("frame").GetString();
        }
        catch (JsonException) { }
        return Truncate(detail, 120);
    }

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}
