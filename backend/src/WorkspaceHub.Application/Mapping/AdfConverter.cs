using System.Text;
using System.Text.Json;

namespace WorkspaceHub.Application.Mapping;

/// <summary>
/// Chuyển đổi 2 chiều giữa ADF (Atlassian Document Format — description của Jira) và plain text.
/// ADF là cây JSON: node gốc có "content"[], mỗi node có "type" + "text"/"content".
/// Đọc: ADF → text (Snippet/hiển thị). Ghi: text → ADF tối giản (mỗi dòng = 1 paragraph) trước khi gửi Jira.
/// Tham khảo: https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/
/// </summary>
public static class AdfConverter
{
    /// <summary>
    /// Build document ADF tối giản từ plain text: mỗi dòng (\n) thành 1 paragraph.
    /// Dòng rỗng vẫn giữ paragraph rỗng để bảo toàn khoảng cách. Trả null nếu text null/rỗng.
    /// </summary>
    public static object? FromPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var paragraphs = lines.Select(line => string.IsNullOrEmpty(line)
            ? new { type = "paragraph", content = Array.Empty<object>() }
            : new { type = "paragraph", content = new object[] { new { type = "text", text = line } } });

        return new
        {
            type = "doc",
            version = 1,
            content = paragraphs.ToArray()
        };
    }

    /// <summary>Trích plain text từ document ADF. Trả chuỗi rỗng nếu null/không parse được.</summary>
    public static string ToPlainText(JsonElement? adf)
    {
        if (adf is not { } root || root.ValueKind != JsonValueKind.Object)
            return string.Empty;

        var sb = new StringBuilder();
        WalkNode(root, sb);
        return sb.ToString().Trim();
    }

    private static void WalkNode(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        var type = node.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString()
            : null;

        // Leaf text node
        if (type == "text" && node.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
        {
            sb.Append(textEl.GetString());
            return;
        }

        // "hardBreak" → newline; "mention" → @name; "emoji" → text fallback
        switch (type)
        {
            case "hardBreak":
                sb.Append('\n');
                return;
            case "mention" when node.TryGetProperty("attrs", out var mAttrs)
                                && mAttrs.TryGetProperty("text", out var mText)
                                && mText.ValueKind == JsonValueKind.String:
                sb.Append(mText.GetString());
                return;
        }

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
                WalkNode(child, sb);
        }

        // Block-level node (paragraph/heading/listItem...) → xuống dòng sau khi duyệt xong content
        if (IsBlockNode(type))
            sb.Append('\n');
    }

    private static bool IsBlockNode(string? type) => type is
        "paragraph" or "heading" or "listItem" or "blockquote"
        or "codeBlock" or "rule" or "panel";
}
