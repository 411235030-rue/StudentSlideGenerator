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
            你是資深簡報總編輯與資訊架構師。此階段只分析文件、建立教學敘事與切頁計畫，
            不撰寫投影片全文，也不提供配色或裝飾建議。

            文件：{fileName}
            對象：{audience}
            深度模式：{detailMode}

            先完整閱讀文件，再依下列順序工作：
            1. 建立內容地圖：辨識章節、概念層級、定義、公式、流程、比較、案例、圖表、結論。
            2. 找出簡報主張：觀眾看完後必須理解或記住的一句話。
            3. 建立敘事弧線：問題／重要性 → 核心概念 → 方法或證據 → 應用 → 結論。
            4. 最後才決定頁數與各段配置。

            切頁硬性規則：
            1. 每頁只能回答一個明確問題或傳達一個核心訊息。
            2. 定義、公式、流程、比較、案例與結論原則上分頁，禁止塞入同一張總覽頁。
            3. 一個 section 的 keyPoints 若包含不同層級或不同用途的概念，必須拆成多頁。
            4. 不得以「概述」「其他」「補充」將不相關內容硬併；不得為湊頁數重複內容。
            5. 每個 section 的 slideCount 必須合理反映內容密度，且所有 slideCount 加總必須等於 recommendedSlideCount。
            6. 第一頁預留封面、最後一頁預留真正的重點統整；中間頁面不得重複封面或目錄內容。
            7. concise 為 6～8 頁；standard 為 9～12 頁；detailed 為 13～18 頁；
               auto 依內容密度在 8～16 頁決定，不要機械式固定頁數。
            8. 忠於文件，不得補造來源沒有的事實、數據、引言或案例。

            輸出的 purpose 必須寫清楚該段對敘事的作用；keyPoints 必須是來源中的具體內容，
            不得寫成「介紹背景」「說明概念」等空泛任務描述。
            """;

        var input = BuildDocumentInput(extension, fileBytes, documentText, prompt);
        return await Se        var prompt =
            $"""
            你是簡報總監、資訊設計師與繁體中文編輯。你的輸出會被程式直接渲染成 16:9 HTML 簡報，
            不是設計提案、不是大綱、也不是給人類設計師的建議。請交付已完成設計決策的結構化資料。

            文件：{fileName}
            對象：{audience}
            必須恰好產生 {outline.RecommendedSlideCount} 頁。

            已核定大綱：
            {outlineJson}

            核心任務：
            1. 忠實保留文件中的定義、公式、因果、步驟、比較與結論，不得發明數據、圖片、引言或案例。
            2. 把每頁做成「觀眾一眼知道重點」的投影畫面，而不是把講義濃縮成條列清單。
            3. 先決定全套簡報的單一視覺概念，再依每頁的溝通目的選擇構圖。
            4. title、subtitle、content 都是會直接顯示的最終文字，禁止放入任何製作備註。

            嚴禁輸出：
            - 「視覺建議」「建議放置」「可搭配」「可以呈現」「設計師可使用」等顧問式文字。
            - 對版面、顏色、圖示或圖片的自然語言說明出現在 title、subtitle 或 content。
            - 每頁固定使用「標題＋四點條列」。
            - 裝飾性漸層、玻璃卡片、emoji、無意義圓形或與主題無關的科技感裝飾。
            - 不存在於來源的統計、引用、品牌、人物、圖表或圖片。

            內容規則：
            1. 每頁只有一個核心訊息；title 最多兩行，避免「XX的介紹」「XX概述」等空泛標題。
            2. subtitle 用一句話說清楚這頁的判斷或意義，不得重複 title。
            3. content 由 2～5 個可直接渲染的內容單位組成，以換行分隔；每行盡量不超過 32 個中文字。
            4. 內容應優先改寫成對比、順序、層級、因果或公式解讀；只有真正並列的資訊才使用一般條列。
            5. speakerNotes 保存來源依據、轉場與口頭補充，不得只是複製畫面文字。
            6. visualBrief 是程式內部欄位，不會顯示。除非來源確實含有必須保留的圖片或圖表，
               否則請輸出空字串；不得在此欄寫「建議」或描述虛構素材。

            Layout 選擇與資料排列：
            - cover：僅第一頁。title 是主標，subtitle 是核心承諾；content 最多 3 行必要資訊。
            - section：只用於真正的章節轉場，文字極少，不得當一般內容頁。
            - title-and-content：只用於無法形成其他視覺關係的單純解說，全套不得超過三分之一。
            - two-column：用於比較、前後、問題／解法；content 必須正好 4 行，前 2 行屬左欄、後 2 行屬右欄。
            - quote：只有文件存在可核對的原文引言時使用，禁止自行創作引言。
            - data-focus：用於重點數字、關鍵名詞或結論；content 第一行必須是 20 字內的焦點，其餘行解釋意義。
            - formula：用於公式；content 第一行只放完整公式，其餘行依序解釋變數與用途。
            - summary：僅最後一頁，必須統整 3 個可帶走的結論，不得重複目錄。

            視覺系統規則：
            1. 根據文件主題選擇 editorial、academic-modern、swiss 或 data-led 方向，
               使用一個主要背景、一個主色與一個強調色；配色必須符合投影可讀性。
            2. 視覺概念必須來自文件主題，不要預設使用「深藍科技感」。
            3. 相鄰兩頁不得使用相同 layout；同一 layout 最多連續出現一次。
            4. backgroundVariant 只能是 base、surface、accent、dark；accent 或 dark 僅用於封面、章節轉場或關鍵結論。
            5. 每頁必須有單一明確焦點，並在留白、字級與位置上形成層級。
            6. 最後逐頁檢查：是否忠於來源、是否能從教室後排閱讀、是否與前後頁構圖重複。

            只回傳符合 schema 的 JSON。不要輸出 Markdown、解說、設計評語或任何 JSON 以外的內容。
            """;. visualBrief 要具體描述可呈現的圖表、流程、公式、對照或重點數字；
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
