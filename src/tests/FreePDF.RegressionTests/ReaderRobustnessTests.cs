// Original malformed PDFs and isolated timeout checks. MIT; see LICENSE.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PdfSharp.Pdf.IO;
using Xunit;
using FreePDF.Lab.Fixtures;

namespace FreePDF.RegressionTests;

// The test assembly also acts as a tiny child process. A timeout or stack overflow
// in the reader must fail one test without taking down the test runner itself.
static class ReaderProbe
{
    static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--reader-probe") return 2;
        try
        {
            var data = args[1].StartsWith("mutation-", StringComparison.Ordinal)
                ? Mutation(int.Parse(args[1].Substring(9), CultureInfo.InvariantCulture)) : ReaderFixtureData.Get(args[1]);
            using var input = new MemoryStream(data);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            Console.WriteLine("OPENED: " + document.PageCount);
            return 0;
        }
        catch (PdfReaderException ex) { Console.WriteLine("REJECTED: " + ex.Message); return 11; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 12; }
    }

    static byte[] Mutation(int index)
    {
        // Fixed corpus and arithmetic, identical on Framework and modern .NET.
        var bytes = index < 16 ? ReaderFixtureData.Get("valid") : PreservationFixtures.Semantic();
        if (index % 2 == 0) return bytes.Take(9 + index * 37 % (bytes.Length - 9)).ToArray();
        var position = 9 + index * 53 % (bytes.Length - 9);
        bytes[position] ^= (byte)(1 << (index % 7));
        return bytes;
    }
}

public class ReaderRobustnessTests
{
    [Theory]
    [InlineData("valid", 0, "OPENED: 1")]
    [InlineData("incremental", 0, "OPENED: 1")]
    [InlineData("trailer-self", 11, "cross-reference")]
    [InlineData("trailer-cycle", 11, "cross-reference")]
    [InlineData("trailer-negative", 11, "cross-reference")]
    [InlineData("trailer-outside", 11, "cross-reference")]
    [InlineData("tree-self", 11, "page tree")]
    [InlineData("tree-cycle", 11, "page tree")]
    [InlineData("tree-repeated", 11, "page tree")]
    [InlineData("tree-deep", 0, "OPENED: 1")]
    [InlineData("hybrid-outside", 11, "cross-reference")]
    [InlineData("hybrid-table", 11, "cross-reference")]
    public async Task Reader_finishes_or_rejects_invalid_structure_without_hanging(string scenario, int exitCode, string message)
    {
        var result = await Probe(scenario);
        Assert.True(result.ExitCode == exitCode, $"Expected exit {exitCode}, actual {result.ExitCode}: {result.Output}");
        Assert.Contains(message, result.Output);
    }

    public static IEnumerable<object[]> Mutations() => Enumerable.Range(0, 32).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Original_mutation_corpus_opens_or_reports_a_reader_error(int index)
    {
        var result = await Probe("mutation-" + index.ToString(CultureInfo.InvariantCulture));
        Assert.True(result.ExitCode == 0 || result.ExitCode == 11, $"Unexpected reader failure for mutation {index}: {result.Output}");
    }

    static async Task<(int ExitCode, string Output)> Probe(string scenario)
    {
#if NETFRAMEWORK
        // xUnit shadow-copies Framework assemblies into separate cache folders.
        // Launch the original build output so the child can resolve its DLLs/config.
        var assembly = new Uri(typeof(ReaderRobustnessTests).Assembly.CodeBase).LocalPath;
        var executable = assembly;
        var arguments = "--reader-probe " + scenario;
#else
        var assembly = typeof(ReaderRobustnessTests).Assembly.Location;
        var executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var arguments = "\"" + assembly + "\" --reader-probe " + scenario;
#endif
        var start = new ProcessStartInfo(executable, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.EnvironmentVariables["DOTNET_GCHeapHardLimit"] = "0x10000000";
        start.EnvironmentVariables["COMPlus_GCHeapHardLimit"] = "0x10000000";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var watch = Stopwatch.StartNew();
        var completed = false;
        while (watch.ElapsedMilliseconds < 10000)
        {
            if (process.WaitForExit(50)) { completed = true; break; }
            try
            {
                process.Refresh();
                if (process.WorkingSet64 > 384L * 1024 * 1024) break;
            }
            catch (InvalidOperationException) when (process.HasExited) { completed = true; break; }
        }
        if (!completed)
        {
            try { process.Kill(); } catch (InvalidOperationException) when (process.HasExited) { }
            process.WaitForExit();
        }
        var stdout = await output;
        var stderr = await error;
        Assert.True(completed, "Reader exceeded the 10 second or 384 MiB process budget for " + scenario);
        return (process.ExitCode, stdout + " " + stderr.Substring(0, Math.Min(stderr.Length, 1000)));
    }
}
