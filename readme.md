# FoundryLocalChatApp

A modern Blazor-based AI Chat application that enables users to chat with a local LLM while querying custom PDF documents using semantic search. Built on .NET 9 and leveraging Microsoft.AI.Foundry.Local for local model management, this application provides a fully local RAG (Retrieval-Augmented Generation) solution.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
- [Hugging Face Models Setup](#hugging-face-models-setup)
- [Building the Project](#building-the-project)
- [Critical Components](#critical-components)
- [Configuration](#configuration)
- [Usage](#usage)
- [Dependencies](#dependencies)
- [Contributing](#contributing)
- [License](#license)
- [Troubleshooting](#troubleshooting)

## Overview

FoundryLocalChatApp is a complete RAG (Retrieval-Augmented Generation) application that allows you to:
- Chat with a local LLM (Qwen 2.5-1.5b-instruct)
- Query custom PDF documents using semantic search
- Run everything locally without external API calls
- Use voice input/output for hands-free interaction
- Get AI responses with citations to source documents

The application uses a vector database (Qdrant) for semantic search, ONNX Runtime for embedding generation, and Microsoft.AI.Foundry.Local for managing local language models.

## Features

- **Blazor-based Interactive Chat UI** - Modern, responsive web interface with real-time streaming responses
- **Local AI Model Support** - Uses Microsoft.AI.Foundry.Local to run models entirely on your machine
- **Semantic Search** - Query custom PDF documents using BGE embeddings and Qdrant vector database
- **Speech Recognition & Synthesis** - Voice input/output using Blazor.SpeechRecognition and Blazor.SpeechSynthesis
- **PDF Document Ingestion** - Automatic processing and indexing of PDF files
- **Tool-based Function Calling** - LLM can invoke semantic search to find relevant document chunks
- **Real-time Citations** - Responses include citations with filename, page number, and quoted text
- **Telemetry & Resilience** - OpenTelemetry integration for monitoring and diagnostics

## Architecture

### Solution Structure

The solution is organized into three main projects:

- **FoundryLocalChatApp.Web** - Main Blazor Server application containing:
  - Chat UI components (`Components/Pages/Chat/`)
  - Service layer (`Services/`)
  - PDF ingestion pipeline (`Services/Ingestion/`)
  - Semantic search implementation
  
- **FoundryLocalChatApp.AppHost** - .NET Aspire host that orchestrates:
  - Qdrant vector database container
  - Web application
  - Service discovery and configuration

- **FoundryLocalChatApp.ServiceDefaults** - Shared configuration for:
  - Service defaults and resilience patterns
  - OpenTelemetry setup
  - HTTP client configuration

### How It Works

```
User Input (Chat.razor)
    ↓
User message added to conversation
    ↓
ChatClient receives streaming request with SystemPrompt
    ↓
LLM (Qwen 2.5-1.5b) processes message with SearchAsync tool available
    ↓
[If tool called] → AI invokes semantic search to find relevant PDF chunks
    ↓
Search generates query embedding → Qdrant finds similar chunks (cosine similarity)
    ↓
Chunks returned with citations (filename, page number, quoted text)
    ↓
LLM incorporates search results into final response
    ↓
Response streamed back and rendered in UI with citations
```

### Data Pipeline

**PDF Ingestion Flow:**
1. Watch `wwwroot/Data` directory for new/modified PDFs
2. Extract text using PdfPig with layout analysis
3. Split into 200-character chunks using TextChunker
4. Generate 1024-dimensional embeddings with BGE-Large model
5. Store chunks in Qdrant with document metadata (filename, page, text)
6. Track document versions to skip unchanged files

**Retrieval Flow:**
1. User query → embedding generated
2. Top 10 similar chunks found via cosine similarity in Qdrant
3. Results filtered by filename if specified
4. Formatted as XML results for LLM consumption
5. LLM uses results to generate response with citations

## Getting Started

### Prerequisites

**Required:**
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (9.0 or later)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for Qdrant vector database)
  - **Important**: If using Ollama, requires Docker Desktop 4.41.1 or later (see [known issues](https://github.com/ollama/ollama/issues/7538))

**Recommended:**
- Visual Studio 2022 (17.14 or later) with .NET Aspire workload, OR
- Visual Studio Code with C# Dev Kit extension

### Quick Start

1. **Clone the repository:**
   ```bash
   git clone https://github.com/chuckbeasley/FoundryLocalChatApp.git
   cd FoundryLocalChatApp
   ```

2. **Ensure Docker is running:**
   ```bash
   docker --version
   # Docker should be running for Qdrant
   ```

3. **Open and run the solution:**

   **Visual Studio:**
   - Open `FoundryLocalChatApp.sln`
   - Press `Ctrl+F5` or `F5` to run
   - The Aspire dashboard will open automatically

   **VS Code:**
   - Open the `FoundryLocalChatApp/FoundryLocalChatApp.AppHost` folder
   - Open `Program.cs` in the AppHost project
   - Use C# Dev Kit's run button or debug launcher

4. **First run setup:**
   - The application will automatically download the Qwen 2.5-1.5b model (~1GB) via Foundry Local Manager
   - Qdrant container will be pulled and started
   - Navigate to the web app URL shown in the Aspire dashboard

5. **Add PDF documents (optional):**
   - Place PDF files in `FoundryLocalChatApp.Web/wwwroot/Data/`
   - The application will automatically process and index them

## Hugging Face Models Setup

The application requires the **BGE-Large-En-V1.5** embedding model from Hugging Face in ONNX format. This model generates 1024-dimensional embeddings for semantic search.

### Required Model Files

You need to download the following files from Hugging Face:

**Model:** [BAAI/bge-large-en-v1.5](https://huggingface.co/BAAI/bge-large-en-v1.5)

**Required files:**
1. `model.onnx` - The ONNX-format embedding model
2. `vocab.txt` - The vocabulary file for tokenization

### Download Instructions

#### Option 1: Manual Download

1. Go to [https://huggingface.co/BAAI/bge-large-en-v1.5](https://huggingface.co/BAAI/bge-large-en-v1.5)

2. Download the ONNX model:
   - Navigate to the Files and versions tab
   - Download `onnx/model.onnx` (~1.4 GB)
   
3. Download the vocabulary:
   - Download `vocab.txt` (~226 KB)

4. Create the following directory structure in your project:
   ```
   FoundryLocalChatApp.Web/
   └── wwwroot/
       └── Models/
           └── bge-large-en-v1.5/
               ├── onnx/
               │   └── model.onnx
               └── vocab.txt
   ```

5. Place the downloaded files:
   - Put `model.onnx` in `wwwroot/Models/bge-large-en-v1.5/onnx/`
   - Put `vocab.txt` in `wwwroot/Models/bge-large-en-v1.5/`

#### Option 2: Using Hugging Face CLI

If you have the Hugging Face CLI installed:

```bash
# Install Hugging Face CLI (if not already installed)
pip install huggingface-hub[cli]

# Navigate to the Web project
cd FoundryLocalChatApp/FoundryLocalChatApp.Web/wwwroot/Models

# Download the model
huggingface-cli download BAAI/bge-large-en-v1.5 \
  --include "onnx/model.onnx" "vocab.txt" \
  --local-dir bge-large-en-v1.5 \
  --local-dir-use-symlinks False
```

### Model Details

- **Model Name:** BGE-Large-En-V1.5
- **Model Type:** Sentence Embedding Model (BERT-based)
- **Embedding Dimension:** 1024
- **Distance Metric:** Cosine Similarity
- **Use Case:** Semantic search and document retrieval
- **License:** MIT License
- **Paper:** [C-Pack: Packaged Resources To Advance General Chinese Embedding](https://arxiv.org/abs/2309.07597)

### Chat Model (Automatic Download)

The application also uses **Qwen 2.5-1.5b-instruct-generic-cpu** as the chat model, which is automatically downloaded and managed by Microsoft.AI.Foundry.Local on first run. No manual setup is required for this model.

- **Model:** Qwen 2.5-1.5b-instruct
- **Size:** ~1 GB
- **Download:** Automatic via Foundry Local Manager
- **Configuration:** Specified in `Program.cs` as `qwen2.5-1.5b-instruct-generic-cpu:4`

## Building the Project

### Build from Command Line

```bash
# Navigate to solution directory
cd FoundryLocalChatApp

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the AppHost (which starts all services)
cd FoundryLocalChatApp/FoundryLocalChatApp.AppHost
dotnet run
```

### Build in Visual Studio

1. Open `FoundryLocalChatApp.sln`
2. Select `Build > Build Solution` or press `Ctrl+Shift+B`
3. Set `FoundryLocalChatApp.AppHost` as the startup project
4. Press `F5` to build and run

### Build Configuration

- **Target Framework:** .NET 9.0
- **Language Features:** C# 13 with nullable reference types enabled
- **Build Output:** The ONNX model files (`model.onnx` and `vocab.txt`) are automatically copied to the output directory

## Critical Components

### 1. SemanticSearch Service (`Services/SemanticSearch.cs`)

**Purpose:** Generates embeddings and performs vector similarity search on ingested documents.

**Key Methods:**
- `SearchAsync(string query, string? fileName)` - Searches for relevant document chunks
  - Generates query embedding using BGE-Large model
  - Searches Qdrant vector database
  - Returns top 10 results with cosine similarity scoring
  - Filters by filename if provided

**Technology:**
- ONNX Runtime for embedding generation
- Qdrant for vector storage and similarity search
- 1024-dimensional embeddings

### 2. DataIngestor Service (`Services/Ingestion/DataIngestor.cs`)

**Purpose:** Processes PDF files, chunks text, generates embeddings, and stores in Qdrant.

**Key Responsibilities:**
- Watch for new/modified PDF files
- Extract text using PdfPig
- Chunk text into 200-character segments
- Generate embeddings for each chunk
- Store in Qdrant with metadata
- Track document versions

**Configuration:**
- Watches: `wwwroot/Data/` directory
- Chunk size: 200 characters
- Overlap: Configured via TextChunker

### 3. PDFDirectorySource (`Services/Ingestion/PDFDirectorySource.cs`)

**Purpose:** Extracts text from PDF files with layout analysis.

**Key Features:**
- Uses PdfPig for PDF parsing
- Maintains page-level granularity
- Tracks document versions (hash-based)
- Provides document metadata (filename, page numbers, text)

### 4. OpenAIChatClientAdapter (`Services/OpenAIChatClientAdapter.cs`)

**Purpose:** Manages OpenAI API communication and chat streaming.

**Key Features:**
- Streaming chat responses
- Tool/function calling support
- Integration with Microsoft.Extensions.AI
- Connection to local Foundry model

### 5. Chat Component (`Components/Pages/Chat/`)

**Purpose:** Blazor UI for chat interaction.

**Key Features:**
- Real-time message streaming
- Citation rendering
- Speech-to-text input
- Text-to-speech output
- Message history management

### 6. System Prompt

The application uses a carefully crafted system prompt that instructs the LLM to:
- Use the `SearchAsync` tool to query document knowledge
- Provide citations with filename, page number, and quoted text
- Format responses with proper markdown
- Be helpful and concise

## Configuration

### Application Settings

**Location:** `FoundryLocalChatApp.Web/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Aspire": {
    "OpenAI": {
      "Key": "none",
      "Endpoint": "http://localhost:5273/v1",
      "Deployment": "Phi-4-mini-instruct-generic-cpu"
    }
  }
}
```

**Key Settings:**
- `Aspire.OpenAI.Endpoint` - Foundry Local Manager endpoint (auto-configured)
- `Aspire.OpenAI.Key` - Set to "none" for local models
- `Aspire.OpenAI.Deployment` - Model identifier (configured in Program.cs)

### User Secrets

User secrets can be used for sensitive configuration:

```bash
# Set user secrets
dotnet user-secrets set "Aspire:OpenAI:Key" "your-key-here" --project FoundryLocalChatApp.Web
```

### Environment Variables

Configuration can also be provided via environment variables:

```bash
export Aspire__OpenAI__Endpoint="http://localhost:5273/v1"
export Aspire__OpenAI__Deployment="qwen2.5-1.5b-instruct-generic-cpu"
```

## Usage

### Basic Chat

1. Navigate to the application URL (shown in Aspire dashboard)
2. Type your message in the chat input
3. Press Enter or click Send
4. View the streaming response

### Querying Documents

1. Add PDF files to `FoundryLocalChatApp.Web/wwwroot/Data/`
2. Wait for ingestion to complete (check logs)
3. Ask questions about your documents:
   ```
   "What does the document say about X?"
   "Search for information about Y"
   ```
4. The AI will use semantic search and provide citations

### Voice Input/Output

- Click the microphone button to use speech-to-text
- Click the speaker button to hear responses read aloud
- Voice features use browser's built-in speech APIs

## Dependencies

### Core AI/ML Packages

- `Microsoft.AI.Foundry.Local` (0.3.0) - Local model management and execution
- `Microsoft.SemanticKernel` (1.69.0) - AI orchestration framework
- `Microsoft.SemanticKernel.Connectors.Qdrant` (1.69.0-preview) - Qdrant integration
- `Microsoft.SemanticKernel.Connectors.Onnx` (1.69.0-alpha) - ONNX embeddings
- `Microsoft.ML.OnnxRuntime.Foundry` (1.23.2) - ONNX Runtime for inference
- `OpenAI` (2.8.0) - OpenAI API client
- `Azure.AI.OpenAI` (2.8.0-beta.1) - Azure OpenAI integration
- `Microsoft.Extensions.AI` (10.2.0) - Standard AI abstractions
- `Microsoft.Extensions.AI.OpenAI` (10.2.0-preview) - AI extensions for OpenAI

### Data & Vector Store

- `Aspire.Qdrant.Client` (13.1.0) - Qdrant vector database client
- `PdfPig` (0.1.14-alpha) - PDF text extraction
- `System.Linq.Async` (7.0.0) - Async LINQ operations

### UI & Interaction

- `Blazor.SpeechRecognition` (9.0.1) - Speech-to-text
- `Blazor.SpeechSynthesis` (9.0.1) - Text-to-speech

### Aspire & Hosting

- `Aspire.Azure.AI.OpenAI` (13.1.0-preview) - Azure OpenAI Aspire integration
- `Aspire.Hosting.AppHost` (13.1.0) - Aspire application host
- `Aspire.Hosting.Qdrant` (13.1.0) - Qdrant container hosting
- `CommunityToolkit.Aspire.Hosting.Ollama` (13.1.2-beta) - Ollama integration

### Observability

- `OpenTelemetry.Exporter.OpenTelemetryProtocol` (1.14.0)
- `OpenTelemetry.Extensions.Hosting` (1.14.0)
- `OpenTelemetry.Instrumentation.AspNetCore` (1.14.0)
- `OpenTelemetry.Instrumentation.Http` (1.14.0)
- `OpenTelemetry.Instrumentation.Runtime` (1.14.0)

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

---

## Troubleshooting

**Docker/Ollama Issues:**
- Ensure Docker Desktop is version 4.41.1 or later
- Check Docker is running: `docker --version`

**Model Download Issues:**
- Foundry Local Manager downloads models on first run
- Check disk space (~2GB required for models)
- Check internet connectivity for initial download

**Qdrant Connection Issues:**
- Ensure Docker is running
- Check Aspire dashboard for Qdrant container status
- Verify port 6333 is not in use

**BGE Model Not Found:**
- Verify model files are in `wwwroot/Models/bge-large-en-v1.5/`
- Check file paths match exactly (case-sensitive)
- Ensure `model.onnx` is in `onnx/` subdirectory

---

*Built with Blazor, .NET 9, and Microsoft AI Stack*