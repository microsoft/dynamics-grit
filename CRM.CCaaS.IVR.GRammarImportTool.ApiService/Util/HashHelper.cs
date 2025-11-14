// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
public static class HashHelper
{
    public static string HashSha256Hex(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            throw new ArgumentException("Input cannot be null or empty.", nameof(input));
        }

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return ConvertToHex(hash);
    }

    private static string ConvertToHex(byte[] bytes) =>
        BitConverter.ToString(bytes).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}
