using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;

/// <summary>
/// Provides validation and filtering for AI prompts to enforce Responsible AI guardrails.
/// </summary>
public partial class AiContentValidator(IOptions<GptChatGrxmlConfiguration> configuration)
{
    private readonly ILogger<AiContentValidator> _logger = GrITLoggerFactory.CreateLogger<AiContentValidator>();
    private readonly GptChatGrxmlConfiguration _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

    // Patterns to detect potentially unsafe/malicious content in prompts
    private static readonly Regex[] ProhibitedContentPatterns =
    [
        // Attempts to bypass system instructions
        IgnorePreviousInstructionsPattern(),
        DisregardPreviousInstructionsPattern(),
        ForgetPreviousInstructionsPattern(),
        
        // Prompt injections
        SystemUserPromptPattern(),
        SystemUserAssistantPromptPattern(),
        
        // Attempts to access system functionality
        GenerateMaliciousCodePattern(),
        ExecuteCommandPattern(),
        
        // Content that clearly falls outside the purpose of grammar conversion
        MalwareGenerationPattern(),
        SocialEngineeringScriptPattern(),
        
        // Attempts to manipulate the system behavior
        ImpersonationAttemptPattern()
    ];

    // Keywords that may indicate content unrelated to grammar conversion
    private static readonly HashSet<string> SuspiciousKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hack", "exploit", "vulnerability", "bypass", "security",
        "credential", "password", "authentication", "token",
        "confidential", "private", "personal", "sensitive",
        "script", "malware", "virus", "trojan", "backdoor",
        "attack", "phishing", "scam", "fraud"
    };
    private static readonly char[] Separator = [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}'];

    /// <summary>
    /// Validates a prompt to be sent to an AI model for potential misuse.
    /// </summary>
    /// <param name="prompt">The prompt text to validate</param>
    /// <param name="fileName">Optional filename for context in logging</param>
    /// <returns>ValidationResult indicating success or failure with reason</returns>
    public virtual Task<ValidationResult> ValidateAiPromptAsync(string prompt, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.FromResult(ValidationResult.Failure("Prompt cannot be empty"));
        }

        string fileNameForLogs = _configuration.HashFileNameInLogs && !string.IsNullOrEmpty(fileName)
            ? HashHelper.HashSha256Hex(fileName)
            : fileName ?? "UnknownFile";

        // Check for prohibited patterns
        foreach (var pattern in ProhibitedContentPatterns)
        {
            if (pattern.IsMatch(prompt))
            {
                _logger.LogWarning("Prohibited content pattern detected in prompt. FileName={FileName}", fileNameForLogs);
                return Task.FromResult(ValidationResult.Failure("Prompt contains prohibited content patterns"));
            }
        }

        // Check for suspicious keywords
        if (ContainsSuspiciousKeywords(prompt))
        {
            _logger.LogWarning("Suspicious keywords detected in prompt. FileName={FileName}", fileNameForLogs);
            return Task.FromResult(ValidationResult.Failure("Prompt contains suspicious keywords not related to grammar conversion"));
        }

        // Check if the prompt seems to be related to grammar conversion
        if (!IsLikelyGrammarConversionPrompt(prompt))
        {
            _logger.LogWarning("Prompt appears unrelated to grammar conversion. FileName={FileName}", fileNameForLogs);
            return Task.FromResult(ValidationResult.Failure("Prompt appears unrelated to grammar conversion"));
        }

        return Task.FromResult(ValidationResult.Success());
    }

    /// <summary>
    /// Sanitizes a prompt to be sent to an AI model to reduce potential for misuse.
    /// </summary>
    /// <param name="prompt">The prompt text to sanitize</param>
    /// <returns>The sanitized prompt</returns>
    public static string SanitizeAiPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return prompt;
        }

        // Remove any attempts to use special prompt formatting
        string sanitized = SystemUserAssistantPromptPattern().Replace(prompt, "[REMOVED]");
        sanitized = SystemUserAssistantPromptPattern2().Replace(sanitized, "[REMOVED]");

        return sanitized;
    }

    /// <summary>
    /// Checks if the prompt contains suspicious keywords.
    /// </summary>
    /// <param name="prompt">The prompt to check</param>
    /// <returns>True if suspicious keywords are found</returns>
    private bool ContainsSuspiciousKeywords(string prompt)
    {
        // Split the prompt into words and check against the suspicious keywords list
        var words = prompt.Split(Separator,
            StringSplitOptions.RemoveEmptyEntries);

        return words.Any(SuspiciousKeywords.Contains);
    }

    /// <summary>
    /// Checks if the prompt seems related to grammar conversion.
    /// </summary>
    /// <param name="prompt">The prompt to check</param>
    /// <returns>True if the prompt appears to be related to grammar conversion</returns>
    private bool IsLikelyGrammarConversionPrompt(string prompt)
    {
        // Check for common grammar-related terms and patterns
        bool containsGrammarTerms = GrammarStructurePattern().IsMatch(prompt);

        bool containsXmlStructure = XmlTagStructurePattern().IsMatch(prompt);

        bool containsConversionTerms = prompt.Contains("convert", StringComparison.OrdinalIgnoreCase) ||
                                      prompt.Contains("transform", StringComparison.OrdinalIgnoreCase) ||
                                      prompt.Contains("grxml", StringComparison.OrdinalIgnoreCase) ||
                                      prompt.Contains("grammar", StringComparison.OrdinalIgnoreCase);

        // The prompt should contain either grammar terms or XML structure, and conversion-related terms
        return (containsGrammarTerms || containsXmlStructure) && containsConversionTerms;
    }

    [GeneratedRegex(@"ignore\s+(previous|all|your)\s+instructions", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IgnorePreviousInstructionsPattern();

    [GeneratedRegex(@"disregard\s+(previous|all|your)\s+(instructions|prompt)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DisregardPreviousInstructionsPattern();

    [GeneratedRegex(@"forget\s+(previous|all|your)\s+instructions", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ForgetPreviousInstructionsPattern();

    [GeneratedRegex(@"(system|user)\s*:.*?[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SystemUserPromptPattern();

    [GeneratedRegex(@"<\/?\s*(system|user|assistant)>\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SystemUserAssistantPromptPattern();

    [GeneratedRegex(@"(system|user|assistant)\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SystemUserAssistantPromptPattern2();

    [GeneratedRegex(@"generate\s+(code|script)\s+to\s+(hack|exploit|bypass|attack)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GenerateMaliciousCodePattern();

    [GeneratedRegex(@"(execute|run|perform)\s+(command|script|code)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExecuteCommandPattern();

    [GeneratedRegex(@"(generate|create)\s+(malware|ransomware|virus|exploit)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MalwareGenerationPattern();

    [GeneratedRegex(@"(social\s+engineering|phishing|fraud|scam)\s+(template|script|instruction)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SocialEngineeringScriptPattern();

    [GeneratedRegex(@"(act\s+as|pretend\s+to\s+be|simulate)\s+(?!grammar|xml|converter)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ImpersonationAttemptPattern();

    [GeneratedRegex(@"<\s*grammar|</\s*grammar|<\s*rule|</\s*rule|<\s*one-of|</\s*one-of|<\s*item|</\s*item", RegexOptions.IgnoreCase)]
    private static partial Regex GrammarStructurePattern();

    [GeneratedRegex(@"<[^>]+>.*?</[^>]+>", RegexOptions.Singleline)]
    private static partial Regex XmlTagStructurePattern();
}
