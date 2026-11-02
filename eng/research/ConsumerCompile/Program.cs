// Compiles C# sources against a fixed set of reference assemblies and writes every error diagnostic as JSON.
// csc stops after the declaration phase when it finds errors there, which hides method-body errors whenever a consumer
// package is unresolved. Compilation.GetDiagnostics() reports all phases, so a FreePDF break inside a method body is
// still seen. The sources are only parsed and bound, never emitted or run.
//
// Usage: ConsumerCompile <request.json> <errors.json>
// request.json: { "root": "...", "sources": [...], "references": [...], "defines": [...], "globalUsings": [...],
//                 "nullable": "enable|disable|warnings|annotations" }

using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ConsumerCompile <request.json> <errors.json>");
    return 2;
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(args[0]), options)
    ?? throw new InvalidOperationException("Empty request.");

var parse = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: request.Defines);
var trees = new List<SyntaxTree>();
foreach (var path in request.Sources)
{
    using var stream = File.OpenRead(path);
    trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(stream), parse, path));
}
if (request.GlobalUsings.Count > 0)
{
    var text = string.Join('\n', request.GlobalUsings.Select(u => $"global using global::{u};"));
    trees.Add(CSharpSyntaxTree.ParseText(text, parse, "<global usings>"));
}

var nullable = request.Nullable?.ToLowerInvariant() switch
{
    "enable" => NullableContextOptions.Enable,
    "warnings" => NullableContextOptions.Warnings,
    "annotations" => NullableContextOptions.Annotations,
    _ => NullableContextOptions.Disable,
};
var compilation = CSharpCompilation.Create(
    "SurveyConsumer",
    trees,
    request.References.Select(r => MetadataReference.CreateFromFile(r)),
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: nullable));

var errors = compilation.GetDiagnostics()
    .Where(d => d.Severity == DiagnosticSeverity.Error)
    .Select(d =>
    {
        var span = d.Location.GetLineSpan();
        var file = span.Path is { Length: > 0 } p && Path.IsPathRooted(p) ? Path.GetRelativePath(request.Root, p).Replace('\\', '/') : span.Path;
        return new Error(file, span.StartLinePosition.Line + 1, d.Id, d.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    })
    .ToList();

File.WriteAllText(args[1], JsonSerializer.Serialize(errors, options));
Console.WriteLine($"{trees.Count} files, {request.References.Count} references, {errors.Count} errors");
return 0;

record Request(string Root, List<string> Sources, List<string> References, List<string> Defines, List<string> GlobalUsings, string? Nullable);

record Error(string File, int Line, string Code, string Message);
