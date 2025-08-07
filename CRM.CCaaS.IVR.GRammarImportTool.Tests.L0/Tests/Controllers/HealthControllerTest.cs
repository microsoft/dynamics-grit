using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Controllers;
public class HealthControllerTest : IClassFixture<BaseTest>
{
    private readonly BaseTest _baseTest;
    public HealthControllerTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");
        _baseTest.LogProvider.Logger.Clear();
    }

    [Fact]
    public void When_GetHealth_Then_ReturnsOkResultWithHealthyString()
    {

        var healthController = new HealthController();

        // Act
        var result = healthController.GetHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Healthy", okResult.Value);
    }
}
