using System.Threading.Channels;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

public abstract class GptChatBase(ILogger<GptChatGrxmlToMcsConverter> logger) : IGptChat
{
    public abstract Task<Stream> ConvertZipAsync(Stream zipStream, Func<int, string, Task> progressCallback, Func<byte[], Task> completedCallback);
    public abstract Task<string> ConvertFileAsync(string stringFile, Func<int, string, Task> progressCallback, Func<string, Task> completedCallback);
    public abstract Task<string> ConvertZipAsync(Stream zipStream, Channel<KeyValuePair<string, string>> results);
    public abstract Task<string> ConvertFileAsync(string stringFile);

    protected Dictionary<string, string> LoadZipToDictionary(Stream zipStream)
    {
        Dictionary<string, string> entries = new Dictionary<string, string>();

        using (var a = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in a.Entries)
            {
                using (var entryStream = entry.Open())
                using (var reader = new StreamReader(entryStream, Encoding.UTF8))
                {
                    string content = reader.ReadToEnd();
                    //Handle duplicate file names by appending a numeric suffix
                    string fileName = entry.Name;
                    int duplicateCount = 1;
                    while (entries.ContainsKey(fileName))
                    {
                        fileName = $"{Path.GetFileNameWithoutExtension(entry.Name)}-{duplicateCount}{Path.GetExtension(entry.Name)}";
                        duplicateCount++;
                    }
                    entries.Add(fileName, content);
                }
            }
        }
        return entries;
    }

    /// <summary>
    /// Removes comments and whitespace from an XML element and its descendants.
    /// </summary>
    protected void RemoveCommentsAndWhitespace(System.Xml.Linq.XElement element)
    {
        if (element == null) return;
        foreach (var node in element.DescendantNodes().OfType<System.Xml.Linq.XComment>().ToList())
        {
            node.Remove();
        }
        foreach (var node in element.DescendantNodes().OfType<System.Xml.Linq.XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList())
        {
            node.Remove();
        }
    }

    /// <summary>
    /// Reads and cleans XML content from a ZipArchiveEntry.
    /// </summary>
    protected bool TryReadXmlContent(string fileName, string xmlContent, out string strippedContent)
    {
        try
        {
            var xmlDoc = System.Xml.Linq.XDocument.Parse(xmlContent);
            if (xmlDoc.Root is not null)
            {
                RemoveCommentsAndWhitespace(xmlDoc.Root);
            }
            strippedContent = xmlDoc.ToString();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse {FileName} as XML at {Timestamp}.", fileName, DateTime.UtcNow);
            strippedContent = $"Error: Failed to parse as XML.";
            return false;
        }
    }

    /// <summary>
    /// Creates a MemoryStream containing a zip archive with one text file per result.
    /// </summary>
    protected MemoryStream CreateResultZipStream(ConcurrentDictionary<string, string> results, string newFileExtension)
    {
        ArgumentNullException.ThrowIfNull(results, nameof(results));
        var outputStream = new MemoryStream();
        using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var kvp in results)
            {
                var entry = outputArchive.CreateEntry(Path.GetFileNameWithoutExtension(kvp.Key) + newFileExtension);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(kvp.Value);
            }
        }
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// Creates the chat client for OpenAI.
    /// </summary>
    protected IChatClient CreateChatClient(string endpoint, string deployment, string key)
    {
        return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
            .AsChatClient(deployment);
    }

    protected string ReadStreamToString(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));
        // Ensure the stream is at the beginning
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            return reader.ReadToEnd();
        }
    }
}
