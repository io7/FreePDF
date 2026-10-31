using FreePDF.Lab;

if (args is ["--self-test"]) { Pixels.SelfTest(); return 0; }
if (args is ["--run", var config]) return await Runner.Run(Json.Read<LabConfig>(config));
var builder = WebApplication.CreateBuilder(args);
var data = Path.GetFullPath(builder.Configuration["Lab:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "runs"));
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "FreePDF Lab", mode = "read-only" }));
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/api/runs", () => {
    if (!Directory.Exists(data)) return Results.Ok(Array.Empty<object>());
    var runs = Directory.GetDirectories(data).Where(d => Runner.RunIdPattern().IsMatch(Path.GetFileName(d)) && File.Exists(Path.Combine(d, "report.json")))
        .OrderDescending().Take(100).Select(d => Json.Read<RunReport>(Path.Combine(d, "report.json")))
        .Where(r => r.Kind == Runner.ReportKind).Select(r => new { r.Id, r.Started, r.SourceRevision, r.Profile, matches = r.Cases.Count(c => c.Status == "match"), reviews = r.Cases.Count(c => c.Status == "review"), failures = r.Cases.Count(c => c.Status == "failed") });
    return Results.Ok(runs);
});
app.MapGet("/api/runs/{id}", (string id) => {
    var file = Path.Combine(data, id, "report.json");
    return Runner.RunIdPattern().IsMatch(id) && File.Exists(file) ? Results.Json(Json.Read<RunReport>(file), Json.Options) : Results.NotFound();
});
app.MapGet("/artifacts/{id}/{**path}", (string id, string path) => {
    if (!Runner.RunIdPattern().IsMatch(id) || string.IsNullOrEmpty(path) || path.Contains('\\') || path.Split('/').Any(s => s is ".." or "." or "")) return Results.NotFound();
    var root = Path.GetFullPath(Path.Combine(data, id)) + Path.DirectorySeparatorChar;
    var file = Path.GetFullPath(Path.Combine(root, path));
    if (!file.StartsWith(root, StringComparison.Ordinal) || !File.Exists(file)) return Results.NotFound();
    for (var check = file; check.Length >= root.Length - 1; check = Path.GetDirectoryName(check)!)
        if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) return Results.NotFound();
    var type = Path.GetExtension(file) switch { ".png" => "image/png", ".pdf" => "application/pdf", ".json" => "application/json", ".log" => "text/plain", _ => null };
    return type == null ? Results.NotFound() : Results.File(file, type, fileDownloadName: type == "application/pdf" ? Path.GetFileName(file) : null);
});
await app.RunAsync();
return 0;
