using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json.Nodes;
using ReportsWeb.Sample;
using Pao.Reports.Web;
using System.Net;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32 * 1024 * 1024);
var app = builder.Build();
var repo = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../.."));
var resources = Environment.GetEnvironmentVariable("REPORTS_RESOURCE_ROOT") ?? Path.Combine(repo, "samples/php/resources");
var web = Environment.GetEnvironmentVariable("REPORTS_WEB_ROOT") ?? Path.Combine(repo, "deploy/reports.web");
const string prefix = "/demo/reports.web/samples/dotnet";
var assetBase = Environment.GetEnvironmentVariable("REPORTS_PUBLIC_ASSET_BASE") ?? "http://127.0.0.1:8093" + prefix + "/api?action=asset&name=";
var db = Environment.GetEnvironmentVariable("REPORTS_CONNECTION_STRING") ?? "Host=127.0.0.1;Port=5434;Database=reports_web_sample;Username=reports_web;Password=reports-web-local-only;Maximum Pool Size=10";
var engine = Environment.GetEnvironmentVariable("REPORTS_ENGINE_URL") ?? "http://127.0.0.1:3107";
var catalog = new SampleCatalog(resources, db);
var assets = new AssetResolver(resources, assetBase, (Environment.GetEnvironmentVariable("REPORTS_TRUSTED_ASSET_BASES") ?? "http://127.0.0.1:8088/samples/php/api.php?action=asset&name=").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
var gate = new SemaphoreSlim(1);
using var http = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    ConnectTimeout = TimeSpan.FromSeconds(10)
})
{
    Timeout = TimeSpan.FromSeconds(90)
};
using var engineClient = new ReportsWebEngine(engine, http);
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    try
    {
        await next();
    }
    catch (Exception e)
    {
        if (ctx.Response.HasStarted)
            throw;
        ctx.Response.StatusCode = e switch
        {
            ArgumentException or System.Text.Json.JsonException => 400,
            BadHttpRequestException b => b.StatusCode,
            OperationCanceledException => 504,
            _ => 502
        };
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = ctx.Response.StatusCode == 400 ? "印刷データ・操作・画像を確認してください。" : ctx.Response.StatusCode == 413 ? "印刷データは32MiB以内にしてください。" : "帳票を処理できませんでした。接続先を確認してください。"
        });
        app.Logger.LogWarning("Report operation failed ({Type})", e.GetType().Name);
    }
});
if (Directory.Exists(web))
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path == "/demo/reports.web/preview" || ctx.Request.Path == "/demo/reports.web/preview/")
            ctx.Request.Path = "/demo/reports.web/preview/index.html";
        if (ctx.Request.Path == "/demo/reports.web/design" || ctx.Request.Path == "/demo/reports.web/design/")
            ctx.Request.Path = "/demo/reports.web/design/index.html";
        await next();
    });
    var types = new FileExtensionContentTypeProvider();
    types.Mappings[".mjs"] = "text/javascript";
    types.Mappings[".wasm"] = "application/wasm";
    types.Mappings[".prepdj"] = "application/json";
    types.Mappings[".prepej"] = "application/json";
    foreach (var dir in new[] {
 "preview", "design", "assets", "barcode", "fonts", "wasm" })
        if (Directory.Exists(Path.Combine(web, dir)))
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(web, dir)),
                RequestPath = "/demo/reports.web/" + dir,
                ContentTypeProvider = types
            });
    foreach (var file in new[] { "font-map.json", "fontmap.json", "preview-help.html" })
        app.MapGet("/demo/reports.web/" + file, () => File.Exists(Path.Combine(web, file))
            ? Results.File(Path.Combine(web, file), file.EndsWith(".html") ? "text/html; charset=utf-8" : "application/json")
            : Results.NotFound());
}
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    runtime = "dotnet"
}));
app.MapGet("/", () => Results.Redirect(prefix + "/"));
app.MapGet(prefix + "/health", () => Results.Json(new
{
    status = "ok"
}));
app.MapGet(prefix + "/", (HttpContext ctx) =>
{
    if (!ctx.Request.Path.Value!.EndsWith('/'))
        return Results.Redirect(prefix + "/" + ctx.Request.QueryString);
    var selected = ctx.Request.Query["sample"].ToString();
    if (!SampleCatalog.Samples.ContainsKey(selected))
        selected = "invoice";
    var options = string.Join("", SampleCatalog.Samples.Select(x => $"<option value=\"{x.Key}\"{(x.Key == selected ? " selected" : "")}>{WebUtility.HtmlEncode(x.Value)}</option>"));
    return Results.Content(File.ReadAllText(Path.Combine(app.Environment.ContentRootPath, "index.html")).Replace("{{OPTIONS}}", options), "text/html; charset=utf-8");
});
foreach (var endpoint in new[] {
 prefix + "/api", prefix + "/api.php" })
{
    app.MapGet(endpoint, (HttpContext ctx) =>
    {
        var action = ctx.Request.Query["action"].ToString();
        if (action == "")
            action = "data";
        var sample = ctx.Request.Query["sample"].ToString();
        if (sample == "")
            sample = "invoice";
        if (action == "catalog")
            return Results.Json(SampleCatalog.Samples);
        if (action == "asset")
        {
            var name = ctx.Request.Query["name"].ToString();
            ctx.Response.Headers.CacheControl = "public,max-age=86400";
            return Results.File(assets.Read(name), name.EndsWith(".jpg") ? "image/jpeg" : "image/png");
        }
        JsonObject data;
        if (action == "definition")
        {
            data = catalog.Definition(sample);
            assets.Externalize(data, sample);
        }
        else if (action == "data")
        {
            data = catalog.Create(sample).ToJsonObject();
            if (data["Definition"] is JsonObject rootDefinition)
                assets.Externalize(rootDefinition, sample);
            foreach (var page in data["Pages"]!.AsArray())
            {
                if (page!["Definition"] is JsonObject d)
                    assets.Externalize(d, sample);
                if (sample == "invoice")
                    foreach (var v in page["Values"]!.AsArray())
                        if (v!["Name"]?.ToString() == "Image1")
                            v["Value"] = assetBase + "kakuin.png";
            }
        }
        else
            throw new ArgumentException();
        // Saved print data must stay self-contained; browsers cannot resolve
        // an internal Docker hostname, so embed every registered image.
        assets.Inline(data);
        ctx.Response.Headers.ContentDisposition = $"inline; filename=\"{sample}.{(action == "definition" ? "prepdj" : "prepej")}\"";
        return Results.Json(data, contentType: action == "definition" ? "application/vnd.pao.reports-definition+json" : "application/vnd.pao.reports-printdata+json");
    });
    app.MapPost(endpoint, async (HttpContext ctx) =>
    {
        if (ctx.Request.Query["action"] != "server-pdf")
            return Results.BadRequest(new
            {
                error = "未対応の操作です。"
            });
        if (!await gate.WaitAsync(0, ctx.RequestAborted))
            return Results.Json(new
            {
                error = "PDF作成中です。少し待ってからお試しください。"
            }, statusCode: 429);
        try
        {
            // Keep the deadline active while reading the response body as well as its headers.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var bytes = await BoundedRead(ctx.Request.Body, deadline.Token);
            var data = JsonNode.Parse(bytes);
            if (data is not JsonObject || data["Pages"] is not JsonArray pages || pages.Count == 0 || (data["Format"] != null && data["Format"]!.ToString() != "Reports.net PrintData"))
                throw new ArgumentException();
            assets.Inline(data);
            // Reports.Web (NuGet): POST /render/pdf to the Reports.Web engine.
            byte[] pdf;
            try
            {
                pdf = await engineClient.RenderPdfAsync(data, cancellationToken: deadline.Token);
            }
            catch (ReportsWebEngineException)
            {
                return Results.Json(new
                {
                    error = "サーバーでPDFを作成できませんでした。"
                }, statusCode: 502);
            }
            ctx.Response.Headers["X-Reports-Engine"] = "server-wasm";
            ctx.Response.Headers.ContentDisposition = "inline; filename=\"report.pdf\"";
            return Results.File(pdf, "application/pdf");
        }
        finally
        {
            gate.Release();
        }
    });
}
app.Run();
static async Task<byte[]> BoundedRead(Stream stream, CancellationToken token)
{
    const int max = 32 * 1024 * 1024;
    using var output = new MemoryStream();
    var buffer = new byte[65536];
    int n;
    while ((n = await stream.ReadAsync(buffer, token)) > 0)
    {
        if (output.Length + n > max)
            throw new BadHttpRequestException("Too large", 413);
        output.Write(buffer, 0, n);
    }
    return output.ToArray();
}
public partial class Program
{
}
