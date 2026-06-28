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
}
