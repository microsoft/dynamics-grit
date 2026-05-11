// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;

/// <summary>
/// Provides validation for files uploaded to the GRIT system.
/// Implements security guardrails to prevent malicious use.
/// </summary>
public partial class FileValidator(IOptions<GptChatGrxmlConfiguration> configuration)
{
    private readonly ILogger<FileValidator> _logger = GrITLoggerFactory.CreateLogger<FileValidator>();
    private readonly GptChatGrxmlConfiguration _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

    // Supported file extensions
    private static readonly string[] AllowedExtensions = [".grxml", ".xml", ".zip"];

    // Supported MIME types
    private static readonly string[] AllowedMimeTypes =
    [
        "application/xml",
        "text/xml",
        "application/zip",
        "application/x-zip-compressed",
        "application/octet-stream",
        "application/srgs+xml"
    ];

    // XML file signatures (magic numbers)
    private static readonly byte[][] XmlSignatures =
    [
        Encoding.ASCII.GetBytes("<?xml"),                 // Standard XML declaration
        Encoding.ASCII.GetBytes("<grammar"),              // GRXML files might start with grammar tag
        [0xEF, 0xBB, 0xBF]                  // UTF-8 BOM potentially followed by XML content
    ];

    // ZIP file signature (magic number)
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    // Patterns that might indicate potentially malicious content
    private static readonly Regex[] SuspiciousPatterns =
    [
        ScriptTagPattern(),
        JavascriptSchemePattern(),
        IframeTagPattern(),
        DataHtmlPattern(),
        EntityTagPattern(),
        DoctypeTagPattern(),
        PhyEntityTagPattern(),
        // Detect file extensions that should not be in GRXML
        DangerousExtensionPattern()
    ];

    /// <summary>
    /// Validates a file uploaded via IFormFile for security and content restrictions.
    /// </summary>
    /// <param name="file">The file to validate</param>
    /// <param name="isGrxml">Whether the file should be treated specifically as a GRXML file</param>
    /// <returns>ValidationResult indicating success or failure with reason</returns>
    public virtual async Task<ValidationResult> ValidateUploadedFileAsync(IFormFile file, bool isGrxml = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        // Validate file size
        if (file.Length <= 0)
        {
            return ValidationResult.Failure("File is empty");
        }

        if (file.Length > _configuration.AllowedUploadFileSizeRangeBytes)
        {
            _logger.LogWarning("File size exceeded limit. Size={Size}, MaxAllowed={MaxSize}",
                file.Length, _configuration.AllowedUploadFileSizeRangeBytes);
            return ValidationResult.Failure("File size exceeds maximum allowed size");
        }

        // Validate file extension
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Array.Exists(AllowedExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Unsupported file extension. FileName={FileName}, Extension={Extension}",
                _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(file.FileName) : file.FileName, extension);
            return ValidationResult.Failure("Unsupported file type. Only GRXML, XML, and ZIP files are allowed");
        }

        // Validate MIME type
        bool validMimeType = false;
        foreach (var mimeType in AllowedMimeTypes)
        {
            if (file.ContentType.StartsWith(mimeType, StringComparison.OrdinalIgnoreCase))
            {
                validMimeType = true;
                break;
            }
        }

        if (!validMimeType)
        {
            _logger.LogWarning("Unsupported MIME type. FileName={FileName}, MimeType={MimeType}",
                _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(file.FileName) : file.FileName, file.ContentType);
            return ValidationResult.Failure("Unsupported file type based on content");
        }

        // Validate file signature (magic number)
        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            stream.Position = 0;

            // Check file signature
            var result = await ValidateFileSignatureAsync(stream, extension);
            if (!result.IsValid)
            {
                return result;
            }

