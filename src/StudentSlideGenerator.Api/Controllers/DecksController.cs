using Microsoft.AspNetCore.Mvc;
using StudentSlideGenerator.Api.Services;
using StudentSlideGenerator.Shared.Models;

namespace StudentSlideGenerator.Api.Controllers;

[ApiController]
[Route("api/decks")]
public sealed class DecksController(IDeckGenerationService deckGenerator) : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;
    private const long MaxRequestSize = 21 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md" };

    [HttpPost("generate")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestSize)]
    public async Task<ActionResult<SlideDeck>> Generate(
        [FromForm] DeckGenerationForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "檔案無效",
                detail: "請選擇包含內容的文件。");
        }

        if (form.File.Length > MaxFileSize)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "檔案過大",
                detail: "單一檔案上限為 20 MB。");
        }

        var extension = Path.GetExtension(form.File.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            return Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "不支援的格式",
                detail: "目前僅支援 PDF、DOCX、TXT 與 MD。");
        }

        try
        {
            await using var source = form.File.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);

            var deck = await deckGenerator.GenerateAsync(
                Path.GetFileName(form.File.FileName),
                form.File.ContentType,
                buffer.ToArray(),
                string.IsNullOrWhiteSpace(form.Audience)
                    ? "大學課堂報告"
                    : form.Audience.Trim(),
                string.IsNullOrWhiteSpace(form.DetailMode)
                    ? "auto"
                    : form.DetailMode.Trim(),
                cancellationToken);

            return Ok(deck);
        }
        catch (DeckGenerationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "簡報產生失敗",
                detail: exception.Message);
        }
    }

    [HttpPost("export/html")]
    public IActionResult ExportHtml([FromBody] SlideDeck deck)
    {
        if (deck.Slides.Count == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "沒有可匯出的投影片",
                detail: "請先產生至少一張投影片。");
        }

        return Content(HtmlDeckExporter.Export(deck), "text/html; charset=utf-8");
    }
}

public sealed class DeckGenerationForm
{
    public IFormFile? File { get; set; }
    public string Audience { get; set; } = "大學課堂報告";
    public string DetailMode { get; set; } = "auto";
}
