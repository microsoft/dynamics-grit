# Project Brief: Grammar Import Tool (GrIT)

## Overview
The Grammar Import Tool (GrIT) is a specialized conversion utility designed to transform GRXML (Grammar XML) files into YAML format compatible with Microsoft Copilot Studio. It serves as a critical bridge between traditional voice recognition grammar files and modern conversational AI platforms, facilitating the migration process for enterprise customers.

## Core Requirements
- Convert GRXML files to structured YAML entities for Microsoft Copilot Studio
- Process both individual files and batches (ZIP archives)
- Provide both synchronous (REST API) and asynchronous (SignalR) processing options
- Maintain high fidelity of grammar rules during conversion
- Leverage Azure OpenAI for intelligent interpretation of complex grammar patterns
- Offer a simple, intuitive interface for developers and system integrators

## Target Users
- Contact Center as a Service (CCaaS) teams migrating from legacy IVR systems
- Developers integrating voice recognition capabilities into Copilot Studio
- System integrators working with Microsoft Dynamics CRM voice solutions
- Enterprise customers transitioning from traditional GRXML-based systems

## Key Value Propositions
- Significantly reduces migration time from legacy IVR systems to Copilot Studio
- Maintains the integrity of existing grammar rules during conversion
- Provides real-time conversion feedback through WebSocket connections
- Eliminates manual conversion errors and inconsistencies
- Integrates easily with existing workflows through standard REST API patterns
- Supports batch processing for large-scale migrations

## Project Scope
### In Scope
- GRXML to YAML conversion engine
- REST API endpoints for programmatic integration
- SignalR WebSocket interface for real-time updates
- Web interface for demonstration and testing
- Comprehensive documentation and examples
- Fine-tuning capabilities for specialized grammar patterns

### Out of Scope
- Modification of the underlying grammar rules
- Direct integration with Microsoft Copilot Studio (output files to be imported manually)
- Support for grammar formats other than GRXML
- Training or generation of new grammar rules
- Management of converted entities within Copilot Studio

## Success Criteria
- Accurate conversion of GRXML files to YAML with 95%+ fidelity
- Performance metrics for batch processing (minimum 50 files per minute)
- Integration with CI/CD pipelines for automated testing
- 80%+ code coverage for unit and integration tests
- Complete documentation for API endpoints and usage patterns