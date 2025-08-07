# Unit Tests

This project contains unit tests for the CRM.CCaaS.IVR.GRammarImportTool application. The tests are organized into the following folders:

- **Controllers**: Tests for the API controllers.
- **Domain**: Tests for the domain logic.
- **Hubs**: Tests for the SignalR hubs.

Unit tests are also referred to as L0 tests.

## Running the Tests
To run the tests, you can use the following command in the terminal:
```bash or powershell
dotnet test --verbosity detailed --logger "trx" --collect:"XPlat Code Coverage"  --settings Test.runsettings  --results-directory ".\TestResults" -c Release -p:Platform="Any CPU" /p:CoverletOutputFormat=cobertura /p:PublishTestResults=false .\CRM.CCaaS.IVR.GRammarImportTool.sln --filter FullyQualifiedName~Tests.L0
```

Test Explorer in Visual Studio can also be used to run and debug the tests.
1. From the "Test" menu  select "Test Explorer".
2. Select category and run tests.

## Code Coverage
Code coverage reports are generated in the `TestResults` directory after running the tests. You can view the coverage report in a web browser by opening the `index.htm` file located in the `coverage` folder within the `TestResults` directory.
To open the coverage report, navigate to the `TestResults` directory and open the `index.htm` file in your preferred web browser.

To run test coverage in Visual Studio, you can use the following steps:
1. From the "Test" menu, select "Analyze Code Coverage" and then "All Tests".
2. The code coverage results will be displayed in the "Code Coverage Results" window.

Our code coverage target is set to 80% for new files. For classes covered 90% coverage should stay 90% or above.

Delta code coverage is also tracked. If a file's coverage drops below 80% or 90% (for already covered files), it will be flagged.
Delta code coverage is posted as a comment on the pull request. Please verify before approve.

## Adding new Tests

When adding new tests, please follow these guidelines:

1. Every class should have its tests in separate file named `<ClassName>Test.cs`.
2. **Naming Conventions**  
  Use following template for test names:
   - For unit tests: `When_<StateUnderTest>_Then_<ExpectedBehavior>`
for example, `When_InputIsValid_Then_ReturnsSuccess`.
3. Use BaseTest as TestFixture for all tests.
```C# 
public class ClassTest : IClassFixture<BaseTest>, IDisposable
```
4. Can pull log messages to Assert() aginst expected log messages.
```C#
var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
```

5. GrIT configuration for tests can be customizzed in BaseTest.cs (BuildMocks method) file.
```C#
var testConfiguration = new GptChatGrxmlConfiguration();
        testConfiguration.AzureOpenAIEndpoint = "https://test.openai.azure.com/";
        testConfiguration.AzureOpenAIDeploymentName = "test-deployment";
        testConfiguration.AzureOpenAIKey = "test-key";

        etc
```
