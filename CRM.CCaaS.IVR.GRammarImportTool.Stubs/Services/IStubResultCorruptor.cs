using System;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

public interface IStubResultCorruptor
{
    (byte[] Body, string ContentType) CorruptIfNeeded(
        byte[] body,
        string contentType,
        Func<bool> shouldCorruptAll,
        Func<(bool shouldCorruptSome, int percentage, int maxPerArray)> someSettings,
        string[]? targetProperties,
        bool produceMalformedJson);
}