using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Store;

/// <summary>
/// Unit tests for ConversionResult.
/// These tests assume:
///  - A constructor ConversionResult(ConcurrentDictionary<string,string>? resultData = null)
///  - Public property ConcurrentDictionary<string,string> ResultData { get; }
///  - Public DateTime CreatedAt { get; set; }
/// Adjust if the actual signature differs.
/// </summary>
public class ConversionResultTest
{
    private static ConversionResult Create(params (string key, string value)[] pairs)
    {
        var dict = new ConcurrentDictionary<string, string>(
            pairs.Select(p => new KeyValuePair<string, string>(p.key, p.value)));
        return new ConversionResult(dict);
    }

    [Fact]
    public void When_CreatedWithDictionary_Then_DictionaryReferenced()
    {
        var dict = new ConcurrentDictionary<string, string>();
        dict["a"] = "1";
        var result = new ConversionResult(dict);
        Assert.Same(dict, result.ResultData);
        Assert.Equal("1", result.ResultData["a"]);
    }

    [Fact]
    public void When_AddingEntries_AfterCreation_Then_ResultDataUpdates()
    {
        var result = Create(("x", "100"));
        result.ResultData["y"] = "200";
        Assert.Equal(2, result.ResultData.Count);
        Assert.Equal("100", result.ResultData["x"]);
        Assert.Equal("200", result.ResultData["y"]);
    }

    [Fact]
    public void When_ModifyingExistingEntry_Then_ValueReplaced()
    {
        var result = Create(("k", "v1"));
        result.ResultData["k"] = "v2";
        Assert.Single(result.ResultData);
        Assert.Equal("v2", result.ResultData["k"]);
    }

    [Fact]
    public void When_Created_Then_CreatedAtIsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-2);
        var result = Create();
        var after = DateTime.UtcNow.AddSeconds(2);

        Assert.True(result.CreatedAt >= before && result.CreatedAt <= after);
    }

    [Fact]
    public void When_CreatedAtOverridden_Then_UsesProvidedValue()
    {
        var custom = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var result = Create();
        result.CreatedAt = custom;
        Assert.Equal(custom, result.CreatedAt);
    }

    [Fact]
    public async Task When_ConcurrentAdds_Then_AllEntriesPresent()
    {
        var result = Create();
        var range = Enumerable.Range(0, 1000).ToArray();

        await Parallel.ForEachAsync(range, (i, _) =>
        {
            result.ResultData.TryAdd("k" + i, i.ToString());
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1000, result.ResultData.Count);
        Assert.Equal("0", result.ResultData["k0"]);
        Assert.Equal("999", result.ResultData["k999"]);
    }

    [Fact]
    public void When_ToStringImplemented_Then_NotNullOrEmpty()
    {
        var result = Create(("a", "b"));
        var s = result.ToString();
        Assert.False(string.IsNullOrWhiteSpace(s));
    }

    [Fact]
    public void When_ResultDataCleared_Then_IsEmpty()
    {
        var result = Create(("a", "b"), ("c", "d"));
        result.ResultData.Clear();
        Assert.Empty(result.ResultData);
    }
}
