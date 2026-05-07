// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

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

    // Defense-in-depth limits for outbound YAML. Copilot Studio entity definitions
    // are shallow (≤4 levels) with hundreds of items at most; these caps leave
    // generous headroom while bounding YAML-bomb / pathological-document blast radius.
    internal const int MaxYamlDepth = 32;
    internal const int MaxYamlNodeCount = 10_000;

    /// <summary>
    /// Sanitizes outbound YAML produced by the AI model before it is returned to the client.
    /// Performs a strict whitelist validation pass over the YAML event stream
    /// (rejects explicit tags, anchors, aliases, multi-document streams, control characters,
    /// and pathological depth/node counts), then re-serializes the parsed representation
    /// to canonical YAML. Throws <see cref="YamlException"/> on any policy violation.
    /// </summary>
    /// <param name="yaml">The raw YAML text returned by the model.</param>
    /// <returns>Canonical, sanitized YAML safe to forward to the client.</returns>
    public static string SanitizeAiYamlOutput(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return yaml ?? string.Empty;
        }

        // Up-front scan of raw bytes: YamlDotNet's scanner can fail on certain control
        // characters (e.g. NUL inside a quoted scalar) with a generic syntax error,
        // and other terminal-injection bytes (ESC, BEL) survive as-is. We surface them
        // all with a consistent, intent-revealing error before the parser runs.
        RejectControlCharacters(yaml);

        ValidateYamlEventStream(yaml);

        // Re-emit canonical YAML from the representation model.
        // assignAnchors:false guarantees we don't introduce anchors during emit
        // (we already proved the input has none).
        var stream = new YamlStream();
        using (var reader = new StringReader(yaml))
        {
            stream.Load(reader);
        }
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    /// <summary>
    /// Walks the YAML event stream without materializing the document, rejecting
    /// any construct that could be an injection vector for a downstream consumer.
    /// Must run BEFORE <see cref="YamlStream.Load"/> so that pathological
    /// alias-expansions never reach the representation model.
    /// </summary>
    private static void ValidateYamlEventStream(string yaml)
    {
        using var reader = new StringReader(yaml);
        var parser = new Parser(reader);

        int documentCount = 0;
        int depth = 0;
        int nodeCount = 0;

        while (parser.MoveNext())
        {
            var current = parser.Current;
            switch (current)
            {
                case StreamStart:
                case StreamEnd:
                case DocumentEnd:
                case Comment:
                    break;

                case DocumentStart:
                    documentCount++;
                    if (documentCount > 1)
                    {
                        throw new YamlException("Outbound YAML must contain a single document.");
                    }
                    break;

                case AnchorAlias alias:
                    throw new YamlException($"YAML aliases are not permitted in outbound content (found '*{alias.Value}').");

                case MappingStart mapping:
                    RejectExplicitTagOrAnchor(mapping.Tag, mapping.Anchor);
                    depth++;
                    nodeCount++;
                    EnforceLimits(depth, nodeCount);
                    break;

                case SequenceStart sequence:
                    RejectExplicitTagOrAnchor(sequence.Tag, sequence.Anchor);
                    depth++;
                    nodeCount++;
                    EnforceLimits(depth, nodeCount);
                    break;

                case MappingEnd:
                case SequenceEnd:
                    depth--;
                    break;

                case Scalar scalar:
                    RejectExplicitTagOrAnchor(scalar.Tag, scalar.Anchor);
                    RejectControlCharacters(scalar.Value);
                    nodeCount++;
                    EnforceLimits(depth, nodeCount);
                    break;
            }
        }
    }

    private static void RejectExplicitTagOrAnchor(TagName tag, AnchorName anchor)
    {
        if (!tag.IsEmpty)
        {
            throw new YamlException($"Explicit YAML tags are not permitted in outbound content (found '{tag.Value}').");
        }
        if (!anchor.IsEmpty)
        {
            throw new YamlException($"YAML anchors are not permitted in outbound content (found '&{anchor.Value}').");
        }
    }

    private static void RejectControlCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        foreach (var ch in value)
        {
            // Allow tab, newline, and carriage return; reject every other control codepoint.
            // This covers ANSI escapes (ESC = 0x1B), NUL, and other terminal-injection vectors.
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
            {
                throw new YamlException($"Outbound YAML scalar contains a disallowed control character (U+{(int)ch:X4}).");
            }
        }
    }

    private static void EnforceLimits(int depth, int nodeCount)
    {
        if (depth > MaxYamlDepth)
        {
            throw new YamlException($"Outbound YAML nesting depth exceeds the allowed maximum ({MaxYamlDepth}).");
        }
        if (nodeCount > MaxYamlNodeCount)
        {
            throw new YamlException($"Outbound YAML node count exceeds the allowed maximum ({MaxYamlNodeCount}).");
        }
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
