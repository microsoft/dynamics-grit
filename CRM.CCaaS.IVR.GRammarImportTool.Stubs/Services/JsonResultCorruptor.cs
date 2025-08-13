using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

public sealed class JsonResultCorruptor : IStubResultCorruptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public (byte[] Body, string ContentType) CorruptIfNeeded(
        byte[] body,
        string contentType,
        Func<bool> shouldCorruptAll,
        Func<(bool shouldCorruptSome, int percentage, int maxPerArray)> someSettings,
        string[]? targetProperties,
        bool produceMalformedJson)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(shouldCorruptAll);
        ArgumentNullException.ThrowIfNull(someSettings);

        if (!IsJson(contentType) || body.Length == 0)
            return (body, contentType);

        var outContentType = contentType;

        if (shouldCorruptAll())
        {
            if (produceMalformedJson)
            {
                return (Encoding.UTF8.GetBytes("{\"error\":\"malformed\""), outContentType);
            }

            var node = ParseJson(body);
            if (node == null) return (body, outContentType);

            var rand = Random.Shared;
            node = CorruptNode(node, targetProperties, corruptAll: true, percentage: 100, maxPerArray: int.MaxValue, rand);
            return (Serialize(node), outContentType);
        }

        var some = someSettings();
        if (some.shouldCorruptSome && some.percentage > 0)
        {
            var node = ParseJson(body);
            if (node == null) return (body, outContentType);

            var rand = Random.Shared;
            node = CorruptNode(node, targetProperties, corruptAll: false, percentage: some.percentage, maxPerArray: some.maxPerArray, rand);
            return (Serialize(node), outContentType);
        }

        return (body, outContentType);
    }

    private static bool IsJson(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonNode? ParseJson(byte[] body)
    {
        try
        {
            return JsonNode.Parse((ReadOnlySpan<byte>)body);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Serialize(JsonNode node)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false });
        node.WriteTo(writer, JsonOptions);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static readonly string[] DefaultTargetProps =
    [
        "id", "name", "status", "value", "text", "message", "description", "result", "results"
    ];

    private static JsonNode CorruptNode(JsonNode node, string[]? targetProps, bool corruptAll, int percentage, int maxPerArray, Random rand)
    {
        targetProps ??= DefaultTargetProps;
        percentage = Math.Clamp(percentage, 0, 100);
        maxPerArray = Math.Max(0, maxPerArray);

        if (node is JsonArray arr)
        {
            if (corruptAll)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] != null)
                        arr[i] = CorruptNode(arr[i]!, targetProps, true, 100, maxPerArray, rand);
                }
                return arr;
            }

            int toCorrupt = (int)Math.Ceiling(arr.Count * (percentage / 100.0));
            toCorrupt = Math.Min(toCorrupt, Math.Min(arr.Count, maxPerArray));
            if (toCorrupt > 0 && arr.Count > 0)
            {
                var indices = Enumerable.Range(0, arr.Count).ToList();
                Shuffle(indices, rand);
                foreach (var idx in indices.Take(toCorrupt))
                {
                    if (arr[idx] != null)
                        arr[idx] = CorruptNode(arr[idx]!, targetProps, true, 100, maxPerArray, rand);
                }
            }
            return arr;
        }

        if (node is JsonObject obj)
        {
            if (corruptAll)
            {
                var keys = obj.Select(kv => kv.Key).ToList();
                foreach (var key in keys)
                {
                    var cur = obj[key];
                    if (cur != null)
                        obj[key] = CorruptNode(cur, targetProps, true, 100, maxPerArray, rand);
                    else
                        obj[key] = JsonValue.Create("@@INVALID@@");
                }
                obj["_corrupted"] = true;
                return obj;
            }

            var properties = obj.Select(kv => kv.Key).ToList();
            if (properties.Count == 0) return obj;

            var targetSet = new HashSet<string>(targetProps, StringComparer.OrdinalIgnoreCase);
            var candidates = properties.Where(targetSet.Contains).ToList();

            var toCorruptCount = Math.Max(1, (int)Math.Ceiling(properties.Count * (percentage / 100.0)));
            toCorruptCount = Math.Min(toCorruptCount, properties.Count);

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (candidates.Count > 0)
            {
                Shuffle(candidates, rand);
                foreach (var c in candidates.Take(toCorruptCount))
                    selected.Add(c);
            }

            if (selected.Count < toCorruptCount)
            {
                var remaining = properties.Where(p => !selected.Contains(p)).ToList();
                Shuffle(remaining, rand);
                foreach (var k in remaining.Take(toCorruptCount - selected.Count))
                    selected.Add(k);
            }

            foreach (var key in selected)
            {
                var cur = obj[key];
                obj[key] = CorruptNode(cur ?? JsonValue.Create((string?)null)!, targetProps, true, 100, maxPerArray, rand);
            }

            obj["_corrupted"] = true;
            return obj;
        }

        return CorruptedValue(node, rand)!;
    }

    private static void Shuffle<T>(IList<T> list, Random rand)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static JsonNode? CorruptedValue(JsonNode? value, Random rand)
    {
        if (value is null) return JsonValue.Create("@@INVALID@@");

        switch (value)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create("!!!invalid:" + s);
            case JsonValue v when v.TryGetValue<int>(out _):
                return JsonValue.Create(-999_999);
            case JsonValue v when v.TryGetValue<long>(out _):
                return JsonValue.Create(-999_999L);
            case JsonValue v when v.TryGetValue<double>(out _):
                return JsonValue.Create(-1234.56);
            case JsonValue v when v.TryGetValue<bool>(out var b):
                return JsonValue.Create(!b);
            case JsonArray a:
                if (a.Count > 0 && rand.NextDouble() < 0.5)
                {
                    a.RemoveAt(rand.Next(a.Count));
                }
                else
                {
                    a.Add(JsonValue.Create("@@DUPLICATE@@"));
                }
                return a;
            case JsonObject o:
                o["_reason"] = "corrupted for negative testing";
                var removeKeys = new[] { "id", "name", "status" };
                foreach (var rk in removeKeys)
                {
                    if (o.ContainsKey(rk)) { o.Remove(rk); break; }
                }
                return o;
            default:
                return JsonValue.Create("@@INVALID@@");
        }
    }
}
