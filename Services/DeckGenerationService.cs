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
        string detailMode,
        CancellationToken cancellationToken = default);
}

public sealed class GeminiDeckGenerationService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiDeckGenerationService> logger) : IDeckGenerationService
{
    private const int MaxExtractedCharacters = 240_000;
    private const string DefaultModel = "gemini-3.6-flash";
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<SlideDeck> GenerateAsync(
        string fileName,
        string contentType,
        byte[] fileBytes,
        string audience,
        string detailMode,
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
        var text = extension == ".pdf"
            ? null
            : DocumentTextExtractor.Extract(extension, fileBytes, MaxExtractedCharacters);

        var outlineJson = await GenerateOutlineAsync(
            apiKey,
            fileName,
            extension,
            fileBytes,
            text,
            audience,
            detailMode,
            cancellationToken);

        var outline = JsonSerializer.Deserialize<OutlinePlan>(outlineJson, JsonOptions)
                      ?? throw new DeckGenerationException("Gemini 無法建立簡報大綱。");
        outline.RecommendedSlideCount = Math.Clamp(outline.RecommendedSlideCount, 6, 18);

        var deckJson = await GenerateDesignedDeckAsync(
            apiKey,
            fileName,
            extension,
            fileBytes,
            text,
            audience,
            outline,
            outlineJson,
            cancellationToken);

        try
        {
            var generated = JsonSerializer.Deserialize<GeneratedDeck>(deckJson, JsonOptions);
            if (generated is null || generated.Slides.Count == 0)
            {
                throw new JsonException("Gemini returned an empty slide deck.");
            }

            return new SlideDeck
            {
                Title = generated.Title,
                SourceFileName = fileName,
                Audience = audience,
                Theme = generated.Theme,
                Slides = generated.Slides
            };
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Gemini returned an invalid designed deck.");
            throw new DeckGenerationException("Gemini 回傳的排版資料不完整，請再產生一次。");
        }
    }

    private async Task<string> GenerateOutlineAsync(
        string apiKey,
        string fileName,
        string extension,
        byte[] fileBytes,
        string? documentText,
        string audience,
        string detailMode,
        CancellationToken cancellationToken)
    {
        var prompt =
            $"""
            你是資深簡報編輯與資訊架構師。此階段只分析文件並規劃大綱，不撰寫投影片全文。

            文件：{fileName}
            對象：{audience}
            深度模式：{detailMode}

            切頁原則：
            1. 先辨識文件的章節、概念層級、定義、公式、流程、案例、圖表與結論。
            2. 每張投影片只能傳達一個核心訊息。
            3. 定義、公式、流程、比較、案例與結論若內容足夠，必須拆成不同頁。
            4. 不得為湊頁數重複前一頁；也不得把三個以上核心概念塞在同一頁。
            5. 先建立敘事：為何重要 → 核心概念 → 方法或證據 → 應用 → 結論。
            6. concise 建議 6～8 頁；standard 建議 9～12 頁；detailed 建議 13～18 頁；
               auto 則依文件密度在 8～16 頁間決定。
            7. 忠於文件，不得補造來源未提供的專有事實。
            """;

        var input = BuildDocumentInput(extension, fileBytes, documentText, prompt);
        return await SendStructuredRequestAsync(
            apiKey,
            input,
            BuildOutlineSchema(),
            "大綱分析",
            cancellationToken);
    }

    private async Task<string> GenerateDesignedDeckAsync(
        string apiKey,
        string fileName,
        string extension,
        byte[] fileBytes,
        string? documentText,
        string audience,
        OutlinePlan outline,
        string outlineJson,
        CancellationToken cancellationToken)
    {
        var prompt =
            $"""
            你是簡報內容設計師與藝術指導。根據文件與已核定大綱，產生繁體中文的完整簡報設計資料。

            文件：{fileName}
            對象：{audience}
            必須恰好產生 {outline.RecommendedSlideCount} 頁。

            已核定大綱：
            {outlineJson}

            內容規則：
            1. 嚴格依大綱順序，每頁只有一個核心訊息。
            2. content 使用 2～5 個短重點，以換行分隔；投影文字要精簡。
            3. subtitle 必須補充主旨，不能重複 title。
            4. speakerNotes 說明依據、轉場與口頭補充，不能只是複製 content。
            5. visualBrief 要具體描述可呈現的圖表、流程、公式、對照或重點數字；
               沒有合適視覺時可留空，禁止假裝文件含有不存在的圖片。

            排版規則：
            1. 先依主題選擇一致的專業 theme，配色須有可讀對比。
            2. 每頁選擇最適合的 layout：cover、section、title-and-content、
               two-column、quote、data-focus、formula、summary。
            3. cover 只用於第一頁；summary 只用於最後一頁。
            4. 連續頁面不得全部使用同一 layout，版面要有節奏。
            5. backgroundVariant 只能是 base、surface、accent、dark；
               重點頁可用 accent 或 dark，其餘保持節制。
            """;

        var input = BuildDocumentInput(extension, fileBytes, documentText, prompt);
        return await SendStructuredRequestAsync(
            apiKey,
            input,
            BuildDeckSchema(outline.RecommendedSlideCount),
            "投影片設計",
            cancellationToken);
    }

