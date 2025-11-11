# System Patterns: Grammar Import Tool (GrIT)

## Overall Architecture

The Grammar Import Tool (GrIT) follows a modern, modular architecture optimized for maintainability, testability, and scalability. The system is structured as a multi-project .NET solution with clear separation of concerns.

```
┌─────────────────────┐      ┌─────────────────────┐
│                     │      │                     │
│    Web Interface    │◄────►│    API Service      │◄────► Azure OpenAI
│                     │      │                     │
└─────────────────────┘      └─────────────────────┘
                                      ▲
                                      │
                                      ▼
                             ┌─────────────────────┐
                             │      Stubs          │
                             │  (For Testing)      │
                             └─────────────────────┘
```

### Key Architectural Decisions

1. **API-First Design**: All functionality is exposed through well-defined API endpoints
2. **Real-Time Processing**: SignalR for asynchronous, real-time communication
3. **Separation of Concerns**: Distinct projects for different responsibilities
4. **Testability**: Stub services and layered testing approach (L0, L1)
5. **Domain-Driven Design**: Organization around business domain concepts
6. **Dependency Injection**: Used throughout for loose coupling and testability
7. **Configuration Management**: Externalized configuration for different environments

## Component Relationships

### Primary Components

- **ApiService**: Core service containing the conversion logic, API endpoints, and SignalR hubs
- **Stubs**: Mock implementations of external dependencies for testing
- **Tests.L0**: Unit tests focusing on individual components in isolation
- **Tests.L1**: Integration tests verifying end-to-end functionality
- **Web**: Web interface for demonstration and manual testing

### Data Flow

1. **GRXML Processing Flow**:
   ```
   Client → API Controller → Domain Services → OpenAI Service → YAML Generator → Client
   ```

2. **Real-Time Processing Flow**:
   ```
   Client → SignalR Hub → Background Job → Progress Updates → SignalR Hub → Client
                                 ↓
                          Domain Services → OpenAI Service → YAML Generator
   ```

3. **ZIP Processing Flow**:
   ```
   Client → API Controller → ZIP Extractor → Multiple GRXML Processors → ZIP Generator → Client
   ```

## Design Patterns

### Structural Patterns

#### Dependency Injection
- Used throughout the application via ASP.NET Core's built-in DI container
- Services are registered in `Program.cs` or via extension methods
- Enables loose coupling and facilitates testing

#### Repository Pattern
- Used for data access and external service interactions
- Abstracts the details of data retrieval and storage
- Example: `GrxmlRepository` for handling GRXML file operations

#### Facade Pattern
- Simplified interfaces to complex subsystems
- Example: `ConversionService` providing a simple API over complex conversion logic

### Behavioral Patterns

#### Observer Pattern
- Implemented via SignalR for real-time progress updates
- Clients subscribe to conversion progress and receive notifications
- Enables asynchronous processing with feedback

#### Strategy Pattern
- Different conversion strategies based on file type or complexity
- Allows for specialized handling of different grammar patterns
- Example: `IConversionStrategy` implementations for different grammar types

#### Command Pattern
- Encapsulates conversion requests as objects
- Allows for queuing, logging, and tracking of conversion operations
- Example: `ConversionCommand` objects processed by a command handler

### Creational Patterns

#### Factory Method
- Creates appropriate converters or processors based on file type
- Centralizes instantiation logic
- Example: `ConverterFactory` creating the appropriate converter for a file

#### Builder Pattern
- Used for constructing complex YAML output structures
- Separates construction from representation
- Example: `YamlEntityBuilder` for building Copilot Studio entities

## Module Organization

### ApiService Structure

```
ApiService/
├── ApiDefinitions/    # OpenAPI specifications
├── Controllers/       # REST API controllers
├── Domain/            # Business logic and domain models
│   ├── Models/        # Domain entities
│   ├── Services/      # Business logic services
│   └── Exceptions/    # Domain-specific exceptions
├── Endpoints/         # Minimal API endpoints
├── Hubs/              # SignalR hubs for real-time communication
├── Infrastructure/    # External service integrations
│   ├── OpenAI/        # Azure OpenAI integration
│   └── Storage/       # File storage operations
├── Main/              # Application startup and configuration
└── Util/              # Utility classes and helpers
```

### Dependency Structure

- **Controllers/Endpoints/Hubs**: Depend on Domain services
- **Domain Services**: Contain core business logic, depend on Infrastructure
- **Infrastructure**: Implements external service integrations
- **Util**: Contains helpers used across the application

## Error Handling and Logging

### Error Handling Strategy

1. **Domain Exceptions**: Custom exceptions for business logic errors
2. **Global Exception Handling**: Middleware for consistent API error responses
3. **Graceful Degradation**: Fallback strategies when services are unavailable
4. **Validation**: Input validation before processing to prevent errors

### Logging Patterns

1. **Structured Logging**: Using semantic logging with contextual properties
2. **Correlation IDs**: Tracking requests across components
3. **Performance Metrics**: Timing for critical operations
4. **External Service Logging**: Detailed logs for Azure OpenAI interactions

## Security Patterns

### Authentication and Authorization

- API endpoints can be secured with standard authentication mechanisms
- Minimal attack surface with clearly defined entry points

### Input Validation

- All file inputs are validated before processing
- Size limits and content type verification

### Secure Configuration

- Sensitive configuration (API keys) stored in Azure Key Vault or user secrets
- Different configuration profiles for development, testing, and production

## API Design Patterns

### RESTful Endpoints

- Resource-oriented design
- HTTP verbs used appropriately (POST for conversions)
- Consistent response formats

### Real-Time Communication

- SignalR hub for bidirectional communication
- Event-based messaging for progress updates
- Connection management with reconnection logic

### Documentation

- OpenAPI (Swagger) for REST API documentation
- XML comments for code documentation
- Example clients for demonstration

## Critical Implementation Paths

### Core Conversion Pipeline

1. File reception and validation
2. GRXML parsing and preprocessing
3. Azure OpenAI prompting and processing
4. YAML entity generation
5. Response formatting and delivery

### ZIP Archive Processing

1. Archive extraction and validation
2. Parallel processing of contained files
3. Progress tracking across multiple conversions
4. Result compilation into a new archive
5. Cleanup of temporary files

### SignalR Real-Time Updates

1. Client connection establishment
2. Background job initialization
3. Progress reporting from long-running processes
4. Connection management and error handling
5. Result delivery upon completion