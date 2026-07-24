using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StudentSlideGenerator.Models;

namespace StudentSlideGenerator.Services;

public interface IDeckGenerationService
{
    Task<SlideDeck> GenerateAsync(
        string fileName,
        string contentType,
        byte[] fileBytes,
        string audience,
        int slideCount,
        CancellationToken cancellationToken = default);
}

public sealed class GeminiDeckGenerationService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiDeckGenerationService> logger) : IDeckGenerationService
{
    private const int MaxExtractedCharacters = 240_000;
    private const string DefaultModel = "gemini-3.6-flash";

    public async Task<SlideDeck> GenerateAsync(
        string fileName,
        string contentType,
        byte[] fileBytes,
        string audience,
        int slideCount,
        CancellationToken cancellationToken = default)
    {
        var apiKey =
            Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new DeckGenerationException(
                "尚未設定 Gemini API Key，請先設定 GEMINI_API_KEY 後重新啟動程式。");
        }

        if (fileBytes.Length == 0)
        {
            throw new DeckGenerationException("檔案內容是空的，請重新選擇文件。");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var prompt = BuildPrompt(fileName, audience, slideCount);
        var input = extension == ".pdf"
            ? BuildPdfInput(fileBytes, prompt)
            : BuildTextInput(
                DocumentTextExtractor.Extract(extension, fileBytes, MaxExtractedCharacters),
                prompt);

        var request = new
        {
            model = configuration["Gemini:Model"] ?? DefaultModel,
            store = false,
            input,
            response_format = new
            {
                type = "text",
                mime_type = "application/json",
                schema = BuildResponseSchema(slideCount)
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("x-goog-api-key", apiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Gemini request failed with status {StatusCode}: {Response}",
                (int)response.StatusCode,
                LimitForLog(responseBody));

            throw new DeckGenerationException(
                $"Gemini 產生失敗（HTTP {(int)response.StatusCode}），請確認 API Key 與模型權限。");
        }

        try
        {
            var json = ExtractOutputText(responseBody);
            var generated = JsonSerializer.Deserialize<GeneratedDeck>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (generated is null || generated.Slides.Count == 0)
            {
                throw new JsonException("Gemini returned an empty slide deck.");
            }

            return new SlideDeck
            {
                Title = generated.Title,
                SourceFileName = fileName,
                Audience = audience,
                Slides = generated.Slides
            };
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Gemini returned an invalid structured response.");
            throw new DeckGenerationException("Gemini 回傳的簡報格式不完整，請再產生一次。");
        }
    }

    private static object[] BuildPdfInput(byte[] fileBytes, string prompt) =>
    [
        new
        {
            type = "document",
            data = Convert.ToBase64String(fileBytes),
            mime_type = "application/pdf"
        },
        new { type = "text", text = prompt }
    ];

    private static object[] BuildTextInput(string documentText, string prompt)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            throw new DeckGenerationException("無法從文件擷取文字，請確認檔案沒有損壞。");
        }

        return
        [
            new
            {
                type = "text",
                text = $"{prompt}\n\n以下是文件內容：\n---\n{documentText}\n---"
            }
        ];
    }

    private static string BuildPrompt(string fileName, string audience, int slideCount) =>
        $"""
        你是一位專業的教學簡報設計師。請根據使用者提供的文件製作繁體中文簡報。

        文件名稱：{fileName}
        簡報對象：{audience}
        投影片數量：必須恰好為 {slideCount} 頁

        規則：
        1. 忠於文件內容，不得捏造文件未提供的事實。
        2. 第一頁為清楚的封面或主題導入，最後一頁整理核心結論。
        3. 每頁只傳達一個主要概念，內容適合直接投影。
        4. content 使用 2 至 5 個短重點，以換行分隔；不要使用 Markdown 表格。
        5. layout 只能是 title、title-and-content、two-column、quote、summary 之一。
        6. speakerNotes 補充講者應說明但不必顯示在畫面上的內容。
        7. 若 PDF 含重要圖片、圖表或表格，請在相關頁的 speakerNotes 說明其意義。
        """;

    private static object BuildResponseSchema(int slideCount) => new
    {
        type = "object",
        properties = new
        {
            title = new
            {
                type = "string",
                description = "整份簡報的短標題"
            },
            slides = new
            {
                type = "array",
                minItems = slideCount,
                maxItems = slideCount,
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        content = new { type = "string" },
                        layout = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "title",
                                "title-and-content",
                                "two-column",
                                "quote",
                                "summary"
                            }
                        },
                        speakerNotes = new { type = "string" }
                    },
                    required = new[] { "title", "content", "layout", "speakerNotes" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "title", "slides" },
        additionalProperties = false
    };

    private static string ExtractOutputText(string responseBody)
    {
        using var response = JsonDocument.Parse(responseBody);

        var steps = response.RootElement.GetProperty("steps");
        for (var stepIndex = steps.GetArrayLength() - 1; stepIndex >= 0; stepIndex--)
        {
            var step = steps[stepIndex];
            if (!step.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && item.TryGetProperty("text", out var text))
                {
                    return text.GetString()
                           ?? throw new JsonException("Empty Gemini text output.");
                }
            }
        }

        throw new JsonException("Gemini response did not contain text output.");
    }

    private static string LimitForLog(string value) =>
        value.Length <= 1_000 ? value : value[..1_000];

    private sealed class GeneratedDeck
    {
        public string Title { get; set; } = string.Empty;
        public List<SlideItem> Slides { get; set; } = [];
    }
}

internal static class DocumentTextExtractor
{
    public static string Extract(string extension, byte[] bytes, int maxCharacters) =>
        extension switch
        {
            ".txt" or ".md" => TrimToLimit(ReadText(bytes), maxCharacters),
            ".docx" => TrimToLimit(ReadDocx(bytes), maxCharacters),
            _ => throw new DeckGenerationException("目前無法解析這種文件格式。")
        };

    private static string ReadText(byte[] bytes)
    {
        using var reader = new StreamReader(
            new MemoryStream(bytes),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ReadDocx(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(
                new MemoryStream(bytes),
                ZipArchiveMode.Read,
                leaveOpen: false);

            var entry = archive.GetEntry("word/document.xml")
                        ?? throw new InvalidDataException("DOCX document.xml is missing.");

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace word =
                "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            var paragraphs = document
                .Descendants(word + "p")
                .Select(paragraph => string.Concat(
                    paragraph.Descendants(word + "t").Select(text => text.Value)))
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return string.Join(Environment.NewLine, paragraphs);
        }
        catch (InvalidDataException exception)
        {
            throw new DeckGenerationException($"DOCX 解析失敗：{exception.Message}");
        }
    }

    private static string TrimToLimit(string text, int maxCharacters) =>
        text.Length <= maxCharacters
            ? text
            : $"{text[..maxCharacters]}\n\n[文件內容因長度限制已截斷]";
}

public sealed class DeckGenerationException(string message) : Exception(message);