    private async Task<string> SendStructuredRequestAsync(
        string apiKey,
        object[] input,
        object schema,
        string stage,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = configuration["Gemini:Model"] ?? DefaultModel,
            store = false,
            input,
            response_format = new
            {
                type = "text",
                mime_type = "application/json",
                schema
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
                "Gemini stage {Stage} failed with status {StatusCode}: {Response}",
                stage,
                (int)response.StatusCode,
                LimitForLog(responseBody));

            throw new DeckGenerationException(
                $"{stage}失敗（HTTP {(int)response.StatusCode}），請確認 API Key 與模型權限。");
        }

        try
        {
            return ExtractOutputText(responseBody);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Gemini stage {Stage} returned invalid JSON.", stage);
            throw new DeckGenerationException($"{stage}回傳格式錯誤，請再試一次。");
        }
    }

    private static object[] BuildDocumentInput(
        string extension,
        byte[] fileBytes,
        string? documentText,
        string prompt)
    {
        if (extension == ".pdf")
        {
            return
            [
                new
                {
                    type = "document",
                    data = Convert.ToBase64String(fileBytes),
                    mime_type = "application/pdf"
                },
                new { type = "text", text = prompt }
            ];
        }

        if (string.IsNullOrWhiteSpace(documentText))
        {
            throw new DeckGenerationException("無法從文件擷取文字，請確認檔案沒有損壞。");
        }

        return
        [
            new
            {
                type = "text",
                text = $"{prompt}\n\n文件內容：\n---\n{documentText}\n---"
            }
        ];
    }

    private static object BuildOutlineSchema() => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            narrative = new
            {
                type = "string",
                description = "簡報從開場到結論的敘事主線"
            },
            recommendedSlideCount = new
            {
                type = "integer",
                minimum = 6,
                maximum = 18
            },
            sections = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        purpose = new { type = "string" },
                        keyPoints = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        },
                        slideCount = new { type = "integer", minimum = 1, maximum = 5 }
                    },
                    required = new[] { "title", "purpose", "keyPoints", "slideCount" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "title", "narrative", "recommendedSlideCount", "sections" },
        additionalProperties = false
    };

    private static object BuildDeckSchema(int slideCount) => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            theme = new
            {
                type = "object",
                properties = new
                {
                    styleName = new { type = "string" },
                    backgroundColor = ColorSchema("主要背景色"),
                    surfaceColor = ColorSchema("卡片或次要背景色"),
                    primaryColor = ColorSchema("主色"),
                    accentColor = ColorSchema("強調色"),
                    textColor = ColorSchema("主要文字色"),
                    mutedTextColor = ColorSchema("次要文字色"),
                    fontFamily = new
                    {
                        type = "string",
                        @enum = new[] { "Noto Sans TC", "Microsoft JhengHei", "Arial" }
                    }
                },
                required = new[]
                {
                    "styleName", "backgroundColor", "surfaceColor", "primaryColor",
                    "accentColor", "textColor", "mutedTextColor", "fontFamily"
                },
                additionalProperties = false
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
                        subtitle = new { type = "string" },
                        content = new { type = "string" },
                        layout = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "cover", "section", "title-and-content", "two-column",
                                "quote", "data-focus", "formula", "summary"
                            }
                        },
                        sectionLabel = new { type = "string" },
                        backgroundVariant = new
                        {
                            type = "string",
                            @enum = new[] { "base", "surface", "accent", "dark" }
                        },
                        visualBrief = new { type = "string" },
                        speakerNotes = new { type = "string" }
                    },
                    required = new[]
                    {
                        "title", "subtitle", "content", "layout", "sectionLabel",
                        "backgroundVariant", "visualBrief", "speakerNotes"
                    },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "title", "theme", "slides" },
        additionalProperties = false
    };

    private static object ColorSchema(string description) => new
    {
        type = "string",
        description = $"{description}，必須是 #RRGGBB"
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

    private sealed class OutlinePlan
    {
        public string Title { get; set; } = string.Empty;
        public string Narrative { get; set; } = string.Empty;
        public int RecommendedSlideCount { get; set; } = 10;
        public List<OutlineSection> Sections { get; set; } = [];
    }

    private sealed class OutlineSection
    {
        public string Title { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public List<string> KeyPoints { get; set; } = [];
        public int SlideCount { get; set; }
    }

    private sealed class GeneratedDeck
    {
        public string Title { get; set; } = string.Empty;
        public PresentationTheme Theme { get; set; } = new();
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
