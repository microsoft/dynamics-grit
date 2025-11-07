# Active Context: Grammar Import Tool (GrIT)

## Current Development Focus

The Grammar Import Tool (GrIT) is currently focusing on the following areas:

1. **Azure OpenAI Integration Refinement**: Optimizing the prompts and processing for more accurate GRXML to YAML conversion
2. **Performance Optimization**: Improving processing speed for large grammar files and ZIP archives
3. **SignalR Real-Time Updates**: Enhancing the WebSocket interface for more reliable progress reporting
4. **Error Handling Improvements**: Adding more robust error handling and recovery mechanisms
5. **Documentation Expansion**: Improving API documentation and usage examples

## Recent Significant Changes

Recent development efforts have included:

1. **Migration to .NET 8.0**: The project has been updated to use the latest .NET framework
2. **Azure OpenAI Client Updates**: Integration with the latest Azure OpenAI client libraries
3. **SignalR Hub Enhancements**: Improved connection management and progress reporting
4. **ZIP Processing Optimizations**: More efficient handling of ZIP archives with multiple files
5. **Swagger Documentation**: Enhanced API documentation with OpenAPI specifications

## Next Development Steps

The following areas have been identified for upcoming development:

1. **Fine-Tuning Pipeline**: Streamlining the process for fine-tuning Azure OpenAI models with domain-specific examples
2. **Enhanced Error Reporting**: More detailed error information to help users troubleshoot conversion issues
3. **Batch Processing Improvements**: Better handling of large batches with pause/resume capabilities
4. **Configuration UI**: Web interface for managing Azure OpenAI settings
5. **Performance Metrics**: Additional telemetry for monitoring conversion performance

## Active Considerations and Trade-offs

The team is currently evaluating several key trade-offs:

1. **Model Selection vs. Performance**: 
   - Using more capable AI models improves conversion accuracy but increases latency and cost
   - Current preference is for accuracy over speed, but with configurable options

2. **Synchronous vs. Asynchronous Processing**:
   - REST API endpoints are simpler for integration but limited for large files
   - SignalR provides better user experience for long-running operations but adds complexity
   - Both approaches are maintained to support different integration scenarios

3. **Error Handling Strategy**:
   - How much automatic correction vs. requiring user intervention
   - Current approach leans toward attempting correction with clear warning messages

4. **Security vs. Usability**:
   - Level of authentication required for API access
   - Current implementation favors usability for development but supports adding authentication

## Implementation Patterns

The following patterns have emerged as particularly important:

1. **Prompt Engineering**:
   - Structured prompts for Azure OpenAI to ensure consistent YAML output
   - Example-based prompting with domain-specific context
   - Temperature and top-p settings optimized for deterministic outputs

2. **Progressive Processing**:
   - Breaking large files into manageable chunks
   - Processing in stages with checkpoints
   - Providing incremental updates via SignalR

3. **Resilient External Service Calls**:
   - Retry logic for Azure OpenAI API calls
   - Circuit breaker pattern to prevent cascading failures
   - Fallback mechanisms for service degradation

## Development Preferences

The following conventions and preferences have been established:

1. **Code Organization**:
   - Domain-driven folder structure
   - Interface-based design with dependency injection
   - Minimal API endpoints where appropriate, Controllers for complex operations

2. **Testing Approach**:
   - L0 tests for unit testing with mocked dependencies
   - L1 tests for integration testing with stub services
   - High code coverage priority (target 80%+)

3. **Coding Style**:
   - C# coding conventions with nullable reference types
   - XML documentation comments for public APIs
   - Async/await throughout the codebase

4. **Error Handling**:
   - Custom exception types for domain-specific errors
   - Global exception handling middleware
   - Detailed logging with contextual information

## Technical Debt and Known Issues

The following areas have been identified as technical debt or known issues:

1. **Test Coverage Gaps**:
   - SignalR hub integration tests are incomplete
   - Some edge cases in ZIP processing need additional tests

2. **Configuration Management**:
   - Azure OpenAI configuration is currently too verbose
   - Need to simplify configuration for different environments

3. **Error Recovery**:
   - Limited ability to resume failed batch operations
   - Incomplete cleanup of temporary files in some error scenarios

4. **Documentation**:
   - API documentation needs examples for all endpoints
   - WebSocket client documentation is minimal

5. **Performance Bottlenecks**:
   - Large ZIP file extraction can cause memory pressure
   - Parallel processing of files needs optimization

## Current Priorities

Based on user feedback and development progress, the current priorities are:

1. **Improve Conversion Accuracy**: Focus on handling complex grammar patterns correctly
2. **Enhance Error Reporting**: Provide more actionable feedback on conversion issues
3. **Optimize Batch Processing**: Better handling of large ZIP archives
4. **Documentation**: Complete the API documentation and add more examples
5. **Performance**: Address identified bottlenecks in processing pipeline