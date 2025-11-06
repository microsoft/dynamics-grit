using Microsoft.ML.Tokenizers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Logging;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;

/// <summary>
/// Service for validating token count in AI prompts to ensure they don't exceed limits.
/// </summary>
public partial class TokenValidator(IOptions<GptChatGrxmlConfiguration> configuration)
{
    private readonly int _maxTokenLimit = configuration.Value.MaxTokenLimit;
    private readonly ILogger<TokenValidator> _logger = GrITLoggerFactory.CreateLogger<TokenValidator>();
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    /// <summary>
    /// Validates that the given text doesn't exceed the maximum token limit.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <param name="context">Optional context for logging (e.g., file name).</param>
    /// <returns>A validation result indicating if the text is within token limits.</returns>
    public virtual ValidationResult ValidateTokenCount(string? text, string? context = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ValidationResult.Success();
        }

        try
        {
            var tokens = _tokenizer.CountTokens(text);

            _logger.LogInformation("[TokenValidator] Token count: {TokenCount} | Context: {Context}",
                tokens, context ?? "unknown");

            if (tokens > _maxTokenLimit)
            {
                var errorMessage = $"Token count ({tokens}) exceeds the maximum limit of {_maxTokenLimit} tokens.";
                _logger.LogWarning("[TokenValidator] Token limit exceeded | TokenCount: {TokenCount} | Limit: {Limit} | Context: {Context}",
                    tokens, _maxTokenLimit, context ?? "unknown");

                return ValidationResult.Failure(errorMessage);
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TokenValidator] Error counting tokens | Context: {Context}", context ?? "unknown");

            // In case of tokenization error, allow the request to proceed but log the error
            return ValidationResult.Success();
        }
    }

    /// <summary>
    /// Gets the current maximum token limit.
    /// </summary>
    public virtual int MaxTokenLimit => _maxTokenLimit;
}
