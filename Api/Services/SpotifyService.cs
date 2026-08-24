using System.Text.Json;

namespace Api.Services;

public class SpotifyService
{
    private readonly HttpClient _http;
    private string? _token;

    public SpotifyService(HttpClient http) => _http = http;

    public async Task<string?> GetTokenAsync(string clientId, string clientSecret)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(json);
        _token = data.GetProperty("access_token").GetString();
        return _token;
    }

    public Task<string?> SearchAsync(string query, string type = "track", int limit = 10)
        => GetWithRetryAsync($"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type={type}&limit={limit}");

    public Task<string?> GetPlaylistAsync(string playlistId)
        => GetWithRetryAsync($"https://api.spotify.com/v1/playlists/{playlistId}");

    public Task<string?> GetRecommendationsAsync(string seedGenres, int limit = 10)
        => GetWithRetryAsync($"https://api.spotify.com/v1/recommendations?seed_genres={Uri.EscapeDataString(seedGenres)}&limit={limit}");

    // ------------------------------------------------------------------
    // GET autenticado con reintento (retry/backoff) ante rate limits (429)
    // y errores de servidor (5xx). Máximo 3 intentos, espera exponencial,
    // respetando Retry-After cuando Spotify lo envía.
    // ------------------------------------------------------------------
    private async Task<string?> GetWithRetryAsync(string url)
    {
        if (_token == null) return null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadAsStringAsync();

            // 400 / 404 -> error definitivo, no reintentar.
            if ((int)resp.StatusCode == 400 || (int)resp.StatusCode == 404)
                return null;

            if (attempt < 3)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    && resp.Headers.TryGetValues("Retry-After", out var ra)
                    && int.TryParse(ra.FirstOrDefault(), out var secs))
                {
                    delay = TimeSpan.FromSeconds(Math.Max(1, secs));
                }
                await Task.Delay(delay);
            }
        }
        return null;
    }
}
