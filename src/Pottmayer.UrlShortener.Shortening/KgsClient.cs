namespace Pottmayer.UrlShortener.Shortening;

public interface IKgsClient
{
    Task<(long RangeStart, long RangeEnd)> AllocateAsync(int size, CancellationToken ct);
}

public sealed class KgsClient(HttpClient http) : IKgsClient
{
    public async Task<(long RangeStart, long RangeEnd)> AllocateAsync(int size, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/keys/allocate", new { size }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AllocateResponse>(ct)
            ?? throw new InvalidOperationException("KGS returned an empty allocation response.");

        return (body.RangeStart, body.RangeEnd);
    }

    private sealed record AllocateResponse(long RangeStart, long RangeEnd);
}
