using StudentSlideGenerator.Models;

namespace StudentSlideGenerator.Services;

public interface IDeckGenerationService
{
    Task<SlideDeck> GenerateAsync(
        string fileName,
        long fileSize,
        string audience,
        int slideCount,
        CancellationToken cancellationToken = default);
}

public sealed class DemoDeckGenerationService : IDeckGenerationService
{
    private static readonly (string Title, string Content)[] Outline =
    [
        ("主題與學習目標", "• 說明本次內容的核心主題\n• 建立學習脈絡\n• 列出預期成果"),
        ("背景與問題", "• 交代教材背景\n• 定義要解決的問題\n• 說明問題的重要性"),
        ("核心觀念", "• 整理第一個重要觀念\n• 補充定義與關鍵字\n• 連結實際情境"),
        ("方法與流程", "• 將複雜內容拆成步驟\n• 標示輸入、處理與輸出\n• 提醒常見錯誤"),
        ("案例分析", "• 套用核心觀念\n• 比較不同做法\n• 說明結果與限制"),
        ("重點整理", "• 回顧三個主要重點\n• 提出可延伸討論\n• 留下問題與下一步")
    ];

    public Task<SlideDeck> GenerateAsync(
        string fileName,
        long fileSize,
        string audience,
        int slideCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var title = Path.GetFileNameWithoutExtension(fileName);
        var slides = Enumerable.Range(0, slideCount)
            .Select(index =>
            {
                var template = Outline[index % Outline.Length];
                return new SlideItem
                {
                    Title = index == 0 ? title : template.Title,
                    Content = index == 0
                        ? $"來源：{fileName}\n對象：{audience}\n檔案大小：{FormatBytes(fileSize)}"
                        : template.Content
                };
            })
            .ToList();

        return Task.FromResult(new SlideDeck
        {
            Title = title,
            SourceFileName = fileName,
            Audience = audience,
            Slides = slides
        });
    }

    private static string FormatBytes(long bytes) =>
        bytes < 1024 * 1024
            ? $"{bytes / 1024d:0.0} KB"
            : $"{bytes / 1024d / 1024d:0.0} MB";
}
