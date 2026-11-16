// Original FreePDF process-boundary tests. See LICENSE.
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace FreePDF.Accessibility.Tests;

public sealed class ValidatorProcessTests
{
    static Task<ValidatorOutput> Run(string mode, TimeSpan? timeout = null, CancellationToken cancellation = default, params string[] arguments) =>
        ValidatorProcess.RunAsync(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            new[] { typeof(ProcessProbe).Assembly.Location, mode }.Concat(arguments).ToArray(), timeout ?? TimeSpan.FromSeconds(15), cancellation);

    [Fact]
    public async Task ArgumentsAreNotShellExpressions()
    {
        var values = new[] { "a file.pdf", "$(not-a-command)", "a&b.pdf", "\"quoted\"", "--flavour", "ua1" };
        var result = await Run("args", arguments: values);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(values, JsonSerializer.Deserialize<string[]>(result.Report));
    }

    [Fact]
    public async Task TimeoutStopsTheWorker()
    {
        var timer = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => Run("sleep", TimeSpan.FromMilliseconds(400)));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("output")]
    [InlineData("stderr")]
    public async Task OutputLimitsStopTheWorker(string mode) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(mode));

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("sleep", cancellation: cancellation.Token));
    }

    [Fact]
    public async Task UnexpectedExitIsRetained() => Assert.Equal(17, (await Run("exit")).ExitCode);

    [Fact]
    public async Task LauncherPathsAndArgumentsWithSpacesAndMetacharactersAreLiteral()
    {
        var directory = Path.Combine(Path.GetTempPath(), "freepdf & launch " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "FreePDF.Accessibility.Tests" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var launcher = Path.Combine(directory, OperatingSystem.IsWindows() ? "validator.cmd" : "validator.sh");
            File.WriteAllText(launcher, OperatingSystem.IsWindows()
                ? "@echo off\r\n\"" + executable + "\" args %*\r\nexit /b %errorlevel%\r\n"
                : "#!/bin/sh\nexec '" + executable.Replace("'", "'\\''") + "' args \"$@\"\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string[] values = { "a & b.pdf", "with spaces.pdf", "caret^value", "(parentheses)" };
            var result = await ValidatorProcess.RunAsync(launcher, values, TimeSpan.FromSeconds(15), default);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(values, JsonSerializer.Deserialize<string[]>(result.Report));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SnapshotIsPrivateBoundedAndPreservesTheOriginal()
    {
        var original = Path.GetTempFileName();
        try
        {
            File.WriteAllText(original, "original bytes");
            string copy;
            using (var snapshot = new ScanSnapshot(original, 1024))
            {
                copy = snapshot.Path;
                Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copy));
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(copy)!));
                File.WriteAllText(copy, "changed snapshot");
                Assert.Equal("original bytes", File.ReadAllText(original));
            }
            Assert.False(File.Exists(copy));
            Assert.Throws<InvalidDataException>(() => new ScanSnapshot(original, 2));
            Assert.Equal("original bytes", File.ReadAllText(original));
        }
        finally { File.Delete(original); }
    }
}
