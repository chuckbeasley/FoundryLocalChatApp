using FoundryLocalChatApp.Web.Components;
using FoundryLocalChatApp.Web.Services;
using FoundryLocalChatApp.Web.Services.Ingestion;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

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
await model.LoadAsync();
OpenAIChatClient chatClient = await model.GetChatClientAsync();
var builder = WebApplication.CreateBuilder(args);

//IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
var onnxModelPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Models", "bge-large-en-v1.5", "onnx", "model.onnx");
var onnxVocabPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Models", "bge-large-en-v1.5", "vocab.txt");
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
builder.Services.AddBertOnnxEmbeddingGenerator(onnxModelPath, onnxVocabPath);

// register the adapter that implements IChatClient
builder.Services.AddSingleton<IChatClient>(sp =>
{
    // Base adapter around the Foundry OpenAIChatClient
    using var adapter = new OpenAIChatClientAdapter(chatClient);

    var loggerFactoryForAI = sp.GetService<ILoggerFactory>();
    var functionInvoker = new FunctionInvokingChatClient(adapter, loggerFactoryForAI, sp);
    return functionInvoker;
});
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

// Find the registered embedding generator service
var embeddingGenerator = app.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

await DataIngestor.IngestDataAsync(
    app.Services,
    new PDFDirectorySource(
        Path.Combine(builder.Environment.WebRootPath, "Data"),
        embeddingGenerator));
app.Run();
