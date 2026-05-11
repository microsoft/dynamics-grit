// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Middleware;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Middleware;

[Collection("BaseTestCollection")]
public class ExceptionMiddlewareTest
{
    private readonly BaseTest _baseTest;

    public ExceptionMiddlewareTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    [Fact]
    public async Task When_NoException_Then_PassesThrough()
    {
        var middleware = new ExceptionMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length); // no body written
        Assert.DoesNotContain(_baseTest.LogProvider.Logger.LoggedMessages,
            m => m.Contains("An unhandled exception occurred.", StringComparison.OrdinalIgnoreCase));
    }

    public static TheoryData<Exception, int> ExceptionMap() =>
        new()
        {
            { new UnauthorizedAccessException(), StatusCodes.Status403Forbidden },
            { new ArgumentException("bad arg"), StatusCodes.Status400BadRequest },
            { new FileNotFoundException("missing"), StatusCodes.Status404NotFound },
            { new DirectoryNotFoundException("missing dir"), StatusCodes.Status404NotFound },
            { new IOException("io fail"), StatusCodes.Status503ServiceUnavailable },
            { new InvalidOperationException("other"), StatusCodes.Status500InternalServerError },
        };

    [Theory]
    // xUnit1045: Exception is not natively serializable; Test Explorer cannot
    // enumerate individual rows. Acceptable trade-off — the tests run correctly
    // and a single combined row is good enough for this CI-only scenario.
#pragma warning disable xUnit1045
    [MemberData(nameof(ExceptionMap))]
#pragma warning restore xUnit1045
    public async Task When_ExceptionThrown_Then_MappedStatusAndJsonBody(Exception thrown, int expectedStatus)
    {
        var middleware = new ExceptionMiddleware(_ => Task.FromException(thrown));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal(expectedStatus, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));

        var doc = JsonDocument.Parse(body);
        Assert.Equal(expectedStatus, doc.RootElement.GetProperty("StatusCode").GetInt32());
        Assert.Equal("Unexpected error occured", doc.RootElement.GetProperty("Message").GetString());

        Assert.Contains(_baseTest.LogProvider.Logger.LoggedMessages,
            m => m.Contains("An unhandled exception occurred.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_NullContext_Then_ArgumentNullException()
    {
        var middleware = new ExceptionMiddleware(_ => Task.CompletedTask);
        await Assert.ThrowsAsync<ArgumentNullException>(() => middleware.InvokeAsync(null!));
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}
