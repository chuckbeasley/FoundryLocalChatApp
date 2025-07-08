# FoundryLocalChatApp

A modern Blazor web application targeting .NET 9, designed for local chat experiences with AI integration and advanced features.

## Solution Structure

- **FoundryLocalChatApp.Web**: Blazor WebAssembly front-end.
- **FoundryLocalChatApp.AppHost**: Application host using Aspire for orchestration.
- **FoundryLocalChatApp.ServiceDefaults**: Shared service configuration and telemetry.

## Features

- Blazor-based interactive chat UI.
- AI integration via Azure OpenAI and Microsoft Semantic Kernel.
- Local AI model support Foundry Local).
- Speech recognition (Blazor.SpeechRecognition).
- PDF parsing (PdfPig).
- Telemetry and resilience via OpenTelemetry and Microsoft.Extensions.

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Node.js (for front-end build, if modifying JS)

## Configuration

- User secrets are used for sensitive settings (see `UserSecretsId` in project files).
- AI and service endpoints can be configured in `appsettings.json` or via environment variables.

## Dependencies

Key NuGet packages:
- `Aspire.Azure.AI.OpenAI`
- `Microsoft.Extensions.AI.OpenAI`
- `Microsoft.SemanticKernel.Core`
- `PdfPig`
- `Blazor.SpeechRecognition`
- `OpenTelemetry.*`
- `Microsoft.AI.Foundry.Local`
- `Aspire.Hosting.*`
- `CommunityToolkit.Aspire.Hosting.Ollama`

## Contributing

1. Fork the repository.
2. Create a feature branch.
3. Commit your changes.
4. Open a pull request.

## License

This project is licensed under the MIT License.

---

*Built with Blazor and .NET 9.*