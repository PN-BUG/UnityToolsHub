#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>离线 Markdown 阅读视图。支持框架文档使用的常用块；不执行 HTML 或脚本。</summary>
public sealed class FrameworkMarkdownView : IDisposable
{
    private readonly List<Texture2D> textures = new List<Texture2D>();
    private readonly Dictionary<string, VisualElement> anchors = new Dictionary<string, VisualElement>();
    private static readonly Regex Link = new Regex(@"(!?)\[([^\]]*)\]\(([^)]+)\)");
    private string root;
    private string page;
    private ScrollView scroll;
    private Action<string> navigate;
    private bool darkTheme;

    public void Render(ScrollView target, string markdown, string documentRoot, string relativePath, Action<string> onNavigate, bool useDarkTheme = true)
    {
        Dispose();
        target.Clear();
        anchors.Clear();
        scroll = target;
        root = documentRoot;
        page = relativePath;
        navigate = onNavigate;
        darkTheme = useDarkTheme;
        string[] lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```"))
            {
                string fence = new string('`', trimmed.TakeWhile(c => c == '`').Count());
                string language = trimmed.Substring(fence.Length).Trim();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith(fence))
                    code.Add(lines[i]);
                DrawCode(string.Join("\n", code), language);
            }
            else if (trimmed.StartsWith("|"))
            {
                var table = new List<string>();
                do { table.Add(lines[i++]); } while (i < lines.Length && lines[i].TrimStart().StartsWith("|"));
                i--;
                DrawTable(table);
            }
            else if (trimmed.StartsWith("#"))
            {
                int level = trimmed.TakeWhile(c => c == '#').Count();
                string title = trimmed.Substring(level).Trim();
                var label = Paragraph(title);
                label.style.fontSize = level <= 1 ? 25 : level == 2 ? 20 : 16;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.marginTop = 14;
                anchors[Slug(title)] = label;
            }
            else if (trimmed == "---" || trimmed == "***")
            {
                var divider = new VisualElement();
                divider.style.height = 1;
                divider.style.backgroundColor = new Color(.45f, .45f, .45f, .4f);
                divider.style.marginTop = divider.style.marginBottom = 10;
                scroll.Add(divider);
            }
            else
            {
                var paragraph = new List<string> { trimmed };
                if (!IsBlock(trimmed))
                    while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]) && !IsBlock(lines[i + 1].TrimStart()))
                        paragraph.Add(lines[++i].Trim());
                DrawInline(string.Join("\n", paragraph));
            }
        }
    }

    private static bool IsBlock(string text) => text.StartsWith("#") || text.StartsWith("|") || text.StartsWith("```")
        || text.StartsWith("- ") || text.StartsWith("* ") || text.StartsWith(">") || text.StartsWith("![")
        || Regex.IsMatch(text, @"^\d+\. ");

    private Label Paragraph(string text)
    {
        var label = new Label(Format(text));
        label.enableRichText = true;
        label.selection.isSelectable = true;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 14;
        label.style.marginBottom = 8;
        label.style.flexShrink = 0;
        scroll.Add(label);
        return label;
    }

    private string Format(string text)
    {
        text = text.Replace("<br>", "\n").Replace("<br/>", "\n");
        // TextCore 不按 HTML 解码 &lt;/&gt;。使用 noparse 保留源码中的尖括号。
        text = text.Replace("<", "<noparse><</noparse>");
        text = Regex.Replace(text, @"`([^`]+)`", darkTheme ? "<color=#69AADF>$1</color>" : "<color=#1E5F93>$1</color>");
        return Regex.Replace(text, @"\*\*([^*]+)\*\*", "<b>$1</b>");
    }

    private void DrawInline(string line)
    {
        bool quote = line.StartsWith("> ");
        if (quote)
            line = line.Substring(2);
        var matches = Link.Matches(line);
        if (matches.Count == 0)
        {
            var label = Paragraph(line);
            if (quote)
            {
                label.style.borderLeftWidth = 3;
                label.style.borderLeftColor = new Color(.45f, .65f, .85f);
                label.style.paddingLeft = 12;
                label.style.opacity = .85f;
            }
            return;
        }
        int offset = 0;
        foreach (Match match in matches)
        {
            if (match.Index > offset && !string.IsNullOrWhiteSpace(line.Substring(offset, match.Index - offset)))
                Paragraph(line.Substring(offset, match.Index - offset));
            string url = match.Groups[3].Value.Trim('<', '>');
            string title = match.Groups[2].Value;
            if (match.Groups[1].Value == "!")
                DrawImage(title, url);
            else
            {
                var button = new Button(() => Follow(url)) { text = title, tooltip = Uri.UnescapeDataString(url) };
                button.style.alignSelf = Align.FlexStart;
                button.style.marginBottom = 7;
                button.style.whiteSpace = WhiteSpace.Normal;
                scroll.Add(button);
            }
            offset = match.Index + match.Length;
        }
        if (offset < line.Length && !string.IsNullOrWhiteSpace(line.Substring(offset)))
            Paragraph(line.Substring(offset));
    }

    private void DrawCode(string text, string language)
    {
        var container = new VisualElement();
        container.style.backgroundColor = darkTheme ? new Color(.1f, .1f, .1f, .35f) : new Color(.91f, .93f, .95f);
        container.style.paddingLeft = container.style.paddingRight = 10;
        container.style.paddingTop = container.style.paddingBottom = 8;
        container.style.marginBottom = 12;
        var copy = new Button(() => EditorGUIUtility.systemCopyBuffer = text) { text = string.IsNullOrEmpty(language) ? "复制代码" : "复制 " + language };
        copy.style.alignSelf = Align.FlexEnd;
        container.Add(copy);
        var horizontal = new ScrollView(ScrollViewMode.Horizontal);
        var code = new Label(text) { enableRichText = false };
        code.selection.isSelectable = true;
        code.style.fontSize = 13;
        code.style.whiteSpace = WhiteSpace.NoWrap;
        horizontal.Add(code);
        container.Add(horizontal);
        scroll.Add(container);
    }

    private void DrawTable(List<string> lines)
    {
        var horizontal = new ScrollView(ScrollViewMode.Horizontal);
        var table = new VisualElement();
        int rowIndex = 0;
        foreach (string line in lines)
        {
            if (Regex.IsMatch(line.Trim(), @"^\|?[\s:|\-]+\|?$"))
                continue;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            if (rowIndex % 2 == 0)
                row.style.backgroundColor = new Color(.5f, .5f, .5f, .1f);
            foreach (string cell in Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|"))
            {
                var label = new Label(Format(Link.Replace(cell.Trim().Replace("\\|", "|"), "$2")));
                label.selection.isSelectable = true;
                label.style.width = 240;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.paddingLeft = label.style.paddingRight = 8;
                label.style.paddingTop = label.style.paddingBottom = 6;
                label.style.borderBottomWidth = 1;
                label.style.borderBottomColor = new Color(.5f, .5f, .5f, .3f);
                if (rowIndex == 0)
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);
            }
            table.Add(row);
            rowIndex++;
        }
        horizontal.Add(table);
        horizontal.style.marginBottom = 12;
        scroll.Add(horizontal);
        // 图片与链接可能位于单元格内；在表后依原序展示，避免只剩 alt 文本。
        foreach (Match match in Link.Matches(string.Join("\n", lines)))
            DrawInline(match.Value);
    }

    private string LocalPath(string url)
    {
        string path = Uri.UnescapeDataString(url.Split('#')[0]);
        string relative = Path.Combine(Path.GetDirectoryName(page) ?? "", path);
        return FrameworkDocumentation.ResolvePath(root, relative, false);
    }

    private void DrawImage(string title, string url)
    {
        try
        {
            string path = LocalPath(url);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                var open = new Button(() => EditorUtility.OpenWithDefaultApp(path)) { text = "打开图片：" + title + "（" + extension + "）" };
                open.style.alignSelf = Align.FlexStart;
                open.style.marginBottom = 12;
                scroll.Add(open);
                return;
            }
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            textures.Add(texture);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                throw new IOException("无法解码图片");
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, tooltip = title + "（点击查看原图）" };
            image.style.height = Mathf.Min(texture.height, 420);
            image.style.maxWidth = texture.width;
            image.style.marginBottom = 12;
            image.RegisterCallback<ClickEvent>(_ => EditorUtility.OpenWithDefaultApp(path));
            scroll.Add(image);
        }
        catch (Exception ex)
        {
            Paragraph("图片未显示：" + title + " — " + ex.Message);
        }
    }

    public void Follow(string url)
    {
        try
        {
            if (url.StartsWith("#"))
            {
                if (anchors.TryGetValue(Uri.UnescapeDataString(url.Substring(1)), out var heading))
                    scroll.ScrollTo(heading);
                return;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "mailto")
                    Application.OpenURL(url);
                return;
            }
            string path = LocalPath(url);
            if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                string relative = path.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                navigate(relative);
            }
            else if (File.Exists(path))
                EditorUtility.OpenWithDefaultApp(path);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("无法打开链接", ex.Message, "确定");
        }
    }

    private static string Slug(string title) => Regex.Replace(title.ToLowerInvariant(), @"[^\p{L}\p{N}\s_-]", "").Replace(' ', '-');

    public void Dispose()
    {
        foreach (var texture in textures)
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        textures.Clear();
    }
}
#endif
