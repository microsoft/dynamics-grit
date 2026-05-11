// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Xml.Linq;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Xunit;
using System.Linq;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;

public class GRXMLSanitizerTest
{
    [Fact]
    public void RemoveUnnecessaryMetadata_RemovesMetaAndGrammarAttributes()
    {
        var xml = @"<grammar version='1.0'><meta name='swirec_compile_parser_with_weights' content='x'/><meta name='keep'/><rule id='r1'> test </rule></grammar>";
        var doc = XDocument.Parse(xml);
        GRXMLSanitizer.SanitizeMetadata(doc);
        var metaNames = doc.Descendants("meta").Select(m => m.Attribute("name")?.Value).ToList();
        Assert.DoesNotContain("swirec_compile_parser_with_weights", metaNames);
        Assert.Contains("keep", metaNames);
        Assert.Empty(doc.Root!.Attributes());
    }

    [Fact]
    public void NormalizeWhitespace_TrimsAndNormalizesTextNodes()
    {
        var xml = @"<root>   text   <child>   more   text   </child>   </root>";
        var doc = XDocument.Parse(xml);
        GRXMLSanitizer.NormalizeElementWhitespace(doc.Root!);
        Assert.Equal("text", doc.Root!.Nodes().OfType<XText>().First().Value);
        Assert.Equal("more text", doc.Root.Element("child")!.Value);
    }

    [Theory]
    [InlineData("   a   b   c   ", "a b c")]
    [InlineData("abc", "abc")]
    [InlineData("   ", "")]
    [InlineData("a\tb\nc", "a b c")]
    public void NormalizeText_NormalizesWhitespace(string input, string expected)
    {
        var result = GRXMLSanitizer.NormalizeText(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MinifyGRXMLContent_RemovesCommentsAndWhitespaceAndMetadata()
    {
        var xml = @"<?xml version='1.0'?><grammar version='1.0'><meta name='swirec_compile_parser_with_weights'/><rule>   test   <!-- comment -->   </rule></grammar>";
        var doc = XDocument.Parse(xml);
        var minified = GRXMLSanitizer.MinifyGRXMLContent(doc);
        Assert.DoesNotContain("comment", minified, StringComparison.Ordinal);
        Assert.DoesNotContain("meta", minified, StringComparison.Ordinal);
        Assert.DoesNotContain("version", minified, StringComparison.Ordinal); // grammar attribute removed
        Assert.Contains("test", minified, StringComparison.Ordinal);
        Assert.DoesNotContain("<?xml", minified, StringComparison.Ordinal);
    }

    [Fact]
    public void MinifyGRXMLContent_RemovesWhitespaceBetweenTags()
    {
        var xml = @"<grammar><rule>   a   </rule>   <rule>   b   </rule></grammar>";
        var doc = XDocument.Parse(xml);
        var minified = GRXMLSanitizer.MinifyGRXMLContent(doc);
        Assert.DoesNotContain(">   <", minified, StringComparison.Ordinal);
        Assert.Contains("a", minified, StringComparison.Ordinal);
        Assert.Contains("b", minified, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveUnnecessaryMetadata_DoesNothingOnNoRoot()
    {
        var doc = new XDocument();
        GRXMLSanitizer.SanitizeMetadata(doc);
        Assert.Null(doc.Root);
    }
}
