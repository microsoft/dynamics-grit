# Grammar Import Tool (GrIT)

GrIT is a tool for converting GRXML (Grammar XML) files to YAML format compatible with Microsoft Copilot Studio. It helps streamline the migration process from traditional voice recognition grammar files to modern conversational AI platforms.

## Overview

The Grammar Import Tool (GrIT) provides a web service with REST API endpoints and SignalR WebSocket interfaces for converting GRXML files. It leverages Azure OpenAI to intelligently convert grammar rules into entities that can be used in Microsoft Copilot Studio.

### Key Features

- Convert individual GRXML files to YAML format
- Process ZIP archives containing multiple GRXML files
- Stream conversion results in real-time via SignalR
- RESTful API endpoints for easy integration
- Support for both synchronous and asynchronous processing

## Architecture

The repository consists of several components:

- **ApiService**: Main service that handles GRXML conversion
- **Stubs**: Stub service for testing/development
- **Tests.L0**: Unit tests (L0 tests)
- **Tests.L1**: Integration tests (L1 tests)
- **Web**: Web interface for demonstration and testing

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (recommended) or any other IDE supporting .NET development
- Azure OpenAI service access (for the conversion functionality)

## Getting Started

### Configuration

1. Clone the repository
   ```bash
   git clone https://github.com/yourusername/grammar-import-tool.git
   cd grammar-import-tool
   ```

2. Configure Azure OpenAI settings in `appsettings.json` or via environment variables:
   ```json
   {
     "GptChat": {
       "Grxml": {
         "AzureOpenAIEndpoint": "https://your-endpoint.openai.azure.com/",
         "AzureOpenAIDeploymentName": "your-deployment-name",
         "AzureOpenAIKey": "your-api-key"
       }
     }
   }
   ```

### Building the Project

```bash
dotnet build CRM.CCaaS.IVR.GRammarImportTool.sln
```

### Running the Service

```bash
dotnet run --project CRM.CCaaS.IVR.GRammarImportTool.ApiService
```

By default, the service will be available at:
- HTTP: http://localhost:5000
- HTTPS: https://localhost:5001

## API Usage

### REST Endpoints

#### 1. Convert GRXML File

```http
POST /grit/grxml
Content-Type: multipart/form-data

[Form Data]
file: <GRXML file>
```

#### 2. Convert ZIP containing GRXML Files

```http
POST /grit/zip
Content-Type: multipart/form-data

[Form Data]
file: <ZIP file containing GRXML files>
```

#### 3. Health Check

```http
GET /health
```

#### 4. Liveness Probe

```http
GET /alive
```

### SignalR WebSocket Integration

An example SignalR client implementation is available at `CRM.CCaaS.IVR.GRammarImportTool.ApiService\Docs\websocketClient\`. This client demonstrates how to connect to the GrITHub server, send files for processing, and handle real-time updates.

Connect to the SignalR hub at `/grithub` to receive real-time updates during file processing:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://your-api-base-url/grithub")
    .Build();

// Handle connection ID
connection.On<string>("ConnectionId", connectionId => {
    Console.WriteLine($"Connected with ID: {connectionId}");
});

// Handle progress updates
connection.On<int>("Progress", progress => {
    Console.WriteLine($"Progress: {progress}%");
});

// Handle completion
connection.On("Completed", () => {
    Console.WriteLine("Processing completed");
});

// Handle errors
connection.On<string>("Error", error => {
    Console.WriteLine($"Error: {error}");
});

// Start connection
await connection.StartAsync();

// Upload a file
byte[] fileBytes = File.ReadAllBytes("path/to/your/file.grxml");
await connection.InvokeAsync("GrxmlConvert", fileBytes);
```

For more detailed examples, see the [SignalR client example](CRM.CCaaS.IVR.GRammarImportTool.ApiService/Docs/websocketClient/README.md) included in the repository.

## Fine-tuning

Fine-tuning adapts a base LLM to your domain by training it on task-specific examples. It can improve consistency, enforce output formats, and reduce prompt complexity for repeated tasks (for example, converting certain GRXML patterns to standardized YAML entities).

- Dataset: A curated JSONL dataset is provided at:
  CRM.CCaaS.IVR.GRammarImportTool/CRM.CCaaS.IVR.GRammarImportTool.ApiService/Docs/finetuning/fine-tune-PS-data.jsonl
- How to use: Create a fine-tuning job in your Azure OpenAI resource using this dataset and a supported base model, wait for training to complete, then deploy the resulting fine-tuned model and update GptChat:Grxml:AzureOpenAIDeploymentName to the new deployment.
- Notes:
  - Evaluate the fine-tuned model against your validation GRXML files before adopting it in production.
  - Keep datasets clean, representative, and consistent to avoid overfitting or regressions.
  - Fine-tuning incurs extra cost and latency during training.
  - However, it is not necessary to do fine-tuning when using the gpt-5 model.

## Running Tests

### Unit Tests (L0)

```bash
dotnet test --filter FullyQualifiedName~Tests.L0
```

### Integration Tests (L1)

```bash
dotnet test --filter FullyQualifiedName~Tests.L1
```

### Generate Code Coverage Report

```bash
dotnet test --verbosity detailed --logger "trx" --collect:"XPlat Code Coverage" --settings Test.runsettings --results-directory "./TestResults" -c Release -p:Platform="Any CPU" /p:CoverletOutputFormat=cobertura /p:PublishTestResults=false ./CRM.CCaaS.IVR.GRammarImportTool.sln
```

Code coverage reports will be generated in the `TestResults` directory.

## Contributing

Contributions are welcome! Here's how you can contribute:

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Commit your changes: `git commit -am 'Add some feature'`
4. Push to the branch: `git push origin feature/my-feature`
5. Submit a pull request

### Coding Guidelines

- Follow C# coding conventions
- Write unit tests for new functionality
- Maintain code coverage (target is 80% for new files)
- Use meaningful commit messages

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgments

- This project uses Azure OpenAI for intelligent GRXML conversion
- Built with ASP.NET Core and SignalR for real-time communication

## Trademarks 
This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow [Microsoft’s Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general). Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party’s policies.
