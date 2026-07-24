using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using StudentSlideGenerator.Models;

namespace StudentSlideGenerator.Services;

public static class HtmlDeckExporter
{
    public static string Export(SlideDeck deck)
    {
        var theme = deck.Theme;
        var html = new StringBuilder();

        html.Append(
            $$"""
            <!doctype html>
            <html lang="zh-Hant">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{{Encode(deck.Title)}}</title>
              <style>
                :root {
                  --bg: {{Color(theme.BackgroundColor, "#F5F1E8")}};
                  --surface: {{Color(theme.SurfaceColor, "#FFFFFF")}};
                  --primary: {{Color(theme.PrimaryColor, "#14213D")}};
                  --accent: {{Color(theme.AccentColor, "#E76F51")}};
                  --text: {{Color(theme.TextColor, "#172033")}};
                  --muted: {{Color(theme.MutedTextColor, "#667085")}};
                }
                * { box-sizing: border-box; }
                body { margin: 0; overflow: hidden; color: var(--text); background: #111827;
                       font-family: "{{Encode(theme.FontFamily)}}", "Noto Sans TC", sans-serif; }
                .deck { width: 100vw; height: 100vh; }
                .slide { position: absolute; inset: 0; display: none; padding: 7vh 8vw;
                         background: var(--bg); overflow: hidden; }
                .slide.active { display: flex; flex-direction: column; }
                .slide::before { content: ""; position: absolute; width: 28vw; height: 28vw;
                                 right: -10vw; top: -12vw; border-radius: 50%;
                                 background: color-mix(in srgb, var(--accent) 18%, transparent); }
                .variant-surface { background: var(--surface); }
                .variant-accent { color: white; background: var(--accent); }
                .variant-dark { color: white; background: var(--primary); }
                .label { position: relative; z-index: 1; color: var(--accent); font-size: 1vw;
                         font-weight: 900; letter-spacing: .18em; text-transform: uppercase; }
                .variant-accent .label, .variant-dark .label { color: rgba(255,255,255,.72); }
                h1 { position: relative; z-index: 1; max-width: 80%; margin: 3vh 0 1.8vh;
                     font-size: clamp(2.3rem, 5vw, 5.5rem); line-height: 1.04; letter-spacing: -.045em; }
                .subtitle { position: relative; z-index: 1; max-width: 68%; margin: 0 0 3vh;
                            color: var(--muted); font-size: clamp(1rem, 1.7vw, 2rem); line-height: 1.5; }
                .variant-accent .subtitle, .variant-dark .subtitle { color: rgba(255,255,255,.72); }
                ul { position: relative; z-index: 1; max-width: 74%; display: grid; gap: 1.5vh;
                     margin: 1vh 0; padding-left: 1.3em; font-size: clamp(1.05rem, 1.8vw, 2rem);
                     line-height: 1.55; }
                .meta-list { list-style: none; padding: 0; display: flex; flex-wrap: wrap; gap: .8rem; }
                .meta-list li { padding: .55rem .9rem; border: 1px solid color-mix(in srgb, currentColor 24%, transparent);
                                font-size: clamp(.82rem, 1.1vw, 1.1rem); }
                .split-grid { position: relative; z-index: 1; display: grid; grid-template-columns: 1fr 1fr;
                              gap: 2.2vw; width: 88%; margin-top: 2vh; }
                .split-panel { display: grid; align-content: start; gap: 1rem; padding: 2vw;
                               border-top: .45vw solid var(--accent);
                               background: color-mix(in srgb, var(--surface) 90%, transparent); }
                .split-item { font-size: clamp(1.05rem, 1.6vw, 1.75rem); line-height: 1.45; }
                .focal-layout { position: relative; z-index: 1; display: grid; grid-template-columns: minmax(32%, .75fr) 1fr;
                                align-items: center; gap: 5vw; width: 90%; flex: 1; }
                .focal-value { color: var(--accent); font-weight: 900; font-size: clamp(2.6rem, 6vw, 6.8rem);
                               line-height: 1.02; letter-spacing: -.045em; overflow-wrap: anywhere; }
                .focal-layout ul { max-width: none; margin: 0; }
                .summary-grid { position: relative; z-index: 1; display: grid;
                                grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1.5vw;
                                width: 92%; margin-top: 3vh; list-style: none; padding: 0; }
                .summary-grid li { min-height: 24vh; padding: 2vw; display: flex; align-items: flex-end;
                                   border-top: .45vw solid currentColor;
                                   background: color-mix(in srgb, var(--surface) 10%, transparent);
                                   font-size: clamp(1rem, 1.45vw, 1.55rem); line-height: 1.45; }
                .variant-accent .split-panel, .variant-dark .split-panel {
                  background: rgba(255,255,255,.09); border-top-color: white;
                }
                .variant-accent .focal-value, .variant-dark .focal-value { color: white; }
                .layout-cover, .layout-section { justify-content: center; }
                .layout-cover h1, .layout-section h1 { max-width: 78%; font-size: clamp(3rem, 7vw, 7.5rem); }
                .layout-two-column ul { columns: 2; column-gap: 7vw; max-width: 86%; }
                .layout-quote { justify-content: center; }
                .layout-quote h1 { max-width: 78%; font-size: clamp(2.4rem, 5.5vw, 6rem); }
                .layout-data-focus h1, .layout-formula h1 { color: var(--accent); font-size: clamp(3rem, 7vw, 7rem); }
                .progress { position: fixed; z-index: 20; left: 0; bottom: 0; height: 5px;
                            background: var(--accent); transition: width .2s; }
                .counter { position: fixed; z-index: 20; right: 2vw; bottom: 2vh;
                           color: rgba(255,255,255,.7); font-size: 13px; }
                .help { position: fixed; z-index: 20; left: 2vw; bottom: 2vh;
                        color: rgba(255,255,255,.55); font-size: 12px; }
                @media print {
                  body { overflow: visible; background: white; }
                  .deck { width: auto; height: auto; }
                  .slide { position: relative; display: flex !important; width: 13.333in; height: 7.5in;
                           page-break-after: always; }
                  .progress, .counter, .help { display: none; }
                }
              </style>
            </head>
            <body>
              <main class="deck">
            """);

        for (var index = 0; index < deck.Slides.Count; index++)
        {
            var slide = deck.Slides[index];
            html.Append(
                $$"""
                    <section class="slide layout-{{Css(slide.Layout)}} variant-{{Css(slide.BackgroundVariant)}}{{(index == 0 ? " active" : "")}}">
                      <div class="label">{{Encode(slide.SectionLabel)}}</div>
                      <h1>{{Encode(slide.Title)}}</h1>
                      <div class="subtitle">{{Encode(slide.Subtitle)}}</div>
                      {{RenderContent(slide)}}
                    </section>
                """);
        }

        html.Append(
            $$"""
              </main>
              <div class="progress" id="progress"></div>
              <div class="help">← → 切換 · F11 全螢幕 · Ctrl+P 匯出 PDF</div>
              <div class="counter" id="counter"></div>
              <script>
                const slides = [...document.querySelectorAll('.slide')];
                let current = 0;
                function show(index) {
                  current = Math.max(0, Math.min(index, slides.length - 1));
                  slides.forEach((slide, i) => slide.classList.toggle('active', i === current));
                  document.getElementById('counter').textContent = `${current + 1} / ${slides.length}`;
                  document.getElementById('progress').style.width = `${((current + 1) / slides.length) * 100}%`;
                }
                addEventListener('keydown', event => {
                  if (['ArrowRight', 'PageDown', ' '].includes(event.key)) show(current + 1);
                  if (['ArrowLeft', 'PageUp'].includes(event.key)) show(current - 1);
                  if (event.key === 'Home') show(0);
                  if (event.key === 'End') show(slides.length - 1);
                });
                addEventListener('click', event => show(current + (event.clientX > innerWidth / 2 ? 1 : -1)));
                show(0);
              </script>
            </body>
            </html>
            """);

        return html.ToString();
    }

