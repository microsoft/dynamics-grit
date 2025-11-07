using System.Diagnostics;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        var sw = Stopwatch.StartNew();

        logger.LogInformation("Incoming request: {Method} {Path}", context.Request.Method, context.Request.Path);

        await next(context);
        var statusCode = context.Response.StatusCode;
        var message = context.Response.Body.ToString() ?? string.Empty;

        sw.Stop();
        logger.LogInformation("Completed response: {StatusCode} in {Elapsed}ms",
           statusCode, sw.ElapsedMilliseconds);
        logger.LogInformation("Body : {Message}", message);
    }
}
