using System.Text.Json;

namespace FreePDF.Lab;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException(path);
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
}
public sealed record Command(string FileName, string[] Arguments);
public sealed record LabConfig(string DataDirectory, Command Reference, Command Candidate, string Renderer,
    string SourceRevision, int RetainRuns = 10, long RetainBytes = 2_000_000_000, int TimeoutSeconds = 30, string Profile = ".NET 10");
public sealed record ProcessResult(int ExitCode, string Output);
public sealed record PixelMetrics(int Width, int Height, bool SameDimensions, long ChangedPixels, double ChangedPercent, double RootMeanSquareError, int MaximumDelta);
public sealed record PageResult(int Number, string? Reference, string? Candidate, string? Difference, PixelMetrics? Pixels);
public sealed record CaseResult(string Id, string Title, string Status, string? KnownReferenceDefect,
    string? ReferenceError, string? CandidateError, string[] StructuralErrors, List<PageResult> Pages);
public sealed record RunReport(string Kind, string Id, DateTimeOffset Started, string SourceRevision, string Profile,
    string RendererVersion, string RendererSha256, string FontSha256, int Dpi, int ChannelTolerance,
    string ReferenceCommand, string CandidateCommand, List<CaseResult> Cases);
