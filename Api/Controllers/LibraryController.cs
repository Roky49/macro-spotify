using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibraryController : ControllerBase
{
    private static readonly List<LibraryEntry> _library = new();
    private static readonly string LibraryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyMacro", "library.json");

    static LibraryController()
    {
        try
        {
            if (System.IO.File.Exists(LibraryFile))
            {
                var json = System.IO.File.ReadAllText(LibraryFile);
                var entries = JsonSerializer.Deserialize<List<LibraryEntry>>(json);
                if (entries != null) _library = entries;
            }
        }
        catch { }
    }

    public static void AddEntry(LibraryEntry entry)
    {
        _library.Add(entry);
        Save();
    }

    static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(LibraryFile)!;
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(LibraryFile, JsonSerializer.Serialize(_library, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? q)
    {
        var query = _library.AsEnumerable();
        if (!string.IsNullOrEmpty(q))
            query = query.Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
        return Ok(query.OrderByDescending(e => e.DownloadedAt).ToList());
    }

    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        var entry = _library.FirstOrDefault(e => e.Id == id);
        return entry != null ? Ok(entry) : NotFound();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var entry = _library.FirstOrDefault(e => e.Id == id);
        if (entry == null) return NotFound();

        try { if (System.IO.File.Exists(entry.FilePath)) System.IO.File.Delete(entry.FilePath); } catch { }
        _library.Remove(entry);
        Save();
        return NoContent();
    }

    [HttpGet("stats")]
    public IActionResult Stats() => Ok(new
    {
        totalTracks = _library.Count,
        totalSize = _library.Sum(e => e.FileSize),
        byFormat = _library.GroupBy(e => e.Format).Select(g => new { format = g.Key, count = g.Count(), size = g.Sum(e => e.FileSize) }),
        recentDownloads = _library.OrderByDescending(e => e.DownloadedAt).Take(5).ToList()
    });
}

public class LibraryEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Format { get; set; } = "mp3";
    public string? SourceUrl { get; set; }
    public long FileSize { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
