# Student Slide Generator

以 .NET 8 Blazor Web App 製作的學生簡報生成器 MVP。

## 第一版功能

- 上傳 PDF、DOCX、TXT 或 Markdown（20 MB 上限）
- 設定簡報對象與頁數
- 產生可編輯的示範投影片草稿
- 新增、刪除與修改投影片
- 即時 16:9 預覽
- 下載結構化 `slides.json`

> 目前 `DemoDeckGenerationService` 不會讀取或外傳文件內容。後續可在
> `IDeckGenerationService` 後方串接文件解析、OpenAI／其他模型與 PPTX 匯出。

## 執行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
dotnet restore
dotnet run
```

開啟終端顯示的本機網址，預設為 `http://localhost:5158`。

## 安全設定

API Key 必須透過環境變數或本機 Secret Manager 提供，不得寫入程式碼或提交至 Git。

```bash
dotnet user-secrets init
dotnet user-secrets set "AI:ApiKey" "your-key"
```

`.env`、`appsettings.Local.json`、`bin` 與 `obj` 已加入 `.gitignore`。
