using FoundryLocalChatApp.Web.Components;
using FoundryLocalChatApp.Web.Services;
using FoundryLocalChatApp.Web.Services.Ingestion;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using OpenAI;
using System.ClientModel;

var alias = "qwen2.5-1.5b-instruct-generic-cpu:4";
var manager = await FoundryLocalManager.StartModelAsync(aliasOrModelId: alias);

var model = await manager.GetModelInfoAsync(aliasOrModelId: alias);
ApiKeyCredential key = new ApiKeyCredential(manager.ApiKey);
OpenAIClient client = new OpenAIClient(key, new OpenAIClientOptions
{
    Endpoint = manager.Endpoint
});
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
builder.Services.AddChatClient(client.GetChatClient(model?.ModelId).AsIChatClient().AsBuilder().UseFunctionInvocation().Build());
//builder.Services.AddChatClient(chatClient).UseFunctionInvocation();
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
