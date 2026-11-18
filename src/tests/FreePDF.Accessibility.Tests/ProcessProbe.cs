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
            case "exit": return 17;
        }
        return 0;
    }
}
