using Microsoft.AspNetCore.Antiforgery;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAntiforgery();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


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
    var fileContent = await FileReader.ReadFileAsTextAsync(file);
    var entityType = await ChatGPTPrompter.GetFileEntityTypeAsync(fileContent);

    return Results.Ok(entityType);
})
.WithName("Grit");


app.MapDefaultEndpoints();

app.Run();