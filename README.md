# Student Slide Generator

將 PDF、DOCX、TXT 或 Markdown 教材轉成可編輯、可預覽及可下載的 HTML 簡報。

## 專案架構

\`\`\`text
StudentSlideGenerator/
├─ src/
│  ├─ StudentSlideGenerator.Web/      # Blazor WebAssembly 前端
│  ├─ StudentSlideGenerator.Api/      # ASP.NET Core Web API 後端
│  └─ StudentSlideGenerator.Shared/   # 前後端共用模型
└─ StudentSlideGenerator.sln
\`\`\`

- **Web**：檔案選擇、簡報設定、投影片編輯、即時預覽與下載。
- **Api**：驗證與解析文件、呼叫 Gemini、產生投影片及匯出 HTML。
- **Shared**：\`SlideDeck\`、\`SlideItem\` 與主題模型。
- Gemini API Key 只存在 API，不會下載到瀏覽器。

## 第一版功能

- 上傳 PDF、DOCX、TXT 或 Markdown（20 MB 上限）
- PDF 原生視覺理解，DOCX、TXT、Markdown 文字擷取
- 兩階段 Gemini 流程：大綱規劃後再產生內容與排版
- 自動、精簡、標準、詳細四種內容深度
- 修改、新增與刪除投影片
- 即時 16:9 預覽
- 下載 HTML 簡報或結構化 JSON

## 本機執行

需要 .NET 8 SDK。第一次執行先還原套件：

\`\`\`bash
dotnet restore StudentSlideGenerator.sln
\`\`\`

設定 Gemini API Key。Windows PowerShell：

\`\`\`powershell
$env:GEMINI_API_KEY="your-key"
\`\`\`

Windows CMD：

\`\`\`cmd
set GEMINI_API_KEY=your-key
\`\`\`

開兩個終端機。

終端機 1 — API：

\`\`\`bash
dotnet run --project src/StudentSlideGenerator.Api --launch-profile http
\`\`\`

終端機 2 — Web：

\`\`\`bash
dotnet run --project src/StudentSlideGenerator.Web --launch-profile http
\`\`\`

瀏覽器開啟 \`http://localhost:5158\`。前端預設呼叫 \`http://localhost:5159\` 的 API。

若部署到不同網址，請修改：

- Web：\`src/StudentSlideGenerator.Web/wwwroot/appsettings.json\` 的 \`ApiBaseUrl\`
- API：\`src/StudentSlideGenerator.Api/appsettings.json\` 的 \`Cors:AllowedOrigins\`

## API

| Method | Path | 用途 |
|---|---|---|
| GET | \`/health\` | API 健康檢查 |
| POST | \`/api/decks/generate\` | 上傳文件並產生 \`SlideDeck\` |
| POST | \`/api/decks/export/html\` | 將編輯後的 \`SlideDeck\` 匯出為 HTML |

> 文件內容會由 API 傳送至 Gemini。請勿上傳未獲授權處理的機密文件。
