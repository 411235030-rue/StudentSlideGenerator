using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StudentSlideGenerator.Shared.Models;

namespace StudentSlideGenerator.Web.Services;

public sealed class DeckApiClient(HttpClient httpClient)
{
    public async Task<SlideDeck> GenerateAsync(
        string fileName,
        string contentType,
        byte[] fileBytes,
        string audience,
        string detailMode,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(fileBytes);

        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            fileContent.Headers.ContentType = mediaType;
        }

        form.Add(fileContent, "File", fileName);
        form.Add(new StringContent(audience), "Audience");
        form.Add(new StringContent(detailMode), "DetailMode");

        using var response = await httpClient.PostAsync(
            "api/decks/generate",
            form,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DeckApiException(await ReadErrorAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<SlideDeck>(
                   cancellationToken: cancellationToken)
               ?? throw new DeckApiException("後端沒有回傳簡報資料，請再試一次。");
    }

    public async Task<string> ExportHtmlAsync(
        SlideDeck deck,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/decks/export/html",
            deck,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DeckApiException(await ReadErrorAsync(response, cancellationToken));
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("detail", out var detail)
                && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }

            if (json.RootElement.TryGetProperty("title", out var title)
                && !string.IsNullOrWhiteSpace(title.GetString()))
            {
                return title.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall back to a safe status message below.
        }

        return $"後端請求失敗（HTTP {(int)response.StatusCode}）。";
    }
}

public sealed class DeckApiException(string message) : Exception(message);
