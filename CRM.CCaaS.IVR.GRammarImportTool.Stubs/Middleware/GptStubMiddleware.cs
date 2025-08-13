using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Middleware;

public sealed class GptStubBehaviorMiddleware(
    RequestDelegate next,
    ILogger<GptStubBehaviorMiddleware> logger,
    StubBehaviorState state,
    IStubResultCorruptor corruptor)
{
    private static readonly PathString AdminRoutePrefix = new("/api/stub");
    private static readonly PathString HealthPrefix = new("/health");
    private static readonly PathString AlivePrefix = new("/alive");
    private const string BypassHeader = "X-Stub-Bypass";

    private readonly RequestDelegate _next = next;
    private readonly ILogger<GptStubBehaviorMiddleware> _logger = logger;
    private readonly StubBehaviorState _state = state;
    private readonly IStubResultCorruptor _corruptor = corruptor;

    public async Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path;

        if (path.StartsWithSegments(AdminRoutePrefix, out _) ||
            path.StartsWithSegments(HealthPrefix, out _) ||
            path.StartsWithSegments(AlivePrefix, out _) ||
            HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Allow bypass if explicitly asked
        if (context.Request.Headers.TryGetValue(BypassHeader, out var bypass) &&
            string.Equals(bypass.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var options = _state.Snapshot();
        switch (options.Mode)
        {
            case StubMode.RefuseConnection:
                _logger.LogWarning("GptStub: refusing connection (configured). Path: {Path}", path);
                context.Abort();
                return;

            case StubMode.ErrorResponse:
                _logger.LogWarning("GptStub: returning error {Status}. Path: {Path}", options.ErrorStatusCode, path);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = options.ErrorStatusCode;
                    context.Response.ContentType = "application/problem+json";
                    var payload = $$"""{"title":"Stub error","status":{{options.ErrorStatusCode}},"detail":{{System.Text.Json.JsonSerializer.Serialize(options.ErrorMessage ?? "Error by stub")}}}""";
                    await context.Response.WriteAsync(payload, context.RequestAborted);
                }
                return;

            case StubMode.BadResultsAll:
            case StubMode.BadResultsSome:
                await InterceptAndCorruptBody(context, options);
                return;

            case StubMode.Normal:
            default:
                await _next(context);
                return;
        }
    }

    private async Task InterceptAndCorruptBody(HttpContext context, StubBehaviorOptions options)
    {
        var originalBody = context.Response.Body;
        await using var memory = new MemoryStream();
        context.Response.Body = memory;

        try
        {
            await _next(context);

            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                var contentType = context.Response.ContentType ?? string.Empty;
                var bodyBytes = memory.ToArray();

                var (outBody, outContentType) = _corruptor.CorruptIfNeeded(
                    bodyBytes,
                    contentType,
                    shouldCorruptAll: () => options.Mode == StubMode.BadResultsAll,
                    someSettings: () => (options.Mode == StubMode.BadResultsSome, options.BadItemsPercentage, options.MaxCorruptionsPerArray),
                    targetProperties: options.TargetProperties,
                    produceMalformedJson: options.ProduceMalformedJson
                );

                context.Response.ContentType = outContentType;
                context.Response.ContentLength = outBody.Length;
                await originalBody.WriteAsync(outBody.AsMemory(0, outBody.Length), context.RequestAborted);
            }
            else
            {
                memory.Position = 0;
                await memory.CopyToAsync(originalBody, context.RequestAborted);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
