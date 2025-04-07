using Microsoft.AspNetCore.Antiforgery;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery();
builder.Services.AddSingleton<GPTPrompter>();

var app = builder.Build();

app.UseAntiforgery();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

//if (app.Environment.IsDevelopment())
//{
//    app..MapOpenApi();
//}


// Endpoint to get anti-forgery token
app.MapGet("/get-antiforgery-token", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.Append("X-CSRF-TOKEN", tokens.RequestToken);
    return Results.Ok(new { Token = tokens.RequestToken });
})
.WithName("GetAntiforgeryToken");

app.MapPost("/grit", async ([FromForm] IFormFile file) =>
{

    if (file.Length == 0)
    {
        return Results.BadRequest("File is empty.");
    }

    try
    {
        var fileContent = await FileReader.ReadFileAsTextAsync(file);
        var gptPrompter = app.Services.GetRequiredService<GPTPrompter>();
        var entityType = await gptPrompter.GetFileEntityTypeAsync(fileContent);
        return Results.Ok(entityType);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
})
.DisableAntiforgery()
.WithName("Grit");


app.MapDefaultEndpoints();

app.Run();