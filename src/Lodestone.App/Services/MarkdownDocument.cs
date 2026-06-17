using Markdig;

namespace Lodestone.App.Services;

/// <summary>
/// Converts a Modrinth/CurseForge description (Markdown that may embed raw HTML) into a complete,
/// dark-themed HTML document for <see cref="Lodestone.App.Controls.MarkdownWebView"/>. The document
/// is rendered with JavaScript disabled and a strict Content-Security-Policy, so untrusted markup
/// can only paint text, styles and images — never execute script or load active content. The CSS
/// mirrors the app theme and constrains media to the column width so nothing spills off the edge.
/// </summary>
public static class MarkdownDocument
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions() // GFM-style: pipe/grid tables, autolinks, task lists, footnotes, etc.
        .Build();

    /// <param name="accentHex">The accent as an opaque CSS hex (e.g. "#5AC26D"), so description links match
    /// the chosen accent. Resolve it from <see cref="AccentApplier.CurrentAccentHex"/> at the call site.</param>
    public static string ToHtml(string? markdown, string accentHex = "#5AC26D")
    {
        string body = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        return Wrap(body, accentHex);
    }

    private static string Wrap(string body, string accentHex) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: data:; media-src https: http: data:; style-src 'unsafe-inline'; font-src data:;">
        <style>
          :root { color-scheme: dark; }
          html, body { margin: 0; padding: 0; }
          body {
            padding: 2px 6px 12px; background: #222227; color: #EDEDF0;
            font-family: 'Segoe UI Variable Display', 'Segoe UI', system-ui, sans-serif;
            font-size: 14px; line-height: 1.6; overflow-wrap: break-word; word-break: break-word;
          }
          a { color: {{accentHex}}; text-decoration: none; }
          a:hover { text-decoration: underline; }
          h1, h2, h3, h4, h5, h6 { color: #F2F2F4; font-weight: 600; line-height: 1.25; margin: 1.1em 0 .5em; }
          h1 { font-size: 1.5em; } h2 { font-size: 1.3em; } h3 { font-size: 1.12em; }
          h1, h2 { padding-bottom: .25em; border-bottom: 1px solid #FFFFFF14; }
          p, ul, ol, blockquote, table, pre, details { margin: 0 0 .85em; }
          body > :first-child { margin-top: 0; }
          body > :last-child { margin-bottom: 0; }
          img, video { max-width: 100%; height: auto; border-radius: 8px; }
          center, [align="center"] { text-align: center; }
          ul, ol { padding-left: 1.4em; }
          li { margin: .2em 0; }
          code { font-family: 'Cascadia Mono', Consolas, ui-monospace, monospace; font-size: .88em; background: #2A2A30; padding: .12em .38em; border-radius: 5px; }
          pre { background: #17171A; padding: 12px 14px; border-radius: 9px; overflow: auto; }
          pre code { background: none; padding: 0; font-size: .86em; }
          blockquote { padding: .2em 1em; border-left: 3px solid {{accentHex}}80; color: #9A9AA2; }
          table { border-collapse: collapse; max-width: 100%; }
          th, td { border: 1px solid #FFFFFF1A; padding: 6px 11px; text-align: left; }
          th { background: #FFFFFF0D; font-weight: 600; }
          hr { border: 0; border-top: 1px solid #FFFFFF1A; margin: 1.2em 0; }
          details { background: #FFFFFF08; border: 1px solid #FFFFFF14; border-radius: 9px; padding: .4em .8em; }
          summary { cursor: pointer; font-weight: 600; }
          kbd { background: #2A2A30; border: 1px solid #FFFFFF1A; border-radius: 5px; padding: .1em .4em; font-size: .85em; }
          ::-webkit-scrollbar { width: 11px; height: 11px; }
          ::-webkit-scrollbar-thumb { background: #FFFFFF22; border-radius: 6px; border: 3px solid transparent; background-clip: padding-box; }
          ::-webkit-scrollbar-track { background: transparent; }
        </style>
        </head>
        <body>{{body}}</body>
        </html>
        """;
}
