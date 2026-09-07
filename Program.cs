using LilyRagPractices.Services;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add this block so logs always show in Output/console
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("UglyToad.PdfPig", LogLevel.Error);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri("http://localhost:11434")
    };

    return new OllamaApiClient(httpClient, "llama3:latest");
});

builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<EmbeddingService>();
// Removed: builder.Services.AddHostedService<DocumentFolderIngestionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseCors("ReactDev");
app.UseHttpsRedirection();
app.MapControllers();

app.Run();