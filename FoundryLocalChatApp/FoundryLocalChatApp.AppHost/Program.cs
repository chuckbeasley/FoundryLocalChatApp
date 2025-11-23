var builder = DistributedApplication.CreateBuilder(args);
var vectorDB = builder.AddQdrant("vectordb")
    .WithDataVolume();
var webApp = builder.AddProject<Projects.FoundryLocalChatApp_Web>("aichatweb-app");
webApp
    .WithReference(vectorDB)
    .WaitFor(vectorDB);
builder.Build().Run();
