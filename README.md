# Student Slide Generator

以 .NET 8 Blazor Web App 與 Gemini API 製作的學生簡報生成器。

## 第一版功能

- 上傳 PDF、DOCX、TXT 或 Markdown（20 MB 上限）
- 設定簡報對象與頁數
- PDF 原生視覺理解（包含圖片、表格與圖表脈絡）
- DOCX、TXT 與 Markdown 文字擷取
- Gemini 結構化投影片產生
- 新增、刪除與修改投影片
- 即時 16:9 預覽
- 下載結構化 `slides.json`

> 文件內容會由後端傳送至 Gemini API。請勿上傳未獲授權處理的機密文件。

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
dotnet user-secrets set "Gemini:ApiKey" "your-key"
```

或在 Windows 的使用者環境變數新增：

```text
GEMINI_API_KEY=your-key
```

設定後必須重新開啟終端機或 Visual Studio，再執行 `dotnet run`。

`.env`、`appsettings.Local.json`、`bin` 與 `obj` 已加入 `.gitignore`。
