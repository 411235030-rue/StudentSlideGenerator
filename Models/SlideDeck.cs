namespace StudentSlideGenerator.Models;

public sealed class SlideDeck
{
    public string Title { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public PresentationTheme Theme { get; set; } = new();
    public List<SlideItem> Slides { get; set; } = [];
}

public sealed class PresentationTheme
{
    public string StyleName { get; set; } = "Editorial";
    public string BackgroundColor { get; set; } = "#F5F1E8";
    public string SurfaceColor { get; set; } = "#FFFFFF";
    public string PrimaryColor { get; set; } = "#14213D";
    public string AccentColor { get; set; } = "#E76F51";
    public string TextColor { get; set; } = "#172033";
    public string MutedTextColor { get; set; } = "#667085";
    public string FontFamily { get; set; } = "Noto Sans TC";
}

public sealed class SlideItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Layout { get; set; } = "title-and-content";
    public string SectionLabel { get; set; } = string.Empty;
    public string BackgroundVariant { get; set; } = "base";
    public string VisualBrief { get; set; } = string.Empty;
    public string SpeakerNotes { get; set; } = string.Empty;
}
