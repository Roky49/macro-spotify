using Microsoft.AspNetCore.Mvc;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpotifyController : ControllerBase
{
    private readonly SpotifyService _spotify;
    private readonly IConfiguration _config;

    public SpotifyController(SpotifyService spotify, IConfiguration config)
    {
        _spotify = spotify;
        _config = config;
    }

    [HttpPost("auth")]
    public async Task<IActionResult> Auth([FromBody] SpotifyAuthRequest req)
    {
        var token = await _spotify.GetTokenAsync(req.ClientId, req.ClientSecret);
        return token != null ? Ok(new { token }) : Unauthorized(new { error = "Credenciales inválidas" });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string type = "track", [FromQuery] int limit = 10)
    {
        var result = await _spotify.SearchAsync(q, type, limit);
        return result != null ? Ok(Json(result)) : BadRequest(new { error = "Error de búsqueda" });
    }

    [HttpGet("playlist/{id}")]
    public async Task<IActionResult> GetPlaylist(string id)
    {
        var result = await _spotify.GetPlaylistAsync(id);
        return result != null ? Ok(Json(result)) : NotFound();
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations([FromQuery] string genres, [FromQuery] int limit = 10)
    {
        var result = await _spotify.GetRecommendationsAsync(genres, limit);
        return result != null ? Ok(Json(result)) : BadRequest();
    }

    static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(s),
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    [HttpGet("genres")]
    public IActionResult Genres() => Ok(new[]
    {
        "pop", "rock", "hip-hop", "electronic", "jazz", "classical", "reggae",
        "blues", "country", "latin", "metal", "punk", "r-n-b", "soul", "alternative"
    });
}

public record SpotifyAuthRequest(string ClientId, string ClientSecret);
