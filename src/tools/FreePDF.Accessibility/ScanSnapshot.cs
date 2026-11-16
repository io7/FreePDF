// Original FreePDF implementation. See LICENSE.
namespace FreePDF.Accessibility;

/// <summary>Both validators inspect the same bounded copy; the original is never passed to an external program.</summary>
internal sealed class ScanSnapshot : IDisposable
{
    readonly string _directory;
    internal string Path { get; }

    internal ScanSnapshot(string input, long maximumBytes)
    {
        _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "freepdf-a11y-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Path = System.IO.Path.Combine(_directory, "input.pdf");
        try
        {
            using var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destination = new FileStream(Path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = source.Read(buffer)) != 0)
            {
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("Input exceeds the scanner file-size limit.");
                destination.Write(buffer, 0, read);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        // Do not replace a useful scan verdict with an exception during cleanup.
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
