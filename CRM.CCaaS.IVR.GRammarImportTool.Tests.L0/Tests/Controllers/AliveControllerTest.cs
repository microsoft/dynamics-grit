// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Controllers;
[Collection("BaseTestCollection")]
public class AliveControllerTest
{
    private readonly BaseTest _baseTest;
    public AliveControllerTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _baseTest.LogProvider.Logger.Clear();
    }

    [Fact]
    public void When_GetLiveness_Then_ReturnsOkResultWithAliveString()
    {
        var aliveController = new AliveController();

        var result = aliveController.GetLiveness();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Alive", okResult.Value);
    }
}
