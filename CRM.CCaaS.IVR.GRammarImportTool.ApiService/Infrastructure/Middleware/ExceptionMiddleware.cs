using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Middleware;

public class ExceptionMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<ExceptionMiddleware> _logger = GrITLoggerFactory.CreateLogger<ExceptionMiddleware>();

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await _next(context); // Pass request to next middleware
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = ex switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            ArgumentException => StatusCodes.Status400BadRequest,
            FileNotFoundException => StatusCodes.Status404NotFound,
            DirectoryNotFoundException => StatusCodes.Status404NotFound,
            IOException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError // Default for unhandled exceptions
        };

        var response = new
        {
            StatusCode = context.Response.StatusCode,
            Message = "Unexpected error occured",
        };

        return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
}
