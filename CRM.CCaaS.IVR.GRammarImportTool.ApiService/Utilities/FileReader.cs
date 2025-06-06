using System.Text;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;

public static class FileReader
{
    public static async Task<string> ReadFileAsTextAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("Invalid file.");
        }

        if (!file.ContentType.Equals("application/srgs+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Unsupported file type.");
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
