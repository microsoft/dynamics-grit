# Product Context: Grammar Import Tool (GrIT)

## Problem Statement
Organizations transitioning from traditional Interactive Voice Response (IVR) systems to Microsoft Copilot Studio face a significant challenge: converting their existing GRXML (Grammar XML) voice recognition rules into a compatible format. This conversion process is typically manual, error-prone, and resource-intensive, creating a barrier to migration that slows down digital transformation initiatives.

The Grammar Import Tool (GrIT) addresses this challenge by providing an automated, intelligent conversion mechanism that preserves the semantic meaning and functionality of GRXML files while transforming them into the YAML entity format required by Microsoft Copilot Studio.

## User Experience Goals

### Primary User Journeys

1. **Single File Conversion**
   - User uploads a GRXML file through the web interface or API
   - System processes the file using Azure OpenAI to interpret grammar rules
   - User receives a downloadable YAML file compatible with Copilot Studio
   - User imports the YAML file into their Copilot Studio project

2. **Batch Conversion**
   - User uploads a ZIP archive containing multiple GRXML files
   - System processes each file in the archive
   - User receives a ZIP archive containing corresponding YAML files
   - User imports the entities into their Copilot Studio project

3. **Real-time Integration**
   - Developer connects to the SignalR hub via WebSocket
   - Developer sends GRXML file(s) for processing
   - Developer receives progress updates in real-time
   - Developer handles completed YAML conversions in their application

### Experience Principles
- **Simplicity**: Minimize steps required to complete a conversion
- **Transparency**: Provide clear feedback on conversion progress and outcomes
- **Reliability**: Ensure consistent, accurate conversion results
- **Flexibility**: Support multiple integration patterns (REST, WebSocket)
- **Discoverability**: Make API documentation and examples easily accessible

## Business Logic

### Domain Concepts

- **GRXML Files**: XML documents containing grammar rules used in speech recognition systems
- **Grammar Rules**: Structured patterns defining recognizable speech inputs
- **Entities**: In Copilot Studio, collections of related items that help the AI understand user inputs
- **Conversion Process**: The transformation from GRXML grammar rules to Copilot Studio entities
- **Batch Processing**: Handling multiple GRXML files within a single operation
- **Conversion Job**: A tracked unit of work representing a file or batch being processed

### Key Business Rules

1. The conversion must preserve the semantic meaning of grammar rules
2. Each GRXML rule should map to an appropriate entity type in Copilot Studio
3. Complex grammar patterns require intelligent interpretation via AI
4. Large files or batches should be processed asynchronously
5. Users must receive appropriate feedback during long-running operations
6. The system must validate input files before processing

## Critical Workflows

### GRXML to YAML Conversion Workflow
1. Receive GRXML file input (via API or UI)
2. Validate file format and structure
3. Parse GRXML into memory model
4. Apply Azure OpenAI processing to interpret grammar patterns
5. Transform into Copilot Studio YAML entity format
6. Return converted YAML to user

### ZIP Archive Processing Workflow
1. Receive ZIP archive containing GRXML files
2. Extract and validate contained files
3. Process each valid GRXML file
4. Compile converted YAML files into a new ZIP archive
5. Return the archive to the user

### Real-time Processing Workflow
1. Establish WebSocket connection via SignalR
2. Receive file data through the connection
3. Initialize processing and send acknowledgment
4. Send progress updates as processing continues
5. Return conversion results when complete

## Success Metrics
- **Conversion Accuracy**: Percentage of grammar rules correctly converted
- **Processing Speed**: Average time to convert files of different sizes
- **Error Rate**: Percentage of conversion attempts resulting in errors
- **User Satisfaction**: Feedback from users on conversion quality
- **Adoption Rate**: Number of organizations successfully using the tool for migration