using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using Microsoft.Extensions.Options;
using System.Linq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Validation;

[Collection("BaseTestCollection")]
public class TokenValidatorTests
{
    private readonly TokenValidator _tokenValidator;
    private readonly BaseTest _baseTest;

    public TokenValidatorTests(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _baseTest.LogProvider.Logger.Clear();
        var configuration = new GptChatGrxmlConfiguration
        {
            MaxTokenLimit = 10000
        };
        var options = Options.Create(configuration);
        _tokenValidator = new TokenValidator(options);
    }

    [Fact]
    public void ValidateTokenCount_WithEmptyString_ReturnsValid()
    {
        var result = _tokenValidator.ValidateTokenCount(string.Empty);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateTokenCount_WithNullString_ReturnsValid()
    {
        string? nullText = null;
        var result = _tokenValidator.ValidateTokenCount(nullText);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateTokenCount_WithShortText_ReturnsValid()
    {
        var shortText = "This is a short text that should be well within the token limit.";
        var result = _tokenValidator.ValidateTokenCount(shortText);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateTokenCount_WithLongText_ReturnsInvalid()
    {
        // Create a text that should exceed 10k tokens
        // Approximate 1 token per word, so 11,000 words should exceed the limit
        var longText = string.Join(" ", Enumerable.Repeat("word", 11000));
        
        var result = _tokenValidator.ValidateTokenCount(longText, "test-context");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Token count", result.ErrorMessage);
        Assert.Contains("exceeds the maximum limit of 10000 tokens", result.ErrorMessage);
    }

    [Fact]
    public void MaxTokenLimit_ReturnsExpectedValue()
    {
        Assert.Equal(10000, _tokenValidator.MaxTokenLimit);
    }

    [Fact]
    public void TokenValidator_RespectsConfigurationValue()
    {
        // Test with a different token limit
        var configuration = new GptChatGrxmlConfiguration
        {
            MaxTokenLimit = 5000
        };
        var options = Options.Create(configuration);
        var validator = new TokenValidator(options);

        Assert.Equal(5000, validator.MaxTokenLimit);

        // Create text that exceeds 5000 but not 10000 tokens
        var mediumText = string.Join(" ", Enumerable.Repeat("word", 6000));
        var result = validator.ValidateTokenCount(mediumText, "test-context");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("exceeds the maximum limit of 5000 tokens", result.ErrorMessage);
    }
}
