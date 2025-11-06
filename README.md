# Grammar Import Tool (GrIT)

GrIT is an LLM-powered tool for converting GRXML (Grammar XML) files to YAML format compatible with Microsoft Copilot Studio. It helps streamline the migration process from traditional voice recognition grammar files to modern conversational AI platforms. It sends prompts to an underlying LLM (Azure/OpenAI) to perform the conversion.

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

## Responsible AI Disclaimer

This repository includes AI guardrails and safety mechanisms designed to reduce risks associated with automated decision-making. However, these measures are **not exhaustive**, and **end users remain responsible for ensuring safe and compliant deployment**.

By using this code, you acknowledge that:

- AI systems can produce **unintended, biased, or harmful outputs**.
- Compliance with **laws, regulations, and ethical standards** is your responsibility.
- Additional **testing, monitoring, and human oversight** are strongly recommended before production use.

**This repository is provided "as is" without warranties or guarantees. Use at your own risk.**

---

### Deployment Best Practices

To help mitigate risks when deploying AI systems based on this repository:

1. **Perform Risk Assessment**
   - Identify potential misuse scenarios and failure modes.
   - Evaluate impact on privacy, security, and fairness.

2. **Enable Human Oversight**
   - Keep humans in the loop for critical decisions.
   - Implement escalation paths for unexpected outputs.

3. **Validate and Test**
   - Use representative datasets to test for bias and accuracy.
   - Conduct adversarial testing to uncover vulnerabilities.

4. **Monitor Continuously**
   - Track system performance and user feedback post-deployment.
   - Set up alerts for anomalies or harmful outputs.

5. **Document and Communicate**
   - Maintain clear documentation of model limitations and intended use.
   - Inform stakeholders about risks and mitigation strategies.

6. **Comply with Regulations**
   - Ensure adherence to data protection laws (e.g., GDPR, CCPA).
   - Follow organizational Responsible AI guidelines.

## Acknowledgments

- This project uses Azure OpenAI for intelligent GRXML conversion
- Built with ASP.NET Core and SignalR for real-time communication
