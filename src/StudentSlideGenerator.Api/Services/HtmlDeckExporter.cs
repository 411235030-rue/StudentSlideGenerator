using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using StudentSlideGenerator.Shared.Models;

namespace StudentSlideGenerator.Api.Services;

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
                html, body { width: 100%; height: 100%; }
                body { margin: 0; overflow: hidden; color: var(--text); background: #0b0d12;
                       font-family: "{{Encode(theme.FontFamily)}}", "Noto Sans TC", "Microsoft JhengHei", sans-serif; }
                .deck { width: 100vw; height: 100vh; }
                .slide { position: absolute; inset: 0; display: none; padding: 4.8vh 5.5vw 6vh;
                         color: var(--text); background: var(--bg); overflow: hidden; isolation: isolate; }
                .slide.active { display: flex; flex-direction: column; }
                .slide::before { content: ""; position: absolute; z-index: -1; inset: 0;
                                 background: linear-gradient(90deg,
                                   color-mix(in srgb, var(--primary) 8%, transparent) 1px,
                                   transparent 1px);
                                 background-size: 8.333vw 100%; opacity: .42; pointer-events: none; }
                .slide::after { content: ""; position: absolute; left: 5.5vw; right: 5.5vw; top: 4vh;
                                height: 1px; background: color-mix(in srgb, currentColor 18%, transparent); }
                .direction-editorial .slide::before {
                  background: linear-gradient(115deg,
                    color-mix(in srgb, var(--accent) 7%, transparent) 0 31%,
                    transparent 31% 100%);
                }
                .direction-data-led .slide::before {
                  background-image:
                    linear-gradient(color-mix(in srgb, var(--primary) 7%, transparent) 1px, transparent 1px),
                    linear-gradient(90deg, color-mix(in srgb, var(--primary) 7%, transparent) 1px, transparent 1px);
                  background-size: 6.25vw 6.25vw;
                }
                .direction-swiss .slide::before { background: none; }
                .direction-swiss h1 { font-weight: 950; letter-spacing: -.06em; }
                .variant-surface { background: var(--surface); }
                .variant-accent { color: white; background: var(--accent); }
                .variant-dark { color: white; background: var(--primary); }
                .slide-head { position: relative; z-index: 2; display: flex; align-items: center;
                              justify-content: space-between; min-height: 4vh; margin-bottom: 3vh; }
                .label { color: var(--accent); font-size: clamp(.66rem, .85vw, .95rem);
                         font-weight: 900; letter-spacing: .16em; text-transform: uppercase; }
                .page-index { color: var(--muted); font-size: clamp(.68rem, .8vw, .9rem);
                              font-variant-numeric: tabular-nums; letter-spacing: .08em; }
                .variant-accent .label, .variant-dark .label,
                .variant-accent .page-index, .variant-dark .page-index { color: rgba(255,255,255,.72); }
                .slide-main { position: relative; z-index: 1; display: flex; flex: 1;
                              min-height: 0; flex-direction: column; }
                h1 { max-width: 88%; margin: 0 0 1.6vh; font-size: clamp(2.25rem, 4.2vw, 5rem);
                     line-height: 1.04; letter-spacing: -.047em; text-wrap: balance; }
                .subtitle { max-width: 72%; margin: 0 0 3vh; color: var(--muted);
                            font-size: clamp(1rem, 1.45vw, 1.7rem); line-height: 1.45; text-wrap: balance; }
                .variant-accent .subtitle, .variant-dark .subtitle { color: rgba(255,255,255,.74); }
                ul:not(.meta-list):not(.summary-grid) { max-width: 76%; display: grid; gap: 1.25vh;
                         margin: 1vh 0 0; padding: 0; list-style: none;
                         font-size: clamp(1.05rem, 1.55vw, 1.8rem); line-height: 1.5; }
                ul:not(.meta-list):not(.summary-grid) li { position: relative; padding: 1.05vh 0 1.05vh 2.1vw;
                         border-bottom: 1px solid color-mix(in srgb, currentColor 14%, transparent); }
                ul:not(.meta-list):not(.summary-grid) li::before { content: ""; position: absolute;
                         left: 0; top: 1.7em; width: .7vw; height: .18vw; min-height: 2px;
                         background: var(--accent); }
                .variant-accent ul li::before, .variant-dark ul li::before { background: white; }
                .meta-list { display: flex; flex-wrap: wrap; gap: .8rem 1.5rem; margin: 2vh 0 0;
                             padding: 0; list-style: none; color: var(--muted); }
                .meta-list li { padding-top: .75rem; border-top: 2px solid var(--accent);
                                font-size: clamp(.82rem, 1.05vw, 1.05rem); }
                .variant-accent .meta-list, .variant-dark .meta-list { color: rgba(255,255,255,.74); }
                .variant-accent .meta-list li, .variant-dark .meta-list li { border-top-color: white; }
                .layout-cover .slide-main { justify-content: flex-end; padding-bottom: 2vh; }
                .layout-cover h1 { max-width: 88%; font-size: clamp(3.3rem, 6.6vw, 7.6rem); line-height: .96; }
                .layout-cover .subtitle { max-width: 64%; margin-top: 1.2vh; }
                .layout-section .slide-main { justify-content: center; }
                .layout-section h1 { max-width: 82%; font-size: clamp(3.4rem, 6vw, 7rem); }
                .layout-title-and-content .slide-main { display: grid; grid-template-columns: minmax(0, .9fr) minmax(0, 1.1fr);
                                                       grid-template-rows: auto 1fr; column-gap: 6vw; align-items: start; }
                .layout-title-and-content h1 { grid-column: 1; grid-row: 1 / 3; max-width: 100%; }
                .layout-title-and-content .subtitle { grid-column: 2; max-width: 100%; }
                .layout-title-and-content ul { grid-column: 2; max-width: 100% !important; }
                .split-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0; width: 100%; margin-top: auto; }
                .split-panel { min-height: 34vh; display: grid; align-content: end; gap: 1.2rem;
                               padding: 2.4vw 3vw 2.4vw 0; border-top: .42vw solid var(--accent); }
                .split-panel + .split-panel { padding-left: 3vw; padding-right: 0;
                                             border-left: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
                .split-panel::before { color: var(--muted); font-size: .78rem; font-weight: 900; letter-spacing: .16em; }
                .split-panel:first-child::before { content: "01"; }
                .split-panel:last-child::before { content: "02"; }
                .split-item:first-of-type { font-size: clamp(1.35rem, 2vw, 2.35rem); font-weight: 850; line-height: 1.2; }
                .split-item:not(:first-of-type) { color: var(--muted); font-size: clamp(1rem, 1.3vw, 1.45rem); line-height: 1.5; }
                .variant-accent .split-panel::before, .variant-dark .split-panel::before,
                .variant-accent .split-item:not(:first-of-type), .variant-dark .split-item:not(:first-of-type) {
                  color: rgba(255,255,255,.7);
                }
                .process-flow { display: grid; grid-template-columns: repeat(auto-fit, minmax(0, 1fr));
                                gap: 0; width: 100%; margin: auto 0; counter-reset: step; }
                .process-step { position: relative; min-height: 27vh; display: flex; flex-direction: column;
                                justify-content: flex-end; padding: 2vw 2.2vw 2vw 0;
                                border-top: .42vw solid var(--accent); counter-increment: step;
                                font-size: clamp(1rem, 1.4vw, 1.6rem); line-height: 1.45; }
                .process-step:not(:last-child) { margin-right: 2.2vw; }
                .process-step:not(:last-child)::after { content: "→"; position: absolute; right: -1.55vw; top: 48%;
                                                        color: var(--accent); font-size: 1.6vw; font-weight: 900; }
                .process-step::before { content: "0" counter(step); margin-bottom: auto;
                                        color: var(--muted); font-size: .82rem; font-weight: 900; letter-spacing: .14em; }
                .variant-accent .process-step, .variant-dark .process-step { border-top-color: white; }
                .variant-accent .process-step::before, .variant-dark .process-step::before {
                  color: rgba(255,255,255,.68);
                }
                .variant-accent .process-step:not(:last-child)::after,
                .variant-dark .process-step:not(:last-child)::after { color: white; }
                .focal-layout { display: grid; grid-template-columns: minmax(36%, .8fr) 1fr;
                                align-items: center; gap: 5vw; width: 100%; flex: 1; }
                .focal-value { color: var(--accent); font-weight: 950; font-size: clamp(3rem, 6.3vw, 7.2rem);
                               line-height: .98; letter-spacing: -.055em; overflow-wrap: anywhere; }
                .focal-layout ul { max-width: none !important; margin: 0 !important; }
                .variant-accent .focal-value, .variant-dark .focal-value { color: white; }
                .quote-block { max-width: 84%; margin: auto 0; padding-left: 4vw;
                               border-left: .55vw solid var(--accent); }
                .quote-mark { color: var(--accent); font-size: 5vw; line-height: .5; }
                blockquote { margin: 1.4vh 0; font-size: clamp(2.2rem, 4.4vw, 5.2rem);
                             font-weight: 850; line-height: 1.16; letter-spacing: -.035em; text-wrap: balance; }
                .quote-source { color: var(--muted); font-size: clamp(.9rem, 1.1vw, 1.2rem); }
                .variant-accent .quote-block, .variant-dark .quote-block { border-left-color: white; }
                .variant-accent .quote-mark, .variant-dark .quote-mark { color: white; }
                .variant-accent .quote-source, .variant-dark .quote-source { color: rgba(255,255,255,.72); }
                .summary-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 2vw;
                                width: 100%; margin: auto 0 0; padding: 0; list-style: none; counter-reset: summary; }
                .summary-grid li { min-height: 28vh; display: flex; flex-direction: column;
                                   justify-content: flex-end; padding: 2vw 0 0;
                                   border-top: .42vw solid currentColor; counter-increment: summary;
                                   font-size: clamp(1.1rem, 1.55vw, 1.75rem); line-height: 1.42; }
                .summary-grid li::before { content: "0" counter(summary); margin-bottom: auto;
                                          color: var(--muted); font-size: .82rem; font-weight: 900; letter-spacing: .14em; }
                .variant-accent .summary-grid li::before, .variant-dark .summary-grid li::before {
                  color: rgba(255,255,255,.68);
                }
                .progress { position: fixed; z-index: 20; left: 0; bottom: 0; height: 4px;
                            background: var(--accent); transition: width .2s; }
                .controls { position: fixed; z-index: 20; left: 50%; bottom: 1.5vh;
                            display: flex; align-items: center; gap: .25rem; padding: .35rem;
                            transform: translateX(-50%); color: white; background: rgba(8,10,14,.78);
                            backdrop-filter: blur(10px); }
                .controls button { min-width: 38px; height: 34px; padding: 0 .7rem; color: white;
                                   background: transparent; border: 0; cursor: pointer; font: inherit; }
                .controls button:hover { background: rgba(255,255,255,.12); }
                .controls button:disabled { opacity: .3; cursor: default; }
                .counter { min-width: 66px; text-align: center; color: rgba(255,255,255,.76);
                           font-size: 11px; letter-spacing: .06em; }
                .help { position: fixed; z-index: 20; left: 1vw; bottom: 1.5vh; padding: .45rem .65rem;
                        color: rgba(255,255,255,.72); background: rgba(8,10,14,.72);
                        font-size: 11px; letter-spacing: .04em; }
                .speaker-notes { display: none; position: absolute; z-index: 10; left: 5.5vw; right: 5.5vw; bottom: 6vh;
                                 padding: 1.2rem 1.4rem; color: white; background: rgba(8,10,14,.94);
                                 font-size: clamp(.8rem, 1vw, 1rem); line-height: 1.5; }
                body.show-notes .slide.active .speaker-notes { display: block; }
                @media print {
                  body { overflow: visible; background: white; }
                  .deck { width: auto; height: auto; }
                  .slide { position: relative; display: flex !important; width: 13.333in; height: 7.5in;
                           page-break-after: always; }
                  .progress, .controls, .help, .speaker-notes { display: none !important; }
                }
              </style>
            </head>
            <body>
              <main class="deck direction-{{Css(theme.StyleName)}}">
            """);

        for (var index = 0; index < deck.Slides.Count; index++)
        {
            var slide = deck.Slides[index];
            html.Append(
                $$"""
                    <section class="slide layout-{{Css(slide.Layout)}} variant-{{Css(slide.BackgroundVariant)}}{{(index == 0 ? " active" : "")}}">
                      <header class="slide-head">
                        <div class="label">{{Encode(slide.SectionLabel)}}</div>
                        <div class="page-index">{{(index + 1).ToString("00")}} / {{deck.Slides.Count.ToString("00")}}</div>
                      </header>
                      <div class="slide-main">
                        <h1>{{Encode(slide.Title)}}</h1>
                        <div class="subtitle">{{Encode(slide.Subtitle)}}</div>
                        {{RenderContent(slide)}}
                      </div>
                      {{RenderNotes(slide.SpeakerNotes)}}
                    </section>
                """);
        }

        html.Append(
            $$"""
              </main>
              <div class="progress" id="progress"></div>
              <div class="help">← → 換頁 · F 全螢幕 · N 備註 · P 列印</div>
              <nav class="controls" aria-label="簡報控制">
                <button id="prev" type="button" title="上一頁">←</button>
                <span class="counter" id="counter"></span>
                <button id="next" type="button" title="下一頁">→</button>
                <button id="fullscreen" type="button" title="全螢幕">⛶</button>
                <button id="notes" type="button" title="講者備註">N</button>
                <button id="print" type="button" title="列印或另存 PDF">PDF</button>
              </nav>
              <script>
                const slides = [...document.querySelectorAll('.slide')];
                const previousButton = document.getElementById('prev');
                const nextButton = document.getElementById('next');
                let current = 0;
                function show(index) {
                  current = Math.max(0, Math.min(index, slides.length - 1));
                  slides.forEach((slide, i) => slide.classList.toggle('active', i === current));
                  document.getElementById('counter').textContent = (current + 1) + ' / ' + slides.length;
                  document.getElementById('progress').style.width = (((current + 1) / slides.length) * 100) + '%';
                  previousButton.disabled = current === 0;
                  nextButton.disabled = current === slides.length - 1;
                }
                function toggleFullscreen() {
                  if (!document.fullscreenElement) document.documentElement.requestFullscreen?.();
                  else document.exitFullscreen?.();
                }
                previousButton.addEventListener('click', () => show(current - 1));
                nextButton.addEventListener('click', () => show(current + 1));
                document.getElementById('fullscreen').addEventListener('click', toggleFullscreen);
                document.getElementById('notes').addEventListener('click', () => document.body.classList.toggle('show-notes'));
                document.getElementById('print').addEventListener('click', () => window.print());
                addEventListener('keydown', event => {
                  if (['ArrowRight', 'PageDown', ' '].includes(event.key)) show(current + 1);
                  if (['ArrowLeft', 'PageUp'].includes(event.key)) show(current - 1);
                  if (event.key === 'Home') show(0);
                  if (event.key === 'End') show(slides.length - 1);
                  if (event.key.toLowerCase() === 'f') toggleFullscreen();
                  if (event.key.toLowerCase() === 'n') document.body.classList.toggle('show-notes');
                  if (event.key.toLowerCase() === 'p') window.print();
                });
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
            "process" => RenderProcess(items),
            "formula" or "data-focus" => RenderFocal(items),
            "quote" => RenderQuote(items),
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

    private static string RenderProcess(string[] items)
    {
        var steps = items
            .Take(5)
            .Select(item => $"<div class=\"process-step\">{Encode(item)}</div>");

        return $"<div class=\"process-flow\">{string.Join(string.Empty, steps)}</div>";
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

    private static string RenderQuote(string[] items)
    {
        var source = items.Length > 1
            ? $"<div class=\"quote-source\">{Encode(string.Join(" · ", items.Skip(1)))}</div>"
            : string.Empty;

        return
            $"<div class=\"quote-block\">" +
            $"<div class=\"quote-mark\">“</div>" +
            $"<blockquote>{Encode(items[0])}</blockquote>" +
            source +
            $"</div>";
    }

    private static string RenderNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes)
            ? string.Empty
            : $"<aside class=\"speaker-notes\"><strong>講者備註</strong><br>{Encode(notes)}</aside>";

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Color(string? value, string fallback) =>
        Regex.IsMatch(value ?? string.Empty, "^#[0-9A-Fa-f]{6}$")
            ? value!
            : fallback;

    private static string Css(string? value) =>
        Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9-]", string.Empty);
}
