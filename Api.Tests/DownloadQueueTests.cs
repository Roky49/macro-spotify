using Api.Services;
using Xunit;

namespace Api.Tests;

public class DownloadQueueTests
{
    [Fact]
    public void Enqueue_AddsTwoJobs_InQueuedState()
    {
        var queue = new DownloadQueue();

        var j1 = queue.Enqueue("https://url/1", "mp3", "0");
        var j2 = queue.Enqueue("https://url/2", "flac", "0");

        var all = queue.GetAll();

        // Ambas descargas estan en la cola
        Assert.Equal(2, all.Count);
        Assert.NotEqual(j1.Id, j2.Id);

        // Ambas en estado "en-cola" inicial
        Assert.All(all, j => Assert.Equal(DownloadStatus.Queued, j.Status));
        Assert.Equal(DownloadStatus.Queued, j1.Status);
        Assert.Equal(DownloadStatus.Queued, j2.Status);

        // Conservan su orden FIFO
        Assert.Equal(j1.Id, all[0].Id);
        Assert.Equal(j2.Id, all[1].Id);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenProcessorCompletes_JobChangesToCompleted()
    {
        var queue = new DownloadQueue(job => Task.CompletedTask); // procesador instantaneo
        var job = queue.Enqueue("https://url/1", "mp3", "0");
        Assert.Equal(DownloadStatus.Queued, job.Status);

        await queue.ProcessNextAsync();

        Assert.Equal(DownloadStatus.Completed, job.Status);
        Assert.Equal(100, job.Progress);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenProcessorThrows_JobChangesToFailed()
    {
        var queue = new DownloadQueue(job => throw new InvalidOperationException("boom"));
        var job = queue.Enqueue("https://url/1", "mp3", "0");

        await queue.ProcessNextAsync();

        Assert.Equal(DownloadStatus.Failed, job.Status);
        Assert.Contains("boom", job.Error);
    }

    [Fact]
    public async Task ProcessNextAsync_ProcessesJobsInOrder()
    {
        var order = new List<string>();
        var queue = new DownloadQueue(job =>
        {
            lock (order) order.Add(job.Url);
            return Task.CompletedTask;
        });
        var j1 = queue.Enqueue("a", "mp3", "0");
        var j2 = queue.Enqueue("b", "mp3", "0");

        await queue.ProcessNextAsync();
        await queue.ProcessNextAsync();

        Assert.Equal(DownloadStatus.Completed, j1.Status);
        Assert.Equal(DownloadStatus.Completed, j2.Status);
        Assert.Equal(new[] { "a", "b" }, order);
    }
}
