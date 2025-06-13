namespace CRM.CCaaS.IVR.GRammarImportTool.Tests;

public class WebTests : IClassFixture<TestD>, IDisposable
{
    private bool _disposedValue;
    private readonly TestD _testD;
    private readonly HttpClient _httpClient;

    public WebTests(TestD testD)
    {
        _testD = testD;
        _httpClient = _testD.GetHttpClient();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _httpClient.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task When_app_started_Then_healthcheck_success()
    {
        var response = await _httpClient.GetAsync("/health");

        _httpClient.Dispose();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
