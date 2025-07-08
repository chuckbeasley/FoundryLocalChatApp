var builder = DistributedApplication.CreateBuilder(args);

var webApp = builder.AddProject<Projects.FoundryLocalChatApp_Web>("aichatweb-app");

builder.Build().Run();
