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
            using var input = new MemoryStream(ReaderFixtureData.Get(args[1]));
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            Console.WriteLine("OPENED: " + document.PageCount);
            return 0;
        }
        catch (PdfReaderException ex) { Console.WriteLine("REJECTED: " + ex.Message); return 11; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 12; }
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
        using var process = Process.Start(new ProcessStartInfo(executable, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var completed = process.WaitForExit(10000);
        if (!completed) { process.Kill(); process.WaitForExit(); }
        var stdout = await output;
        var stderr = await error;
        Assert.True(completed, "Reader exceeded 10 seconds for " + scenario);
        Assert.True(process.ExitCode == exitCode, $"Expected exit {exitCode}, actual {process.ExitCode}: {stdout} {stderr.Substring(0, Math.Min(stderr.Length, 1000))}");
        Assert.Contains(message, stdout);
    }
}
