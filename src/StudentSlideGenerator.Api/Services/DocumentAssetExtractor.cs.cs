using System.IO.Compression;
using UglyToad.PdfPig;

namespace StudentSlideGenerator.Api.Services;

public sealed record ExtractedDocumentAsset(
    string Id,
    string FileName,
    string ContentType,
    byte[] Bytes,
    int? SourcePage = null);

public static class DocumentAssetExtractor
{
    private const int MaxAssets = 12;
    private const int MaxAssetSize = 2 * 1024 * 1024;

    public static IReadOnlyList<ExtractedDocumentAsset> Extract(
        string fileName,
        byte[] fileBytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        var assets = extension switch
        {
            ".docx" => ExtractDocxImages(fileBytes),
            ".pdf" => ExtractPdfImages(fileBytes),
            _ => []
        };

        return assets
            .Take(MaxAssets)
            .Select((asset, index) => asset with
            {
                Id = $"asset-{index + 1:000}"
            })
            .ToList();
    }

    private static List<ExtractedDocumentAsset> ExtractDocxImages(byte[] fileBytes)
    {
        var assets = new List<ExtractedDocumentAsset>();

        using var archive = new ZipArchive(
            new MemoryStream(fileBytes),
            ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(
                    "word/media/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contentType = GetImageContentType(
                Path.GetExtension(entry.FullName));

            if (contentType is null || entry.Length == 0 || entry.Length > MaxAssetSize)
            {
                continue;
            }

            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);

            assets.Add(new ExtractedDocumentAsset(
                Id: string.Empty,
                FileName: Path.GetFileName(entry.FullName),
                ContentType: contentType,
                Bytes: output.ToArray()));
        }

        return assets;
    }

    private static List<ExtractedDocumentAsset> ExtractPdfImages(byte[] fileBytes)
    {
        var assets = new List<ExtractedDocumentAsset>();

        using var document = PdfDocument.Open(new MemoryStream(fileBytes));

        foreach (var page in document.GetPages())
        {
            foreach (var image in page.GetImages())
            {
                if (!image.TryGetPng(out var pngBytes)
                    || pngBytes.Length == 0
                    || pngBytes.Length > MaxAssetSize)
                {
                    continue;
                }

                assets.Add(new ExtractedDocumentAsset(
                    Id: string.Empty,
                    FileName: $"pdf-page-{page.Number}-image-{assets.Count + 1}.png",
                    ContentType: "image/png",
                    Bytes: pngBytes,
                    SourcePage: page.Number));
            }
        }

        return assets;
    }

    private static string? GetImageContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null
        };
}
