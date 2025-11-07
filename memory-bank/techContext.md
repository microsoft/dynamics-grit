# Technical Context: Grammar Import Tool (GrIT)

## Technology Stack

### Core Technologies
- **.NET 8.0**: Primary development platform
- **ASP.NET Core**: Web framework for APIs and services
- **C#**: Primary programming language
- **Azure OpenAI**: AI service for intelligent grammar processing
- **SignalR**: Real-time communication framework
- **YamlDotNet**: YAML parsing and generation library

### External Dependencies
- **Azure.AI.OpenAI**: Client library for Azure OpenAI service
- **Microsoft.AspNetCore.OpenApi**: OpenAPI support for API documentation
- **Azure.Identity**: Authentication for Azure services
- **Microsoft.AspNetCore.SignalR.Client**: Client library for SignalR connections
- **Microsoft.Extensions.AI.OpenAI**: Extensions for OpenAI integration
- **Swashbuckle.AspNetCore**: Swagger integration for API documentation
- **YamlDotNet**: YAML serialization and deserialization

## Development Environment

### Required Tools
- **.NET 8.0 SDK**: Required for building and running the application
- **Visual Studio 2022** or equivalent IDE: Recommended for development
- **Azure OpenAI Service Access**: Required for the conversion functionality
- **Git**: Version control system

### Development Setup
1. Clone the repository
2. Configure Azure OpenAI settings in `appsettings.json` or environment variables
3. Build the solution: `dotnet build CRM.CCaaS.IVR.GRammarImportTool.sln`
4. Run the service: `dotnet run --project CRM.CCaaS.IVR.GRammarImportTool.ApiService`

### Configuration
The application requires configuration for Azure OpenAI service:
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

These settings can be provided in `appsettings.json`, environment variables, or Azure Key Vault.

## Technical Constraints

### Performance Considerations
- Grammar files can be large and complex, requiring efficient processing
- Batch processing of multiple files must be optimized
- Real-time progress reporting adds communication overhead

### Security Constraints
- Azure OpenAI API keys must be securely stored
- Input files must be validated to prevent security vulnerabilities
- API endpoints should implement appropriate authentication

### Scalability Requirements
- The service should handle multiple concurrent conversion requests
- Large ZIP archives may contain hundreds of GRXML files
- SignalR connections must scale to support multiple clients

## Dependencies and Integrations

### External Services
- **Azure OpenAI**: Primary dependency for intelligent grammar conversion
- **Azure Key Vault** (optional): For secure storage of configuration
- **Application Insights** (optional): For monitoring and telemetry

### Integration Patterns
- **REST API**: Synchronous HTTP endpoints for direct integration
- **SignalR WebSockets**: Asynchronous communication for real-time updates
- **File-based Integration**: Input/output via GRXML and YAML files

## Build and Deployment

### Build Process
```bash
# Build the solution
dotnet build CRM.CCaaS.IVR.GRammarImportTool.sln

# Run unit tests
dotnet test --filter FullyQualifiedName~Tests.L0

# Run integration tests
dotnet test --filter FullyQualifiedName~Tests.L1
```

### Deployment Options
- **Docker Container**: The service includes Dockerfile configuration
- **Azure App Service**: Recommended for production deployment
- **Local Development**: Runs on `http://localhost:5000` and `https://localhost:5001`

## Testing Approach

### Test Categories
- **L0 Tests**: Unit tests for individual components and methods
- **L1 Tests**: Integration tests for end-to-end functionality
- **Manual Testing**: For UI and complex scenarios

### Testing Tools
- **xUnit**: Test framework
- **Moq**: Mocking framework for unit tests
- **coverlet**: Code coverage tool

### Code Coverage Goals
- Target is 80% code coverage for new files
- Generated using:
```bash
dotnet test --collect:"XPlat Code Coverage" --settings Test.runsettings --results-directory "./TestResults"
```

## Performance Considerations

### Optimizations
- Parallel processing for batch conversions
- Caching of similar grammar patterns
- Streaming response for large files
- Fine-tuning for domain-specific grammar patterns

### Monitoring
- Conversion time metrics
- Error rates by grammar complexity
- Azure OpenAI token usage
- Processing queue length

## Security Model

### Data Protection
- No persistent storage of user files after processing
- Temporary files cleaned up after conversion
- No sharing of grammar data between users

### Authentication
- API endpoints can be secured with standard authentication mechanisms
- SignalR connections use connection IDs for isolation

## Deployment Architecture

### Components
- **ApiService**: Main conversion service with REST and SignalR endpoints
- **Web**: Optional web interface for demonstration and testing
- **Stubs**: Mock services for development and testing

### Service Communication
- REST API for synchronous operations
- SignalR for asynchronous and real-time operations
- File system for temporary storage during processing