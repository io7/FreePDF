// Time-boxed mutation fuzzing for the FreePDF reader and writer.
//
//   CorpusCheck fuzz <seed-dir> <output-dir> [--minutes <n>] [--seed <n>] [--workers <n>]
//
// Each round takes a seed PDF (at most 256 KB), applies one to four mutations aimed at PDF structure (bit flips,
// syntax-character substitutions, deleted, duplicated or inserted ranges, truncation, corrupted numbers next to
// 'obj', '/Length', 'xref' and 'startxref'), and runs it through the same child-process check as the corpus run with
// a 10 s timeout and a 512 MB GC heap limit. A refusal with PdfReaderException is the expected outcome for broken
// input; any other exception, a timeout or a dead process is a finding. Findings are grouped by signature (outcome,
// exception type and first FreePDF frame) and the smallest reproducer per signature is kept.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

static class Fuzzer
{
    static readonly byte[] SyntaxBytes = Encoding.ASCII.GetBytes("()<>[]{}/%\\\n\r 0129-.Rnobjstreamendxref");
    static readonly string[] Anchors = [" obj", "/Length", "xref", "startxref", "/Size", "/Prev", "/W ", "/Index", "/N ", "/First", " R"];

    public static int Run(string seedDir, string output, string[] options)
    {
        var minutes = 30.0;
        var seed = 1;
        var workers = 3;
        for (var i = 0; i < options.Length; i++)
        {
            if (options[i] == "--minutes") minutes = double.Parse(options[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (options[i] == "--seed") seed = int.Parse(options[++i]);
            else if (options[i] == "--workers") workers = int.Parse(options[++i]);
        }
        var seeds = Directory.EnumerateFiles(seedDir, "*.pdf", SearchOption.AllDirectories)
            .Where(f => new FileInfo(f).Length <= 256 * 1024).Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray();
        if (seeds.Length == 0) { Console.Error.WriteLine("No seed PDFs of at most 256 KB."); return 2; }
        Directory.CreateDirectory(output);
        var work = Directory.CreateDirectory(Path.Combine(output, "work")).FullName;
        var findingsDir = Directory.CreateDirectory(Path.Combine(output, "findings")).FullName;
        var self = Environment.ProcessPath!;
        var selfArgs = self.EndsWith("dotnet", StringComparison.Ordinal) ? new[] { typeof(Fuzzer).Assembly.Location } : Array.Empty<string>();
        var environment = new Dictionary<string, string> { ["DOTNET_GCHeapHardLimit"] = "0x20000000" };

        var deadline = DateTime.UtcNow.AddMinutes(minutes);
        var findings = new ConcurrentDictionary<string, (int Count, int Size, string File)>();
        var outcomes = new ConcurrentDictionary<string, int>();
        var executions = 0;
        Parallel.For(0, workers, worker =>
        {
            var random = new Random(seed * 1000 + worker);
            var input = Path.Combine(work, $"w{worker}.pdf");
            var resaved = Path.Combine(work, $"w{worker}-resaved.pdf");
            while (DateTime.UtcNow < deadline)
            {
                var data = Mutate(seeds[random.Next(seeds.Length)], random);
                File.WriteAllBytes(input, data);
                var (code, text) = Parent.Execute(self, [.. selfArgs, "check", input, resaved], TimeSpan.FromSeconds(10), environment);
                Interlocked.Increment(ref executions);
                var outcome = code is null ? "timeout" : Outcome.Name(code.Value);
                outcomes.AddOrUpdate(outcome, 1, (_, n) => n + 1);
                if (outcome is "ok" or "rejected" or "password") continue;
                var signature = outcome + " | " + Parent.Signature(text.Trim());
                var name = "finding-" + Math.Abs(StableHash(signature)).ToString("D10") + ".pdf";
                findings.AddOrUpdate(signature,
                    _ => { File.WriteAllBytes(Path.Combine(findingsDir, name), data); Console.WriteLine($"NEW {signature}"); return (1, data.Length, name); },
                    (_, f) =>
                    {
                        if (data.Length < f.Size) { File.WriteAllBytes(Path.Combine(findingsDir, f.File), data); return (f.Count + 1, data.Length, f.File); }
                        return (f.Count + 1, f.Size, f.File);
                    });
            }
        });

        var report = new StringBuilder($"# Mutation run: {executions} inputs from {seeds.Length} seeds, {minutes} min, seed {seed}\n\n");
        report.AppendLine("| Outcome | Inputs |").AppendLine("| --- | --- |");
        foreach (var (outcome, count) in outcomes.OrderByDescending(o => o.Value)) report.AppendLine($"| {outcome} | {count} |");
        report.AppendLine().AppendLine("## Findings").AppendLine();
        foreach (var (signature, f) in findings.OrderByDescending(f => f.Value.Count))
            report.AppendLine($"- {signature} ({f.Count} inputs; smallest {f.Size} bytes: findings/{f.File})");
        File.WriteAllText(Path.Combine(output, "summary.md"), report.ToString());
        File.WriteAllText(Path.Combine(output, "findings.json"), JsonSerializer.Serialize(findings.ToDictionary(f => f.Key, f => f.Value.File)));
        Console.WriteLine($"{executions} inputs; " + string.Join(", ", outcomes.OrderBy(o => o.Key).Select(o => $"{o.Key} {o.Value}")) + $"; {findings.Count} distinct findings");
        return 0;
    }

    static byte[] Mutate(byte[] seed, Random random)
    {
        var data = new List<byte>(seed);
        var rounds = 1 + random.Next(4);
        for (var round = 0; round < rounds && data.Count > 16; round++)
        {
            var at = random.Next(data.Count);
            switch (random.Next(8))
            {
                case 0: data[at] ^= (byte)(1 << random.Next(8)); break;
                case 1: data[at] = SyntaxBytes[random.Next(SyntaxBytes.Length)]; break;
                case 2: data.RemoveRange(at, Math.Min(1 + random.Next(64), data.Count - at)); break;
                case 3:
                {
                    var length = Math.Min(1 + random.Next(256), data.Count - at);
                    data.InsertRange(random.Next(data.Count), data.GetRange(at, length));
                    break;
                }
                case 4: data.InsertRange(at, Enumerable.Range(0, 1 + random.Next(16)).Select(_ => (byte)random.Next(256))); break;
                case 5: data.RemoveRange(at, data.Count - at); break;
                default: CorruptNumberNearAnchor(data, random); break;
            }
        }
        return data.ToArray();
    }

    // Replace the digits next to a structural keyword with a boundary value, which is where reader bugs hide.
    static void CorruptNumberNearAnchor(List<byte> data, Random random)
    {
        var text = Encoding.Latin1.GetString(data.ToArray());
        var anchor = Anchors[random.Next(Anchors.Length)];
        var hits = new List<int>();
        for (var i = text.IndexOf(anchor, StringComparison.Ordinal); i >= 0 && hits.Count < 64; i = text.IndexOf(anchor, i + 1, StringComparison.Ordinal))
            hits.Add(i);
        if (hits.Count == 0) return;
        var position = hits[random.Next(hits.Count)];
        var direction = random.Next(2) == 0 ? -1 : 1;
        var start = position + (direction > 0 ? anchor.Length : 0);
        while (start >= 0 && start < text.Length && !char.IsDigit(text[start]) && Math.Abs(start - position) < 24) start += direction;
        if (start < 0 || start >= text.Length || !char.IsDigit(text[start])) return;
        var end = start;
        while (start > 0 && char.IsDigit(text[start - 1])) start--;
        while (end < text.Length && char.IsDigit(text[end])) end++;
        string[] values = ["0", "1", "-1", "2147483647", "2147483648", "99999999999", "4294967295", "65535", (end - start > 1 ? text[start..(end - 1)] : "7")];
        var replacement = Encoding.ASCII.GetBytes(values[random.Next(values.Length)]);
        data.RemoveRange(start, end - start);
        data.InsertRange(start, replacement);
    }

    static int StableHash(string s)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in s) hash = (hash ^ c) * 16777619;
            return hash == int.MinValue ? 0 : hash;
        }
    }
}
