using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private static readonly List<ListenEvent> _events = new();

    [HttpPost("listen")]
    public IActionResult LogListen([FromBody] ListenEvent ev)
    {
        ev.Timestamp = DateTime.UtcNow;
        _events.Add(ev);
        return Ok(new { logged = true });
    }

    [HttpGet("weekly")]
    public IActionResult WeeklyReport()
    {
        var week = DateTime.UtcNow.AddDays(-7);
        var recent = _events.Where(e => e.Timestamp >= week).ToList();

        return Ok(new
        {
            totalTracks = recent.Count,
            uniqueTracks = recent.Select(e => e.TrackId).Distinct().Count(),
            topArtists = recent.GroupBy(e => e.Artist).Select(g => new { artist = g.Key, plays = g.Count() }).OrderByDescending(g => g.plays).Take(5),
            topGenres = recent.GroupBy(e => e.Genre).Select(g => new { genre = g.Key, plays = g.Count() }).OrderByDescending(g => g.plays).Take(5),
            hourlyBreakdown = recent.GroupBy(e => e.Timestamp.Hour).Select(g => new { hour = g.Key, plays = g.Count() }).OrderBy(g => g.hour)
        });
    }

    [HttpGet("history")]
    public IActionResult History([FromQuery] int limit = 20)
        => Ok(_events.OrderByDescending(e => e.Timestamp).Take(limit).ToList());
}

public class ListenEvent
{
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? Genre { get; set; }
    public DateTime Timestamp { get; set; }
}
