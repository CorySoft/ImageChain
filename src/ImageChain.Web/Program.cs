using ImageChain.Core.Extensions;
using ImageChain.Web.Auth;
using ImageChain.Web.Logging;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory(),
    WebRootPath = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory(), "wwwroot")
});

builder.Services.AddImageChain(builder.Configuration);
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var logBuffer = new LogRingBuffer();
builder.Logging.AddProvider(new RingLoggerProvider(logBuffer));
builder.Services.AddSingleton(logBuffer);

builder.Services.AddAuthentication("AdminPassword")
    .AddScheme<AuthenticationSchemeOptions, AdminPasswordHandler>("AdminPassword", opts => { })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>("ApiKey", opts => { });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/v1"))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        if (context.Request.Method == HttpMethods.Post && context.Request.ContentLength is > 0)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8, leaveOpen: true, detectEncodingFromByteOrderMarks: false);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            logger.LogInformation("[REQ] 收到请求 {Method} {Path} 请求体={Body}", context.Request.Method, context.Request.Path, body[..Math.Min(body.Length, 150)]);
        }
        else
        {
            logger.LogInformation("[REQ] 收到请求 {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await next();
        sw.Stop();
        logger.LogInformation("[RESP] 返回 {Path} => {Status}，耗时 {Ms}",
            context.Request.Path, context.Response.StatusCode, FmtSec(sw.ElapsedMilliseconds));
    }
    else
    {
        await next();
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

static string FmtSec(long ms) => (ms / 1000.0).ToString("0.###") + "s";

app.Run();
