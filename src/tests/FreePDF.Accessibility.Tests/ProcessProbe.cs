// Original, synthetic subprocess controls. Never used by the shipped CLI.
namespace FreePDF.Accessibility.Tests;

internal static class ProcessProbe
{
    static async Task<int> Main(string[] args)
    {
        if (args[0] == "--format")
        {
            // Adapter integration fixture: the last argument is its private snapshot.
            var input = args[^1];
            var mode = File.ReadAllText(input);
            if (mode == "bad-report") { Console.Write("not XML"); return 0; }
            bool failing = mode.StartsWith("fail", StringComparison.Ordinal);
            var report = VeraPdfAdapterTests.Report(passed: !failing);
            report.Descendants("name").Single().Value = input;
            report.Descendants("item").Single().SetAttributeValue("size", new FileInfo(input).Length);
            // Keep the length, so only the hash comparison can detect the change.
            if (mode.EndsWith("change-input", StringComparison.Ordinal)) File.WriteAllText(input, mode.ToUpperInvariant());
            if (mode == "stderr-flood") Console.Error.Write(new string('x', 70 * 1024));
            // The adapter can still read the length, but not the bytes for its hash check.
            if (mode == "failing-unreadable" && !OperatingSystem.IsWindows()) File.SetUnixFileMode(input, UnixFileMode.UserWrite);
            Console.Write(report.ToString());
            return failing ? 1 : 0;
        }
        switch (args[0])
        {
            case "sleep": await Task.Delay(TimeSpan.FromSeconds(30)); break;
            case "output": Console.Write(new string('x', 9 * 1024 * 1024)); break;
            case "stderr": Console.Error.Write(new string('x', 70 * 1024)); break;
            case "args": Console.Write(System.Text.Json.JsonSerializer.Serialize(args.Skip(1))); break;
            case "scan":
                // Scan and write the JSON report, as the CLI does, under the caller's heap and time limits.
                // Output: scan status | READ outcome | distinct NotChecked messages | JSON length.
                var options = new PdfSharp.UniversalAccessibility.PdfAccessibilityScanOptions { MaxDuration = TimeSpan.FromSeconds(int.Parse(args[2])) };
                if (args.Length > 3) options.MaxNodes = int.Parse(args[3]);
                var report = PdfSharp.UniversalAccessibility.PdfAccessibilityScanner.Scan(args[1], options);
                var read = report.Checks.Single(c => c.Id == "FPDF-A11Y-READ").Status;
                var notChecked = String.Join(";", report.Findings
                    .Where(f => f.Status == PdfSharp.UniversalAccessibility.PdfAccessibilityCheckStatus.NotChecked)
                    .Select(f => f.Message).Distinct());
                Console.Write(report.Status + "|" + read + "|" + notChecked + "|" + AccessibilityReportWriter.Write(report, "input.pdf", "json").Length);
                break;
            case "exit": return 17;
        }
        return 0;
    }
}
