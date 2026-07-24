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
            1. 每頁只有一個核心訊息；先寫主張或結論，再放支持它的事實。title 最多兩行，
               避免「XX的介紹」「XX概述」等空泛標題。
            2. subtitle 用一句話說清楚這頁的判斷、價值或因果關係，不得重複 title。
            3. 採用適合繁體中文投影的 6×6 精簡精神：content 由 2～5 個可直接渲染的內容單位組成，
               以換行分隔；每行盡量不超過 32 個中文字，細節移至 speakerNotes。
            4. 內容應優先轉譯成對比、順序、層級、因果、循環、公式解讀或重點數字；
               只有真正並列且無其他關係的資訊才使用一般條列。
            5. 若來源含有真實數據，先判斷溝通目的：類別比較用長條圖思維、時間趨勢用折線圖思維、
               組成比例才用圓餅圖思維、變數關係用散布圖思維。不得為了裝飾而製造圖表。
            6. speakerNotes 保存來源依據、轉場與口頭補充，不得只是複製畫面文字。
            7. visualBrief 是程式內部欄位，不會顯示。除非來源確實含有必須保留的圖片或圖表，
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
               先確定整份簡報的視覺概念，再生成各頁。
            2. 色彩控制在一個主色、一個輔助色與一個點綴色以內；背景、白色、黑色與灰階中性色不計。
               顏色必須有明確功能並符合投影可讀性，不得每頁任意換色。
            3. 視覺概念必須來自文件主題，不要預設使用「深藍科技感」。
            4. 建立固定字級層級：標題最大、焦點資訊次之、內文最小；整份簡報使用一致字體，
               不得用縮小文字的方式塞入更多內容。
            5. 保留安全邊距與充足留白。每頁只設一個主要視覺焦點，不要把所有空間填滿。
            6. 優先以 HTML 可渲染的比較欄、流程關係、公式焦點、數字焦點和層級結構取代長段文字。
            7. 相鄰兩頁不得使用相同 layout；同一 layout 最多連續出現一次。
            8. backgroundVariant 只能是 base、surface、accent、dark；accent 或 dark 僅用於封面、章節轉場或關鍵結論。
            9. 若簡報超過 10 頁且來源確實可分成 3～5 個章節，可安排精簡目錄；短簡報或單一主題不得硬加目錄。
            10. 最後逐頁檢查：是否忠於來源、是否結論先行、是否能從教室後排閱讀、
                是否有足夠留白，以及是否與前後頁構圖重複。

            只回傳符合 schema 的 JSON。不要輸出 Markdown、解說、設計評語或任何 JSON 以外的內容。
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
                    styleName = new
                    {
                        type = "string",
                        @enum = new[]
                        {
                            "editorial", "academic-modern", "swiss", "data-led"
                        }
                    },
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
