namespace StudentSlideGenerator.Models;

public sealed class SlideDeck
{
    public string Title { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SlideItem> Slides { get; set; } = [];
}

public sealed class SlideItem
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Layout { get; set; } = "title-and-content";
    public string SpeakerNotes { get; set; } = string.Empty;
}
