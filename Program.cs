using LilyRagPractices.Services;
using Microsoft.Extensions.AI;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();

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

// Auto-ingest local files from documents folder on startup
builder.Services.AddHostedService<DocumentFolderIngestionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    app.MapGet("/", () => Results.Ok("LilyRagPractices API is running."));
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
