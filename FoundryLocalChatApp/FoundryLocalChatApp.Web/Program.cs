using FoundryLocalChatApp.Web.Components;
using FoundryLocalChatApp.Web.Services;
using FoundryLocalChatApp.Web.Services.Ingestion;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

// Create a logger for the Foundry manager
var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var flLogger = loggerFactory.CreateLogger("FoundryLocalManager");

// Minimal configuration — change paths/URLs as needed for your environment
var config = new Configuration
{
    AppName = "FoundryLocalChatApp", // Fix: set required property
    // example: keep cached models under local app data
    ModelCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".foundry\\cache\\models")
    // If you need the built-in web service, populate config.Web (see package docs)
};
// Create the singleton manager
await FoundryLocalManager.CreateAsync(config, flLogger);

// Get the singleton instance
var manager = FoundryLocalManager.Instance; 
var catalog = await manager.GetCatalogAsync();
var models = await catalog.GetCachedModelsAsync();
Model? model = await catalog.GetModelAsync("phi-4-mini");
if (model is null)
{
    throw new InvalidOperationException("Model 'phi-4-mini' not found in catalog.");
}
var cached = (await catalog.GetCachedModelsAsync())
    .FirstOrDefault(v => v.Id == model.Id || v.Alias == model.Alias);

if (cached is null)
{
    throw new InvalidOperationException(
        $"Model '{model.Id}' is not installed in the cache at '{config.ModelCacheDir}'. " +
        "Install the model (Foundry tooling) or place the model files under the cache directory.");
}

string modelPath = await model.GetPathAsync();  
await model.LoadAsync();
OpenAIChatClient chatClient = await model.GetChatClientAsync();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
builder.AddServiceDefaults();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true;
    });
builder.Services.AddSpeechRecognitionServices();
builder.Services.AddSpeechSynthesisServices();
builder.Services.AddMemoryCache();
// register the adapter that implements IChatClient
builder.Services.AddSingleton<IChatClient>(sp => new OpenAIChatClientAdapter(chatClient));

builder.AddQdrantClient("vectordb");
builder.Services.AddQdrantCollection<Guid, IngestedChunk>("data-blazorchat-chunks");
builder.Services.AddQdrantCollection<Guid, IngestedDocument>("data-blazorchat-documents");
builder.Services.AddScoped<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
