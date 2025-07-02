namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

public interface IGptChat
{
    public Task<Stream> ConvertZipAsync(Stream zipStream, Func<int, string, Task> progressCallback, Func<byte[], Task> completedCallback);
    public Task<string> ConvertFileAsync(string stringFile, Func<int, string, Task> progressCallback, Func<string, Task> completedCallback);
}
