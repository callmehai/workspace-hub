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

    // ───────────────────── FromPlainTextOrEmptyDoc (clear description — review #4) ─────────────────────

    [Fact]
    public void FromPlainTextOrEmptyDoc_Empty_ReturnsEmptyDoc_NotSpaceParagraph()
    {
        var adf = AdfConverter.FromPlainTextOrEmptyDoc("");
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;

        el.GetProperty("type").GetString().Should().Be("doc");
        el.GetProperty("content").GetArrayLength().Should().Be(0); // doc rỗng → xoá description, không phải paragraph " "
    }

    [Fact]
    public void FromPlainTextOrEmptyDoc_Null_ReturnsEmptyDoc()
    {
        var adf = AdfConverter.FromPlainTextOrEmptyDoc(null);
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;
        el.GetProperty("content").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void FromPlainTextOrEmptyDoc_WithText_ProducesParagraph()
    {
        var adf = AdfConverter.FromPlainTextOrEmptyDoc("hello");
        var el = JsonDocument.Parse(JsonSerializer.Serialize(adf)).RootElement;
        el.GetProperty("content")[0].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("hello");
    }

    // ───────────────────── Emoji + list (review #8) ─────────────────────

    [Fact]
    public void ToPlainText_Emoji_UsesTextThenShortName()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [
              { "type": "text", "text": "nice " },
              { "type": "emoji", "attrs": { "shortName": ":smile:", "text": "😄" } }
            ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be("nice 😄");
    }

    [Fact]
    public void ToPlainText_Emoji_NoText_FallsBackToShortName()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [ { "type": "emoji", "attrs": { "shortName": ":fire:" } } ] }
          ]
        }
        """);

        AdfConverter.ToPlainText(adf).Should().Be(":fire:");
    }

    [Fact]
    public void ToPlainText_BulletList_SeparatesFromSurroundingText()
    {
        var adf = Parse("""
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [ { "type": "text", "text": "intro" } ] },
            { "type": "bulletList", "content": [
              { "type": "listItem", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "item1" } ] } ] }
            ] },
            { "type": "paragraph", "content": [ { "type": "text", "text": "outro" } ] }
          ]
        }
        """);

        var result = AdfConverter.ToPlainText(adf);
        // bulletList giờ là block node → có separator; intro/item1/outro không bị dính liền.
        result.Should().Contain("intro");
        result.Should().Contain("item1");
        result.Should().Contain("outro");
        result.Should().NotContain("introitem1");
        result.Should().NotContain("item1outro");
    }

    // ── Markdown subset ⇄ ADF (comment rich) ──
    private static string RoundTrip(string md, params string[] media)
    {
        var adf = AdfConverter.FromMarkdown(md, media.Length == 0 ? null : media);
        var json = JsonSerializer.Serialize(adf);
        return AdfConverter.ToMarkdown(Parse(json));
    }

    [Theory]
    [InlineData("**bold**")]
    [InlineData("*italic*")]
    [InlineData("~~strike~~")]
    [InlineData("plain text line")]
    public void Markdown_InlineMarks_RoundTrip(string md)
    {
        RoundTrip(md).Should().Be(md);
    }

    [Fact]
    public void Markdown_Underscore_Italic_NormalizesToStar()
    {
        // _x_ và *x* đều là em → ToMarkdown chuẩn hoá về *x*.
        RoundTrip("_italic_").Should().Be("*italic*");
    }

    [Fact]
    public void Markdown_Link_RoundTrip()
    {
        RoundTrip("[Google](https://google.com)").Should().Be("[Google](https://google.com)");
    }

    [Fact]
    public void Markdown_BulletList_RoundTrip()
    {
        RoundTrip("- one\n- two").Should().Be("- one\n- two");
    }

    [Fact]
    public void Markdown_OrderedList_RoundTrip()
    {
        RoundTrip("1. one\n2. two").Should().Be("1. one\n1. two"); // ToMarkdown luôn phát "1. "
    }

    [Fact]
    public void Markdown_MediaIds_EmitAttachMarker()
    {
        var result = RoundTrip("see file", "att-123");
        result.Should().Contain("see file");
        result.Should().Contain("[[attach:att-123]]");
    }

    [Fact]
    public void Markdown_AttachMarkerInText_StaysAsText()
    {
        // Marker [[attach:ID]] được giữ nguyên dạng text (không convert media node) → round-trip ổn định.
        var result = RoundTrip("hello [[attach:xyz]] world");
        result.Should().Contain("hello");
        result.Should().Contain("world");
        result.Should().Contain("[[attach:xyz]]");
    }

    [Fact]
    public void Markdown_Empty_ProducesEmptyDoc()
    {
        RoundTrip("").Should().BeEmpty();
    }
}