            // Perform additional validation based on file type
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                stream.Position = 0;
                return await ValidateZipContentsAsync(stream);
            }
            else if (isGrxml || extension.Equals(".grxml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                stream.Position = 0;
                return await ValidateXmlContentsAsync(stream);
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a stream containing XML for security restrictions.
    /// </summary>
    /// <param name="stream">The stream containing XML data</param>
    /// <returns>ValidationResult indicating success or failure with reason</returns>
    public virtual async Task<ValidationResult> ValidateXmlContentsAsync(Stream stream)
    {
        if (stream == null || !stream.CanRead)
        {
            return ValidationResult.Failure("Invalid XML stream");
        }

        try
        {
            // Read the contents to check for suspicious patterns
            stream.Position = 0;
            string content;
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                content = await reader.ReadToEndAsync();
            }

            // Check for suspicious patterns
            foreach (var pattern in SuspiciousPatterns)
            {
                if (pattern.IsMatch(content))
                {
                    _logger.LogWarning("Suspicious pattern detected in XML content");
                    return ValidationResult.Failure("File contains potentially unsafe content");
                }
            }

            return ValidationResult.Success();
        }
        catch (Exception ex) when (ex is not XmlException)
        {
            _logger.LogError(ex, "Error validating XML contents");
            return ValidationResult.Failure("Error validating file contents");
        }
    }

    /// <summary>
    /// Validates a stream containing a ZIP archive for security and content restrictions.
    /// </summary>
    /// <param name="stream">The stream containing ZIP data</param>
    /// <returns>ValidationResult indicating success or failure with reason</returns>
    public virtual async Task<ValidationResult> ValidateZipContentsAsync(Stream stream)
    {
        if (stream == null || !stream.CanRead)
        {
            return ValidationResult.Failure("Invalid ZIP stream");
        }

        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // Check entry count
            if (archive.Entries.Count > _configuration.MaxEntryCount)
            {
                _logger.LogWarning("ZIP archive contains too many entries: {Count}", archive.Entries.Count);
                return ValidationResult.Failure($"ZIP archive contains too many entries. Maximum allowed: {_configuration.MaxEntryCount}");
            }

            if (archive.Entries.Count == 0)
            {
                _logger.LogWarning("ZIP archive is empty");
                return ValidationResult.Failure("ZIP archive is empty");
            }

            long totalUncompressedSize = 0;
            using var entryValidator = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Validate each entry in the ZIP
            foreach (var entry in archive.Entries)
            {
                // Skip directories
                if (string.IsNullOrEmpty(entry.Name) || entry.Name.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.EndsWith("\\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check file extension
                string entryExtension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!Array.Exists(AllowedExtensions, ext => ext.Equals(entryExtension, StringComparison.OrdinalIgnoreCase)
                    && !entryExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("ZIP contains file with unsupported extension: {Extension}", entryExtension);
                    return ValidationResult.Failure("ZIP contains files with unsupported extensions. Only GRXML and XML files are allowed");
                }

                // Check entry size
                if (entry.Length > _configuration.MaxEntrySize)
                {
                    _logger.LogWarning("ZIP entry exceeds max size: {Size}", entry.Length);
                    return ValidationResult.Failure($"ZIP entry {entry.Name} exceeds maximum allowed size");
                }

                totalUncompressedSize += entry.Length;
                if (totalUncompressedSize > _configuration.MaxTotalUncompressedSize)
                {
                    _logger.LogWarning("Total uncompressed size exceeds limit: {Size}", totalUncompressedSize);
                    return ValidationResult.Failure("Total uncompressed size of ZIP archive exceeds maximum allowed");
                }

                // Check file path for traversal attempts
                string fullPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), entry.FullName));
                if (!fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("ZIP entry contains path traversal attempt: {Path}",
                        _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(entry.FullName) : entry.FullName);
                    return ValidationResult.Failure("ZIP archive contains potential path traversal attack");
                }

                // Validate XML content of the entry
                try
                {
                    using var entryStream = entry.Open();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream, entryValidator.Token);
                    memoryStream.Position = 0;

                    var result = await ValidateXmlContentsAsync(memoryStream);
                    if (!result.IsValid)
                    {
                        _logger.LogWarning("ZIP entry contains invalid XML: {Entry}",
                            _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(entry.FullName) : entry.FullName);
                        return ValidationResult.Failure($"ZIP entry {entry.Name} contains invalid XML: {result.ErrorMessage}");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Timeout validating ZIP entry: {Entry}",
                        _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(entry.FullName) : entry.FullName);
                    return ValidationResult.Failure("Timeout validating ZIP archive entries");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating ZIP entry: {Entry}",
                        _configuration.HashFileNameInLogs ? HashHelper.HashSha256Hex(entry.FullName) : entry.FullName);
                    return ValidationResult.Failure($"Error validating ZIP entry {entry.Name}");
                }
            }

            return ValidationResult.Success();
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid ZIP format");
            return ValidationResult.Failure("Invalid ZIP format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating ZIP contents");
            return ValidationResult.Failure("Error validating ZIP contents");
        }
    }

    /// <summary>
    /// Validates file signature (magic number) to ensure the file matches its claimed type.
    /// </summary>
    /// <param name="stream">The stream containing file data</param>
    /// <param name="extension">The file extension</param>
    /// <returns>ValidationResult indicating success or failure with reason</returns>
    private async Task<ValidationResult> ValidateFileSignatureAsync(Stream stream, string extension)
    {
        if (stream == null || !stream.CanRead)
        {
            return ValidationResult.Failure("Invalid file stream");
        }

        try
        {
            stream.Position = 0;
            byte[] buffer = new byte[Math.Max(ZipSignature.Length, XmlSignatures.Max(s => s.Length))];
            await stream.ReadAsync(buffer);

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Validate ZIP signature
                if (!StartsWithSignature(buffer, ZipSignature))
                {
                    _logger.LogWarning("File with .zip extension does not have a valid ZIP signature");
                    return ValidationResult.Failure("File does not appear to be a valid ZIP archive");
                }
            }
            else if (extension.Equals(".grxml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                // Validate XML signature
                bool validXmlSignature = false;
                foreach (var signature in XmlSignatures)
                {
                    if (StartsWithSignature(buffer, signature))
                    {
                        validXmlSignature = true;
                        break;
                    }
                }

                if (!validXmlSignature)
                {
                    _logger.LogWarning("File with XML extension does not have a valid XML signature");
                    return ValidationResult.Failure("File does not appear to be a valid XML document");
                }
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating file signature");
            return ValidationResult.Failure("Error validating file signature");
        }
    }

    /// <summary>
    /// Checks if a byte array starts with a specific signature.
    /// </summary>
    /// <param name="buffer">The buffer to check</param>
    /// <param name="signature">The signature to look for</param>
    /// <returns>True if the buffer starts with the signature</returns>
    private static bool StartsWithSignature(byte[] buffer, byte[] signature)
    {
        if (buffer.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"<\s*script", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex ScriptTagPattern();
    [GeneratedRegex(@"<\s*iframe", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex IframeTagPattern();
    [GeneratedRegex(@"javascript:", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex JavascriptSchemePattern();
    [GeneratedRegex(@"data:text/html", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex DataHtmlPattern();
    [GeneratedRegex(@"<!ENTITY", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex EntityTagPattern();
    [GeneratedRegex(@"<!DOCTYPE[^>]*\[", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex DoctypeTagPattern();
    [GeneratedRegex(@"PHY_ENTITY", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex PhyEntityTagPattern();
    [GeneratedRegex(@"\.(exe|dll|bat|cmd|ps1|sh|jar|js|vbs|py|pl|rb|php|asp|aspx|jsp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    private static partial Regex DangerousExtensionPattern();
}

/// <summary>
/// Represents the result of a file validation operation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    private ValidationResult(bool isValid, string? errorMessage = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    public static ValidationResult Success() => new ValidationResult(true, null);
    public static ValidationResult Failure(string errorMessage) => new ValidationResult(false, errorMessage);
}
