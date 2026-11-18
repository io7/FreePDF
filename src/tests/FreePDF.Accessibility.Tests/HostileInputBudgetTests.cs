// Original FreePDF memory-budget checks on synthetic hostile inputs. See LICENSE.
using System.Diagnostics;
using Xunit;

namespace FreePDF.Accessibility.Tests;

public sealed class HostileInputBudgetTests
{
    // Each input is a few hundred kilobytes at most; before the fixes each one needed between 1 and 5 GB.
    const long HeapLimit = 256L * 1024 * 1024;

    public static TheoryData<string> Inputs => new(HostileInputs.Names);

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task ScanAndJsonReportStayWithinTheHeapLimit(string input)
    {
        var path = Path.Combine(Path.GetTempPath(), "freepdf-budget-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, HostileInputs.Create(input));
        try
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add(typeof(ProcessProbe).Assembly.Location);
            info.ArgumentList.Add("scan");
            info.ArgumentList.Add(path);
            // A .NET heap hard limit makes the probe fail with OutOfMemoryException instead of using the machine's memory.
            info.Environment["DOTNET_GCHeapHardLimit"] = HeapLimit.ToString("x");
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var error = await errors;
            Assert.True(process.ExitCode == 0, input + " exited with " + process.ExitCode + ": " + error[..Math.Min(error.Length, 2000)]);
            Assert.Matches("^(Failed|Incomplete|NeedsReview) [0-9]+$", await output);
        }
        finally { File.Delete(path); }
    }
}
