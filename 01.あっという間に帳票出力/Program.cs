using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Pao.Reports.Web;

JsonObject CreatePrintData()
{
    var definition = JsonNode.Parse(File.ReadAllText("definition/quick-report.prepdj"))!.AsObject();
    var report = new PrintData().SetDefinition(definition).PageStart()
        .SetValue("Title", "C#/.NETから、あっという間に帳票出力")
        .SetValue("CustomerName", "株式会社パオ").PageEnd();
    return JsonNode.Parse(report.ToJson())!.AsObject();
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");
var app = builder.Build();
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider("/app/reports.web"), RequestPath = "/reports.web" });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider("/app/reports.web"), RequestPath = "/demo/reports.web" });
app.MapGet("/", () => Results.File("/app/index.html", "text/html; charset=utf-8"));
app.MapGet("/print-data", () => Results.Json(CreatePrintData()));
app.MapPost("/pdf", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    // Reports.Web (NuGet): POST /render/pdf to the Reports.Web engine.
    using var engine = new ReportsWebEngine(Environment.GetEnvironmentVariable("REPORTS_ENGINE_URL") ?? "http://engine:3107");
    return Results.File(await engine.RenderPdfAsync(await reader.ReadToEndAsync()), "application/pdf");
});
app.Run();
