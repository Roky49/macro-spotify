using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Endpoints de la cola de descargas.
///   GET  /api/downloads        -> lista + estado de cada descarga
///   GET  /api/downloads/{id}   -> estado de una descarga
///   POST /api/downloads        -> encolar una nueva descarga
/// </summary>
[ApiController]
[Route("api/downloads")]
public class DownloadsController : ControllerBase
{
    private readonly DownloadQueue _queue;

    public DownloadsController(DownloadQueue queue) => _queue = queue;

    [HttpGet]
    public IActionResult List()
    {
        // Ordenar: en-cola/procesando primero, luego completado/fallo (por tiempo)
        var jobs = _queue.GetAll()
            .OrderBy(j => j.Status == DownloadStatus.Queued || j.Status == DownloadStatus.Processing ? 0 : 1)
            .ThenBy(j => j.CreatedAt)
            .ToList();
        return Ok(jobs);
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var job = _queue.Get(id);
        return job != null ? Ok(job) : NotFound(new { error = "Descarga no encontrada" });
    }

    [HttpPost]
    public IActionResult Enqueue([FromBody] DownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            return BadRequest(new { error = "URL requerida" });

        var job = _queue.Enqueue(req.Url, req.Format ?? "mp3", req.Quality ?? "0");
        return Ok(job);
    }
}
