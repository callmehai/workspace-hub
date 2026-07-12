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

    /// <summary>
    /// Như <see cref="FromPlainText"/> nhưng text rỗng → ADF doc RỖNG (content: []) thay vì null.
    /// Dùng khi set field description: gửi empty doc để XOÁ nội dung trên Jira (không gửi paragraph " ").
    /// </summary>
    public static object FromPlainTextOrEmptyDoc(string? text) =>
        FromPlainText(text) ?? new { type = "doc", version = 1, content = Array.Empty<object>() };

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

        // "hardBreak" → newline; "mention" → @name; "emoji" → text/shortName fallback
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
            case "emoji" when node.TryGetProperty("attrs", out var eAttrs):
                // Ưu tiên "text" (ký tự emoji thật), fallback "shortName" (vd :smile:).
                if (eAttrs.TryGetProperty("text", out var eText) && eText.ValueKind == JsonValueKind.String)
                    sb.Append(eText.GetString());
                else if (eAttrs.TryGetProperty("shortName", out var eShort) && eShort.ValueKind == JsonValueKind.String)
                    sb.Append(eShort.GetString());
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
        or "codeBlock" or "rule" or "panel"
        or "bulletList" or "orderedList";

    // ═══════════════════ Markdown subset ⇄ ADF (comment rich + description) ═══════════════════
    // Subset hỗ trợ: **đậm**, *nghiêng* / _nghiêng_, ~~gạch~~, `code`, [text](url),
    //   bullet list "- ", ordered list "1. ", heading "#"→"######", code block ``` fenced.
    // Attachment trong comment: KHÔNG dùng ADF media node (Jira validate attachment id → ATTACHMENT_VALIDATION_ERROR).
    //   Thay bằng marker TEXT `[[attach:ID]]` (Jira lưu như text; FE render thành chip tải file).

    /// <summary>
    /// Markdown subset → ADF doc. `mediaIds` (nếu có) được nối cuối body dạng marker text `[[attach:ID]]`
    /// (mỗi id 1 dòng). Rỗng cả 2 → doc rỗng (content:[]).
    /// </summary>
    public static object FromMarkdown(string? markdown, IEnumerable<string>? mediaIds = null)
    {
        var content = new List<object>();
        var text = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        // Nối marker attachment (dạng text) vào cuối body — giữ nguyên marker sẵn có trong text (round-trip khi sửa).
        var ids = mediaIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (ids is { Count: > 0 })
        {
            var markers = string.Join("\n", ids.Select(id => $"[[attach:{id}]]"));
            text = text.Length == 0 ? markers : text + "\n" + markers;
        }

        var lines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            // Fenced code block ``` ... ``` → codeBlock node (giữ nguyên text bên trong, không parse inline).
            if (line.TrimStart().StartsWith("```"))
            {
                var codeLines = new List<string>();
                i++; // bỏ dòng mở ```
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```")) { codeLines.Add(lines[i]); i++; }
                if (i < lines.Length) i++; // bỏ dòng đóng ```
                content.Add(new
                {
                    type = "codeBlock",
                    content = codeLines.Count == 0
                        ? Array.Empty<object>()
                        : new object[] { new { type = "text", text = string.Join("\n", codeLines) } }
                });
                continue;
            }

            // Heading "# " → "###### " → heading node level 1-6.
            var heading = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                var hInline = ParseInline(heading.Groups[2].Value);
                content.Add(new
                {
                    type = "heading",
                    attrs = new { level = heading.Groups[1].Value.Length },
                    content = hInline.Count == 0 ? Array.Empty<object>() : hInline.ToArray()
                });
                i++;
                continue;
            }

            var bullet = System.Text.RegularExpressions.Regex.Match(line, @"^\s*[-*]\s+(.*)$");
            var ordered = System.Text.RegularExpressions.Regex.Match(line, @"^\s*\d+\.\s+(.*)$");

            if (bullet.Success)
            {
                var items = new List<object>();
                while (i < lines.Length && (bullet = System.Text.RegularExpressions.Regex.Match(lines[i], @"^\s*[-*]\s+(.*)$")).Success)
                {
                    items.Add(ListItem(bullet.Groups[1].Value));
                    i++;
                }
                content.Add(new { type = "bulletList", content = items.ToArray() });
                continue;
            }
            if (ordered.Success)
            {
                var items = new List<object>();
                while (i < lines.Length && (ordered = System.Text.RegularExpressions.Regex.Match(lines[i], @"^\s*\d+\.\s+(.*)$")).Success)
                {
                    items.Add(ListItem(ordered.Groups[1].Value));
                    i++;
                }
                content.Add(new { type = "orderedList", content = items.ToArray() });
                continue;
            }

            // Dòng thường → paragraph (rỗng vẫn giữ để bảo toàn khoảng cách).
            var inline = ParseInline(line);
            content.Add(inline.Count == 0
                ? new { type = "paragraph", content = Array.Empty<object>() }
                : new { type = "paragraph", content = inline.ToArray() });
            i++;
        }

        return new { type = "doc", version = 1, content = content.ToArray() };
    }

    private static object ListItem(string lineText)
    {
        var inline = ParseInline(lineText);
        return new
        {
            type = "listItem",
            content = new object[]
            {
                inline.Count == 0
                    ? new { type = "paragraph", content = Array.Empty<object>() }
                    : new { type = "paragraph", content = inline.ToArray() }
            }
        };
    }

    /// <summary>Parse inline markdown 1 dòng → list ADF text node (marks: strong/em/strike/link). Không lồng nhau.</summary>
    private static List<object> ParseInline(string text)
    {
        var nodes = new List<object>();
        var buf = new StringBuilder();

        void Flush()
        {
            if (buf.Length > 0) { nodes.Add(new { type = "text", text = buf.ToString() }); buf.Clear(); }
        }
        void AddMarked(string content, string markType)
        {
            Flush();
            if (content.Length > 0)
                nodes.Add(new { type = "text", text = content, marks = new object[] { new { type = markType } } });
        }

        int i = 0;
        while (i < text.Length)
        {
            // Link [text](url)
            var link = System.Text.RegularExpressions.Regex.Match(text[i..], @"^\[([^\]]+)\]\(([^)\s]+)\)");
            if (link.Success)
            {
                Flush();
                nodes.Add(new
                {
                    type = "text",
                    text = link.Groups[1].Value,
                    marks = new object[] { new { type = "link", attrs = new { href = link.Groups[2].Value } } }
                });
                i += link.Length;
                continue;
            }
            // `code` inline — parse TRƯỚC các mark khác để nội dung trong backtick giữ nguyên ký tự * _ ~.
            if (text[i] == '`' && Closing(text, i + 1, "`") is int c && c > 0)
            {
                AddMarked(text.Substring(i + 1, c - (i + 1)), "code"); i = c + 1; continue;
            }
            if (Starts(text, i, "**") && Closing(text, i + 2, "**") is int b && b > 0)
            {
                AddMarked(text.Substring(i + 2, b - (i + 2)), "strong"); i = b + 2; continue;
            }
            if (Starts(text, i, "~~") && Closing(text, i + 2, "~~") is int s && s > 0)
            {
                AddMarked(text.Substring(i + 2, s - (i + 2)), "strike"); i = s + 2; continue;
            }
            if ((text[i] == '*' || text[i] == '_') && Closing(text, i + 1, text[i].ToString()) is int e && e > 0)
            {
                AddMarked(text.Substring(i + 1, e - (i + 1)), "em"); i = e + 1; continue;
            }
            buf.Append(text[i]); i++;
        }
        Flush();
        return nodes;
    }

    private static bool Starts(string s, int i, string token) =>
        i + token.Length <= s.Length && s.Substring(i, token.Length) == token;

    /// <summary>Vị trí bắt đầu token đóng gần nhất kể từ `from` (nội dung giữa không rỗng), hoặc -1.</summary>
    private static int Closing(string s, int from, string token)
    {
        var idx = s.IndexOf(token, from, StringComparison.Ordinal);
        return idx > from ? idx : -1;
    }

    /// <summary>ADF → Markdown subset (đọc comment). Media node → marker `[[attach:{id}]]` (FE resolve tên file).</summary>
    public static string ToMarkdown(JsonElement? adf)
    {
        if (adf is not { } root || root.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var sb = new StringBuilder();
        WalkMarkdown(root, sb);
        return sb.ToString().Trim('\n');
    }

    private static void WalkMarkdown(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        var type = node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        switch (type)
        {
            case "text":
                var txt = node.TryGetProperty("text", out var te) && te.ValueKind == JsonValueKind.String ? te.GetString() ?? "" : "";
                sb.Append(ApplyMarks(txt, node));
                return;
            case "hardBreak":
                sb.Append('\n');
                return;
            case "media":
                if (node.TryGetProperty("attrs", out var ma) && ma.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String)
                    sb.Append($"[[attach:{mid.GetString()}]]");
                return;
            case "mention" when node.TryGetProperty("attrs", out var mn) && mn.TryGetProperty("text", out var mnt) && mnt.ValueKind == JsonValueKind.String:
                sb.Append(mnt.GetString());
                return;
            case "codeBlock":
            {
                // Fenced block — text bên trong giữ nguyên (không marks).
                var inner = new StringBuilder();
                if (node.TryGetProperty("content", out var cbc) && cbc.ValueKind == JsonValueKind.Array)
                    foreach (var child in cbc.EnumerateArray()) WalkMarkdown(child, inner);
                sb.Append("```\n").Append(inner.ToString().TrimEnd('\n')).Append("\n```\n");
                return;
            }
            case "heading":
            {
                // "#"×level + space — FE render heading, round-trip về ADF heading khi sửa.
                var level = node.TryGetProperty("attrs", out var ha) && ha.TryGetProperty("level", out var hl) && hl.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(hl.GetInt32(), 1, 6) : 2;
                sb.Append(new string('#', level)).Append(' ');
                break; // rơi xuống duyệt content như block thường
            }
        }

        var prefix = type switch { "bulletList" => "- ", "orderedList" => "1. ", _ => null };
        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                if (prefix != null && child.TryGetProperty("type", out var ct) && ct.GetString() == "listItem")
                {
                    sb.Append(prefix);
                    WalkMarkdown(child, sb);
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                }
                else WalkMarkdown(child, sb);
            }
        }

        if (type is "paragraph" or "heading" or "listItem" or "mediaSingle")
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
        }
    }

    private static string ApplyMarks(string text, JsonElement node)
    {
        if (text.Length == 0 || !node.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array)
            return text;

        string? href = null;
        bool strong = false, em = false, strike = false, code = false;
        foreach (var m in marks.EnumerateArray())
        {
            var mt = m.TryGetProperty("type", out var mte) && mte.ValueKind == JsonValueKind.String ? mte.GetString() : null;
            switch (mt)
            {
                case "strong": strong = true; break;
                case "em": em = true; break;
                case "strike": strike = true; break;
                case "code": code = true; break;
                case "link" when m.TryGetProperty("attrs", out var la) && la.TryGetProperty("href", out var lh) && lh.ValueKind == JsonValueKind.String:
                    href = lh.GetString(); break;
            }
        }
        if (code) return href != null ? $"[`{text}`]({href})" : $"`{text}`"; // code loại trừ mark trang trí khác
        if (strong) text = $"**{text}**";
        if (em) text = $"*{text}*";
        if (strike) text = $"~~{text}~~";
        if (href != null) text = $"[{text}]({href})";
        return text;
    }
}
