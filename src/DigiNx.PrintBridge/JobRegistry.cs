using System.Collections.Concurrent;

namespace DigiNx.PrintBridge;

internal sealed record CompletedJob(string status, string commandId, string receiptId, string receiptNumber, string printerId, int copies, DateTimeOffset processedAt);

internal sealed class JobRegistry
{
    private readonly ConcurrentDictionary<string, CompletedJob> _completed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private const int MaxJobs = 500;

    public bool TryGet(string commandId, out CompletedJob? job) => _completed.TryGetValue(commandId, out job);

    public void Add(CompletedJob job)
    {
        if (!_completed.TryAdd(job.commandId, job)) return;
        _order.Enqueue(job.commandId);
        while (_order.Count > MaxJobs && _order.TryDequeue(out var old)) _completed.TryRemove(old, out _);
    }

    public IReadOnlyList<CompletedJob> Recent(int max = 5) =>
        _completed.Values.OrderByDescending(x => x.processedAt).Take(max).ToList();
}