    private static string RenderContent(SlideItem slide)
    {
        var items = slide.Content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('•', '-', ' '))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (items.Length == 0)
        {
            return string.Empty;
        }

        return slide.Layout switch
        {
            "cover" or "section" => RenderList(items, "meta-list"),
            "two-column" => RenderTwoColumn(items),
            "formula" or "data-focus" => RenderFocal(items),
            "summary" => RenderList(items.Take(3), "summary-grid"),
            _ => RenderList(items)
        };
    }

    private static string RenderList(IEnumerable<string> items, string? className = null)
    {
        var classAttribute = string.IsNullOrWhiteSpace(className)
            ? string.Empty
            : $" class=\"{className}\"";
        var listItems = items.Select(item => $"<li>{Encode(item)}</li>");
        return $"<ul{classAttribute}>{string.Join(string.Empty, listItems)}</ul>";
    }

    private static string RenderTwoColumn(string[] items)
    {
        var middle = (int)Math.Ceiling(items.Length / 2d);
        var left = items.Take(middle)
            .Select(item => $"<div class=\"split-item\">{Encode(item)}</div>");
        var right = items.Skip(middle)
            .Select(item => $"<div class=\"split-item\">{Encode(item)}</div>");

        return
            $"<div class=\"split-grid\">" +
            $"<div class=\"split-panel\">{string.Join(string.Empty, left)}</div>" +
            $"<div class=\"split-panel\">{string.Join(string.Empty, right)}</div>" +
            $"</div>";
    }

    private static string RenderFocal(string[] items)
    {
        var explanation = items.Skip(1);
        return
            $"<div class=\"focal-layout\">" +
            $"<div class=\"focal-value\">{Encode(items[0])}</div>" +
            RenderList(explanation) +
            $"</div>";
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Color(string? value, string fallback) =>
        Regex.IsMatch(value ?? string.Empty, "^#[0-9A-Fa-f]{6}$")
            ? value!
            : fallback;

    private static string Css(string? value) =>
        Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9-]", string.Empty);
}
