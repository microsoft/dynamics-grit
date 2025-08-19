using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;

public interface IGptChat
{
    public Task<Stream> ConvertZipAsync(Stream zipStream, Func<int, string, Task> progressCallback, Func<byte[], Task> completedCallback);
    public Task<string> ConvertZipAsync(Stream zipStream, Channel<KeyValuePair<string, string>> results);
    public Task<string> ConvertFileAsync(string stringFile, Func<int, string, Task> progressCallback, Func<string, Task> completedCallback);
    public Task<string> ConvertFileAsync(string stringFile);
}
