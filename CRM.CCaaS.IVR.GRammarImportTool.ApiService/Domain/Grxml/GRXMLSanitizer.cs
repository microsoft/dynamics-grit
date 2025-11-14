// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;

public static partial class GRXMLSanitizer
{
    public static void SanitizeMetadata(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var root = doc.Root;
        if (root is null)
            return;

        var metaElements = root.Descendants("meta").ToList();
        foreach (var meta in metaElements)
        {
            var nameAttr = meta.Attribute("name")?.Value;
            if (nameAttr == "swirec_compile_parser_with_weights")
            {
                meta.Remove();
            }
        }

        if (root.Name.LocalName == "grammar")
        {
            root.RemoveAttributes();
        }

        NormalizeElementWhitespace(root);
    }

    public static void NormalizeElementWhitespace(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        foreach (var child in element.Elements())
        {
            NormalizeElementWhitespace(child);
        }

        foreach (var textNode in element.Nodes().OfType<XText>())
        {
            string text = textNode.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                textNode.Remove();
            }
            else
            {
                string normalized = NormalizeText(text);
                if (!string.Equals(normalized, text, StringComparison.Ordinal))
                {
                    textNode.Value = normalized;
                }
            }
        }
    }

    public static string NormalizeText(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new System.Text.StringBuilder(input.Length);
        bool inWhitespace = false;

        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                inWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    public static string MinifyGRXMLContent(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        SanitizeMetadata(doc);
        RemoveXmlDeclaration(doc);
        RemoveComments(doc);
        string rawXml = doc.ToString(SaveOptions.DisableFormatting);
        string cleanedXml = RemoveMalformedComments(rawXml);
        cleanedXml = RemoveWhitespaceBetweenTags(cleanedXml);
        cleanedXml = NormalizeText(cleanedXml);
        return cleanedXml.Trim();
    }

    private static void RemoveXmlDeclaration(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        doc.Declaration = null;
    }

    private static void RemoveComments(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var comments = doc.DescendantNodes().OfType<XComment>().ToList();
        foreach (var comment in comments)
        {
            comment.Remove();
        }
    }

    private static string RemoveMalformedComments(string xmlString)
    {
        ArgumentNullException.ThrowIfNull(xmlString);

        return RemoveMalformedCommentsRegex().Replace(xmlString, string.Empty);
    }

    private static string RemoveWhitespaceBetweenTags(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return RemoveWhitespaceBetweenTagsRegex().Replace(xml, "><");
    }

    [GeneratedRegex(@">\s+<")]
    private static partial Regex RemoveWhitespaceBetweenTagsRegex();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex RemoveMalformedCommentsRegex();
}
