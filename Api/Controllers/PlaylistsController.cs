using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private static readonly List<SmartPlaylist> _playlists = new();

    [HttpGet]
    public IActionResult GetAll() => Ok(_playlists);

    [HttpPost]
    public IActionResult Create([FromBody] SmartPlaylist pl)
    {
        pl.Id = _playlists.Any() ? _playlists.Max(p => p.Id) + 1 : 1;
        pl.CreatedAt = DateTime.UtcNow;
        _playlists.Add(pl);
        return CreatedAtAction(nameof(GetAll), new { id = pl.Id }, pl);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var pl = _playlists.FirstOrDefault(p => p.Id == id);
        if (pl == null) return NotFound();
        _playlists.Remove(pl);
        return NoContent();
    }

    [HttpPost("{id}/tracks")]
    public IActionResult AddTrack(int id, [FromBody] TrackInfo track)
    {
        var pl = _playlists.FirstOrDefault(p => p.Id == id);
        if (pl == null) return NotFound();
        pl.Tracks.Add(track);
        return Ok(pl);
    }

    [HttpGet("export/{format}")]
    public IActionResult Export(string format)
    {
        if (format == "json")
            return Ok(_playlists);
        if (format == "csv")
        {
            var lines = new List<string> { "Playlist,Canción,Artista" };
            foreach (var pl in _playlists)
                foreach (var t in pl.Tracks)
                    lines.Add($"{pl.Name},{t.Title},{t.Artist}");
            return File(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)), "text/csv", "playlists.csv");
        }
        return BadRequest(new { error = "Formato no soportado. Usa json o csv" });
    }
}

public class SmartPlaylist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Genre { get; set; }
    public string? Mood { get; set; }
    public List<TrackInfo> Tracks { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TrackInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? Album { get; set; }
    public string? ImageUrl { get; set; }
}
