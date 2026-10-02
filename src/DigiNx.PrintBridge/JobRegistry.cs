using System.Collections.Concurrent;

namespace DigiNx.PrintBridge;

internal sealed record PrintJob(
    string status,
    string commandId,
    string receiptId,
    string receiptNumber,
    string printerId,
    int copies,
    DateTimeOffset acceptedAt,
    DateTimeOffset? processedAt,
    string? errorCode
);

internal sealed class JobRegistry
{
    private readonly ConcurrentDictionary<string, PrintJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private const int MaxJobs = 500;

    public bool TryGet(string commandId, out PrintJob? job) => _jobs.TryGetValue(commandId, out job);

    public bool TryAccept(PrintJob job, out PrintJob current)
    {
        if (_jobs.TryAdd(job.commandId, job))
        {
            _order.Enqueue(job.commandId);
            Trim();
            current = job;
            return true;
        }
        current = _jobs[job.commandId];
        return false;
    }

    public void MarkPrinting(string commandId) => Update(commandId, job => job with { status = "printing", errorCode = null });

    public void MarkPrinted(string commandId) => Update(commandId, job => job with
    {
        status = "printed",
        processedAt = DateTimeOffset.UtcNow,
        errorCode = null,
    });

    public void MarkFailed(string commandId, string errorCode) => Update(commandId, job => job with
    {
        status = "failed",
        processedAt = DateTimeOffset.UtcNow,
        errorCode = string.IsNullOrWhiteSpace(errorCode) ? "PRINT_FAILED" : errorCode,
    });

    private void Update(string commandId, Func<PrintJob, PrintJob> update)
    {
        while (_jobs.TryGetValue(commandId, out var current))
        {
            var next = update(current);
            if (_jobs.TryUpdate(commandId, next, current)) return;
        }
    }

    private void Trim()
    {
        while (_order.Count > MaxJobs && _order.TryDequeue(out var old)) _jobs.TryRemove(old, out _);
    }

    public IReadOnlyList<PrintJob> Recent(int max = 5) =>
        _jobs.Values
            .OrderByDescending(x => x.processedAt ?? x.acceptedAt)
            .Take(max)
            .ToList();
}
