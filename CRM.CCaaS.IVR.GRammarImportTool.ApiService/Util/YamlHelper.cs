namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;

public static class YamlHelper
{
    private static readonly char[] TrimChars = [' ', '\n', '\r'];

    public static string SerializeYaml(string? data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        var serializer = new YamlDotNet.Serialization.Serializer();
        return serializer.Serialize(data.Trim(TrimChars));
    }
}
