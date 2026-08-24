namespace Api.Services;

public enum DownloadStatus
{
    Queued,      // en-cola: esperando a ser procesado
    Processing,  // en-progreso: está siendo descargado
    Completed,   // completado
    Failed       // fallo
}

public class DownloadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = "";
    public string Format { get; set; } = "mp3";
    public string? Quality { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public int Progress { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// Cola de descargas en memoria (thread-safe).
/// Encola trabajos, los expone via GetAll/Get, y un worker de fondo
/// los procesa en orden actualizando el estado en tiempo real.
/// El procesador por defecto es simulado (progreso 0->100) para que la
/// maquinaria de cola/progreso funcione sin depender de yt-dlp/red;
/// se puede inyectar un procesador real (yt-dlp) vía constructor/DI.
/// </summary>
public class DownloadQueue
{
    private readonly object _lock = new();
    private readonly List<DownloadJob> _jobs = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly Func<DownloadJob, Task> _processor;
    private Task? _worker;
    private bool _started;

    public DownloadQueue(Func<DownloadJob, Task>? processor = null)
        => _processor = processor ?? SimulatedProcessor;

    public DownloadJob Enqueue(string url, string format, string? quality)
    {
        DownloadJob job;
        lock (_lock)
        {
            job = new DownloadJob { Url = url, Format = format, Quality = quality };
            _jobs.Add(job);
        }
        _signal.Release();
        return job;
    }

    public IReadOnlyList<DownloadJob> GetAll()
    {
        lock (_lock) return _jobs.ToList();
    }

    public DownloadJob? Get(string id)
    {
        lock (_lock) return _jobs.FirstOrDefault(j => j.Id == id);
    }

    public void Update(DownloadJob job, Action<DownloadJob> mutate)
    {
        lock (_lock) mutate(job);
    }

    /// <summary>Arranca el worker de fondo que procesa la cola en orden.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _worker = Task.Run(WorkerLoop);
    }

    /// <summary>
    /// Procesa el siguiente trabajo "en-cola" (FIFO): pasa a en-progreso,
    /// ejecuta el procesador y marca completado o fallo. Expuesto para tests.
    /// </summary>
    public async Task ProcessNextAsync()
    {
        DownloadJob? job;
        lock (_lock) { job = _jobs.FirstOrDefault(j => j.Status == DownloadStatus.Queued); }
        if (job == null) return;

        Update(job, j =>
        {
            j.Status = DownloadStatus.Processing;
            j.Progress = 0;
            j.StartedAt = DateTime.UtcNow;
        });

        try
        {
            await _processor(job);
            Update(job, j =>
            {
                j.Status = DownloadStatus.Completed;
                j.Progress = 100;
                j.FinishedAt = DateTime.UtcNow;
            });
        }
        catch (Exception ex)
        {
            Update(job, j =>
            {
                j.Status = DownloadStatus.Failed;
                j.Error = ex.Message;
                j.FinishedAt = DateTime.UtcNow;
            });
        }
    }

    private async Task WorkerLoop()
    {
        while (true)
        {
            await _signal.WaitAsync();
            await ProcessNextAsync();
        }
    }

    private async Task SimulatedProcessor(DownloadJob job)
    {
        for (int p = 10; p <= 100; p += 10)
        {
            await Task.Delay(80);
            Update(job, j => j.Progress = p);
        }
    }
}
