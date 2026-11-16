// Original FreePDF implementation. See LICENSE.
using System.Diagnostics;
using System.Text;

namespace FreePDF.Accessibility;

internal sealed record ValidatorOutput(int ExitCode, byte[] Report);

internal static class ValidatorProcess
{
    internal const int MaximumReportBytes = 8 * 1024 * 1024;

    internal static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows() && Path.GetExtension(executable).ToLowerInvariant() is ".bat" or ".cmd")
        {
            // cmd expands variables even inside quotes. Reject those paths, rather
            // than treating document or executable paths as shell expressions.
            var values = new[] { executable }.Concat(arguments).ToArray();
            if (values.Any(v => v.Any(c => c is '"' or '%' or '!' || char.IsControl(c))))
                throw new ArgumentException("The Windows validator launcher path contains unsupported shell characters.");
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            info.Arguments = "/d /v:off /s /c \"" + string.Join(" ", values.Select(v => "\"" + v + "\"")) + "\"";
        }
        else
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    internal static async Task<ValidatorOutput> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, arguments) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        process.Start();
        // Kill the entire tree when a limit is reached, including a launcher's JVM.
        using var registration = deadline.Token.Register(() => Kill(process));
        var output = ReadBounded(process.StandardOutput.BaseStream, MaximumReportBytes, deadline);
        var errors = ReadBounded(process.StandardError.BaseStream, 64 * 1024, deadline);
        try
        {
            await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return new ValidatorOutput(process.ExitCode, await output.ConfigureAwait(false));
        }
        catch
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            // Preserve a size-limit error when cancellation also interrupted a reader.
            if (output.Exception?.InnerException is InvalidDataException limit) throw limit;
            if (errors.Exception?.InnerException is InvalidDataException stderrLimit) throw stderrLimit;
            if (deadline.IsCancellationRequested) throw new TimeoutException("Validator exceeded its time limit.");
            throw;
        }
    }

    static async Task<byte[]> ReadBounded(Stream stream, int limit, CancellationTokenSource deadline)
    {
        using var result = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            if (read == 0) return result.ToArray();
            if (result.Length + read > limit)
            {
                deadline.Cancel();
                throw new InvalidDataException("Validator output exceeded its size limit.");
            }
            result.Write(buffer, 0, read);
        }
    }

    static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
