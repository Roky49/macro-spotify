using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private static readonly string SyncDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyMacro", "sync");

    public SyncController() => Directory.CreateDirectory(SyncDir);

    [HttpPost("export")]
    public IActionResult Export()
    {
        var data = new
        {
            exportedAt = DateTime.UtcNow,
            version = "1.0",
            playlistsUrl = $"{Request.Scheme}://{Request.Host}/api/playlists",
            libraryUrl = $"{Request.Scheme}://{Request.Host}/api/library",
            statsUrl = $"{Request.Scheme}://{Request.Host}/api/stats/weekly"
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"spotify-macro-export-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpGet("remote")]
    public IActionResult RemoteInfo()
    {
        var host = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            remoteUrl = $"{host}/remote.html",
            apiUrl = host,
            qrCode = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(host + "/remote.html")}"
        });
    }
}
