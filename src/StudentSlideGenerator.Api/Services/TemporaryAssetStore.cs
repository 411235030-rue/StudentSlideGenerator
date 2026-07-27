using System.Collections.Concurrent;
using StudentSlideGenerator.Shared.Models;

namespace StudentSlideGenerator.Api.Services;

public sealed class TemporaryAssetStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, TemporaryAsset> assets = new();

    public IReadOnlyList<SourceAsset> Store(
        IEnumerable<ExtractedDocumentAsset> extractedAssets)
    {
        RemoveExpiredAssets();

        return extractedAssets
            .Select(asset =>
            {
                var id = Guid.NewGuid().ToString("N");

                assets[id] = new TemporaryAsset(
                    Id: id,
                    FileName: asset.FileName,
                    ContentType: asset.ContentType,
                    Bytes: asset.Bytes,
                    SourcePage: asset.SourcePage,
                    ExpiresAt: DateTimeOffset.UtcNow.Add(Lifetime));

                return new SourceAsset
                {
                    Id = id,
                    FileName = asset.FileName,
                    ContentType = asset.ContentType,
                    Url = $"/api/assets/{id}",
                    SourcePage = asset.SourcePage
                };
            })
            .ToList();
    }

    public bool TryGet(string id, out TemporaryAsset? asset)
    {
        RemoveExpiredAssets();

        if (assets.TryGetValue(id, out var found)
            && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            asset = found;
            return true;
        }

        asset = null;
        return false;
    }

    private void RemoveExpiredAssets()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var item in assets)
        {
            if (item.Value.ExpiresAt <= now)
            {
                assets.TryRemove(item.Key, out _);
            }
        }
    }
}

public sealed record TemporaryAsset(
    string Id,
    string FileName,
    string ContentType,
    byte[] Bytes,
    int? SourcePage,
    DateTimeOffset ExpiresAt);