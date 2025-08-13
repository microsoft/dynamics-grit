using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Xml;
using System.Xml.Linq;
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("CRM.CCaaS.IVR.GRammarImportTool.Tests.L0")]
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

public abstract class GptChatBase(IAzureOpenAIClientFactory azureOpenAIClientFactory, GptChatGrxmlConfiguration? gptChatGrxmlConfiguration = null) : IGptChat
{
    public abstract Task<Stream> ConvertZipAsync(Stream zipStream, Func<int, string, Task> progressCallback, Func<byte[], Task> completedCallback);
    public abstract Task<string> ConvertFileAsync(string stringFile, Func<int, string, Task> progressCallback, Func<string, Task> completedCallback);
    public abstract Task<string> ConvertZipAsync(Stream zipStream, Channel<KeyValuePair<string, string>> results);
    public abstract Task<string> ConvertFileAsync(string stringFile);

    public IAzureOpenAIClientFactory AzureOpenAIClientFactory { get; } = azureOpenAIClientFactory;
    protected GptChatGrxmlConfiguration? GptChatGrxmlConfiguration { get; } = gptChatGrxmlConfiguration;

    private readonly ILogger<GptChatBase> _logger = GrITLoggerFactory.CreateLogger<GptChatBase>();
    private const int DefaultBufferSize = 8192;

    internal async Task<Dictionary<string, string>> LoadZipToDictionaryAsync(Stream zipStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream, nameof(zipStream));

        long maxEntrySize = GptChatGrxmlConfiguration?.MaxEntrySize ?? 1 * 1024 * 1024;
        long maxTotalUncompressedSize = GptChatGrxmlConfiguration?.MaxTotalUncompressedSize ?? 100 * 1024 * 1024;
        int maxEntryCount = GptChatGrxmlConfiguration?.MaxEntryCount ?? 1000;

        var entries = new ConcurrentDictionary<string, string>();
        long totalUncompressedSize = 0;

        // Compute hash of the zip stream for logging
        string zipFileHash;
        if (zipStream.CanSeek)
        {
            long originalPosition = zipStream.Position;
            zipStream.Position = 0;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                zipFileHash = Convert.ToHexString(sha256.ComputeHash(zipStream));
            }
            zipStream.Position = originalPosition;
        }
        else
        {
            zipFileHash = "StreamNotSeekable";

            _logger.LogWarning("Zip stream is not seekable; using fallback identifier {ZipFileHash}.", zipFileHash);
        }

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count > maxEntryCount)
        {
            _logger.LogWarning("Zip file contains too many entries ({EntryCount}).", archive.Entries.Count);
            throw new InvalidDataException("Zip file has too many entries.");
        }

        var tasks = archive.Entries
            .Where(entry =>
            {
                string fileName = Path.GetFileName(entry.FullName); // strips directory traversal
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    _logger.LogWarning("Skipped a zip entry with empty or whitespace name.");
                    return false;
                }
                return true;
            }) // Skip directory entries and log warning
            .Select(async entry =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Length > maxEntrySize)
                {
                    _logger.LogWarning("Zip entry {EntryName} exceeds maximum allowed size ({EntrySize} bytes).", entry.FullName, entry.Length);
                    throw new InvalidDataException($"Zip entry '{entry.FullName}' is too large.");
                }

                totalUncompressedSize += entry.Length;
                Interlocked.Add(ref totalUncompressedSize, entry.Length);
                if (totalUncompressedSize > maxTotalUncompressedSize)
                {
                    _logger.LogWarning("Total uncompressed size of zip exceeds limit ({TotalSize} bytes).", totalUncompressedSize);
                    throw new InvalidDataException("Zip file is too large when decompressed.");
                }

                // Path traversal protection
                string safeRoot = Path.GetFullPath(".");
                string fullPath = Path.GetFullPath(Path.Combine(safeRoot, entry.FullName));
                if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Zip entry path traversal detected: {EntryName}.", entry.FullName);
                    throw new SecurityException("Zip entry path traversal detected.");
                }

                Stream entryStream;
                lock (archive)
                {
                    entryStream = entry.Open();
                }
                using (entryStream)
                using (var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: DefaultBufferSize, leaveOpen: false))
                {
                    string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                    string fileName = Path.GetFileName(entry.FullName); // strips directory traversal
                    int duplicateCount = 1;

                    // Ensure unique file names
                    while (!entries.TryAdd(fileName, content))
                    {
                        // Log a warning with a hash of the file name
                        string fileNameHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fileName)));
                        string truncatedName = fileName.Length > 20 ? fileName[..20] + "..." : fileName;
                        _logger.LogWarning(
                            "Duplicate file name detected in zip: {FileNameHash} (Original: {TruncatedName}, ZipHash: {ZipFileHash}).",
                            fileNameHash, truncatedName, zipFileHash);

                        fileName = $"{Path.GetFileNameWithoutExtension(fileName)}-{duplicateCount}{Path.GetExtension(fileName)}";
                        duplicateCount++;
                    }
                }
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (entries.IsEmpty)
        {
            _logger.LogWarning("No entries found in the zip file (ZipHash: {ZipFileHash}).", zipFileHash);
            throw new InvalidDataException("The zip file contains no entries.");
        }

        return new Dictionary<string, string>(entries);
    }

    /// <summary>
    /// Reads and cleans XML content from a ZipArchiveEntry.
    /// </summary>
    /// <param name="fileName">The name of the file being processed.</param>
    /// <param name="xmlContent">The XML content to parse and clean.</param>
    /// <param name="strippedContent">The cleaned/minified XML content, or an error message if parsing fails. This parameter is returned as an output.</param>
    /// <returns>True if the XML was successfully parsed and cleaned; otherwise, false.</returns>
    internal bool TryReadXmlContent(string fileName, string xmlContent, out string strippedContent)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024,
                MaxCharactersInDocument = 10_000_000
            };

            using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
            var xmlDoc = XDocument.Load(reader, LoadOptions.None);

            if (xmlDoc.Root is not null)
            {
                strippedContent = GRXMLSanitizer.MinifyGRXMLContent(xmlDoc);
            }
            else
            {

                _logger.LogWarning("XML document has no root element in file {FileName}.", fileName);
                strippedContent = "Error: XML document has no root element.";
                return false;
            }

            return true;
        }
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName} as XML (XmlException).", fileName);
            strippedContent = "Error: Failed to parse as XML (XmlException).";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName} as XML (InvalidOperationException).", fileName);
            strippedContent = "Error: Failed to parse as XML (InvalidOperationException).";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName} as XML.", fileName);
            strippedContent = $"Error: Failed to parse as XML.";
            return false;
        }
    }

    /// <summary>
    /// Creates a MemoryStream containing a zip archive with one text file per result.
    /// </summary>
    internal async Task<MemoryStream> CreateResultZipStreamAsync(
        ConcurrentDictionary<string, string> results,
        string newFileExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results, nameof(results));
        if (results.IsEmpty)
        {
            _logger.LogWarning("Attempted to create zip stream with empty results.");
            throw new InvalidDataException("Cannot create zip stream with empty results.");
        }
        var outputStream = new MemoryStream();
        using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var kvp in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = outputArchive.CreateEntry(Path.GetFileNameWithoutExtension(kvp.Key) + newFileExtension);
                await using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8, bufferSize: DefaultBufferSize);
                await writer.WriteAsync(kvp.Value.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        outputStream.Position = 0;
        return outputStream;
    }
}
