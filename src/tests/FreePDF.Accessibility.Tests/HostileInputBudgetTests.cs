// Original FreePDF memory-budget checks on synthetic hostile inputs. See LICENSE.
using System.Diagnostics;
using Xunit;

namespace FreePDF.Accessibility.Tests;

public sealed class HostileInputBudgetTests
{
    // The inputs are at most a few megabytes. Before the fixes, the first six each needed between 1 and 5 GB and the
    // last three each needed more than a minute of CPU time.
    const long HeapLimit = 256L * 1024 * 1024;
    // A scan that reaches this limit reports READ as NotChecked, which fails the expectation below.
    const int MaxDurationSeconds = 20;

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
            info.ArgumentList.Add(MaxDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (HostileInputs.MaxNodes(input) is int nodes) info.ArgumentList.Add(nodes.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            var parts = (await output).Split('|');
            Assert.Equal(4, parts.Length);
            Assert.DoesNotContain("MaxDuration", parts[2]);
            var (read, message) = HostileInputs.Expected(input);
            Assert.True(read == parts[1], input + ": READ " + parts[1] + "; not checked: " + parts[2]);
            if (message != null) Assert.Contains(message, parts[2]);
        }
        finally { File.Delete(path); }
    }
}
