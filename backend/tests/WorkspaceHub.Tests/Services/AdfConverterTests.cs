using System.Text.Json;
using FluentAssertions;
using WorkspaceHub.Application.Mapping;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class AdfConverterTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ToPlainText_Null_ReturnsEmpty()
    {
        AdfConverter.ToPlainText(null).Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_NonObject_ReturnsEmpty()
    {
        AdfConverter.ToPlainText(Parse("\"just a string\"")).Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_SimpleParagraph_ExtractsText()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "version": 1,
          "content": [
            { "type": "paragraph", "content": [ { "type": "text", "text": "Hello world" } ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be("Hello world");
    }

    [Fact]
    public void ToPlainText_MultipleParagraphs_SeparatedByNewline()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [ { "type": "text", "text": "Line one" } ] },
            { "type": "paragraph", "content": [ { "type": "text", "text": "Line two" } ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be("Line one\nLine two");
    }

    [Fact]
    public void ToPlainText_HardBreak_BecomesNewline()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [
              { "type": "text", "text": "a" },
              { "type": "hardBreak" },
              { "type": "text", "text": "b" }
            ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be("a\nb");
    }

    [Fact]
    public void ToPlainText_Mention_UsesAttrsText()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [
              { "type": "text", "text": "cc " },
              { "type": "mention", "attrs": { "id": "123", "text": "@Loc" } }
            ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be("cc @Loc");
    }

    [Fact]
    public void ToPlainText_NestedListItems_ExtractsAllText()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "bulletList", "content": [
              { "type": "listItem", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "first" } ] } ] },
              { "type": "listItem", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "second" } ] } ] }
            ] }
          ]
        }
        """);

        var result = AdfConverter.ToPlainText(adf);
        result.Should().Contain("first");
        result.Should().Contain("second");
    }

    [Fact]
    public void ToPlainText_EmptyDoc_ReturnsEmpty()
    {
        AdfConverter.ToPlainText(Parse("""{ "type": "doc", "content": [] }""")).Should().BeEmpty();
    }

    // ───────────────────────── FromPlainText (SCRUM-56) ─────────────────────────

    [Fact]
    public void FromPlainText_Null_ReturnsNull()
    {
        AdfConverter.FromPlainText(null).Should().BeNull();
    }

    [Fact]
    public void FromPlainText_Empty_ReturnsNull()
    {
        AdfConverter.FromPlainText(string.Empty).Should().BeNull();
    }

    [Fact]
    public void FromPlainText_SingleLine_ProducesDocWithOneParagraph()
    {
        var adf = AdfConverter.FromPlainText("Hello");

        // Serialize → parse để kiểm tra cấu trúc ADF.
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;
        el.GetProperty("type").GetString().Should().Be("doc");
        el.GetProperty("version").GetInt32().Should().Be(1);

        var content = el.GetProperty("content");
        content.GetArrayLength().Should().Be(1);

        var paragraph = content[0];
        paragraph.GetProperty("type").GetString().Should().Be("paragraph");
        paragraph.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello");
    }

    [Fact]
    public void FromPlainText_MultiLine_ProducesParagraphPerLine()
    {
        var adf = AdfConverter.FromPlainText("line1\nline2");
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;

        var content = el.GetProperty("content");
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("line1");
        content[1].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("line2");
    }

    [Fact]
    public void FromPlainText_EmptyLine_ProducesEmptyParagraph()
    {
        var adf = AdfConverter.FromPlainText("a\n\nb");
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;

        var content = el.GetProperty("content");
        content.GetArrayLength().Should().Be(3);
        content[1].GetProperty("content").GetArrayLength().Should().Be(0); // paragraph rỗng
    }

    [Fact]
    public void FromPlainText_RoundTrip_PreservesText()
    {
        const string original = "first line\nsecond line";
        var adf = AdfConverter.FromPlainText(original);
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;

        AdfConverter.ToPlainText(el).Should().Be(original);
    }
}
