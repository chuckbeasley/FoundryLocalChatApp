using FoundryLocalChatApp.Web.Components;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;

var manager = new FoundryLocalManager();
if (!manager.IsServiceRunning)
{
    await manager.StartServiceAsync();
}

var cachedModels = await manager.ListCachedModelsAsync();
if (!cachedModels.Any(m => m.Alias == "Phi-4-mini-instruct-generic-cpu"))
{
    await manager.DownloadModelAsync("Phi-4-mini-instruct-generic-cpu");
}
await manager.LoadModelAsync("Phi-4-mini-instruct-generic-cpu");

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

builder.AddOpenAIClient(
            connectionName: "foundryLocal",
            configureSettings: options =>
            {
                options.Endpoint = manager.Endpoint;
            })
    .AddChatClient()
    .UseFunctionInvocation()
    .UseOpenTelemetry(configure: c =>
        c.EnableSensitiveData = builder.Environment.IsDevelopment());

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
