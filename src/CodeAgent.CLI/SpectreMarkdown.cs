using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodeAgent.CLI;

public static class SpectreMarkdown
{
    // 将 Markdown 纯文本转换为 Spectre.Console 的 Markup 标记
    public static string Convert(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var document = Markdown.Parse(markdown);
        var sb = new StringBuilder();

        foreach (var node in document)
        {
            RenderBlock(node, sb);
        }

        return sb.ToString();
    }

    private static void RenderBlock(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case HeadingBlock heading:
                // 一级标题黄色加粗，二级蓝色加粗
                var color = heading.Level switch
                {
                    1 => "bold yellow",
                    2 => "bold blue",
                    3 => "bold red",
                    _ => "bold white"
                };
                sb.AppendLine($"[{color}]{RenderInlines(heading.Inline)}[/]");
                break;

            case ParagraphBlock paragraph:
                sb.AppendLine(RenderInlines(paragraph.Inline));
                break;
            case ListBlock listBlock:
                // 修复：正确处理列表块，而不是引用不存在的 paragraph 变量
                foreach (var item in listBlock)
                {
                    if (item is ListItemBlock listItem)
                    {
                        // 简单的列表项处理，通常 ListItemBlock 包含 ParagraphBlock
                        foreach (var subNode in listItem)
                        {
                            if (subNode is ParagraphBlock p)
                            {
                                sb.AppendLine($"• {RenderInlines(p.Inline)}");
                            }
                        }
                    }
                }
                break;
            case FencedCodeBlock codeBlock:
                // 提取代码文本，用灰色背景包裹模拟代码块
                var code = ExtractCode(codeBlock);
                sb.AppendLine($"[grey on black]{EscapeMarkup(code)}[/]");
                break;

            case ThematicBreakBlock:
                sb.AppendLine("[grey]----------------------------------------[/]");
                break;
        }
    }

    private static string RenderInlines(ContainerInline? inlines)
    {
        if (inlines == null) return string.Empty;
        var sb = new StringBuilder();

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(EscapeMarkup(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    // 两个星号是加粗，一个是斜体
                    var tag = emphasis.DelimiterCount == 2 ? "bold" : "italic";
                    sb.Append($"[{tag}]{RenderInlines(emphasis)}[/]");
                    break;

                case CodeInline code:
                    // 行内代码，加灰色背景
                    sb.Append($"[grey on black] {EscapeMarkup(code.Content)} [/]");
                    break;

                case LineBreakInline:
                    sb.AppendLine();
                    break;

                case ContainerInline container:
                    sb.Append(RenderInlines(container));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ExtractCode(FencedCodeBlock codeBlock)
    {
        if (codeBlock.Lines.Lines==null)
        {
            return "";
        }
        var lines = codeBlock.Lines.Lines;
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            sb.AppendLine(lines[i].ToString());
        }
        return sb.ToString().TrimEnd();
    }

    // Spectre.Console 的特殊字符是 [ 和 ]，必须转义
    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
