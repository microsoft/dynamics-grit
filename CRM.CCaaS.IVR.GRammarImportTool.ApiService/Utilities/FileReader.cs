using System.Text;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities
{
    public class FileReader
    {
        public static async Task<string> ReadFileAsTextAsync(IFormFile file)
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }
}
