using System.Collections.Immutable;
using System.Threading.Channels;

namespace JenkinsTray.Core;

public sealed class PollingCoordinator(IJenkinsClientFactory clients, Func<Settings> settings,
    TimeProvider? timeProvider = null, int concurrency = 6) : IAsyncDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Channel<bool> refresh = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim requests = new(concurrency > 0 ? concurrency : throw new ArgumentOutOfRangeException(nameof(concurrency)));
    private readonly Dictionary<string, ProjectSnapshot> previous = new();
    private Task? loop;
    private bool disposed;
    public event Action<ImmutableArray<ProjectSnapshot>>? Updated;
    public void Start() => loop ??= Task.Run(() => RunAsync(stop.Token));
    public void Refresh() => refresh.Writer.TryWrite(true);

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var configuration = settings();
                var active = configuration.Servers.SelectMany(s => s.Projects.Select(p => s.Id + "|" + p.Url)).ToHashSet();
                foreach (var key in previous.Keys.Where(k => !active.Contains(k)).ToArray()) previous.Remove(key);
                lock (previous) Updated?.Invoke(previous.Values.ToImmutableArray());
                await Task.WhenAll(configuration.Servers.Select(s => RefreshServerAsync(s, token)));
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = DelayIntervalAsync(settings().PollIntervalSeconds, wait.Token);
                var manual = refresh.Reader.ReadAsync(wait.Token).AsTask();
                await Task.WhenAny(delay, manual);
                wait.Cancel();
                // Observe both cancelled waits; discard all requests coalesced before the next cycle.
                try { await Task.WhenAll(delay, manual); } catch (OperationCanceledException) { }
                while (refresh.Reader.TryRead(out _)) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task DelayIntervalAsync(int seconds, CancellationToken token)
    {
        // Task.Delay has a platform timer limit; every positive settings interval still remains valid.
        while (seconds > 0)
        {
            var chunk = Math.Min(seconds, 3_000_000);
            await Task.Delay(TimeSpan.FromSeconds(chunk), clock, token);
            seconds -= chunk;
        }
    }

    private async Task RefreshServerAsync(ServerSettings server, CancellationToken token)
    {
        IJenkinsClient client;
        try { client = await clients.CreateAsync(server, token); }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            foreach (var project in server.Projects) Publish(Unavailable(server, project, exception));
            return;
        }
        using (client)
        {
            using var serverRequests = new SemaphoreSlim(2);
            await Task.WhenAll(server.Projects.Select(async project =>
            {
                await serverRequests.WaitAsync(token);
                try
                {
                    await requests.WaitAsync(token);
                    try
                    {
                        ProjectSnapshot snapshot;
                        try { snapshot = await client.RefreshAsync(project, token); }
                        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                        { snapshot = Unavailable(server, project, exception); }
                        Publish(snapshot);
                    }
                    finally { requests.Release(); }
                }
                finally { serverRequests.Release(); }
            }));
        }
    }

    private ProjectSnapshot Unavailable(ServerSettings server, ProjectSettings project, Exception exception)
    {
        var error = exception switch
        {
            OperationCanceledException or TimeoutException => "Request timed out.",
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } => "Jenkins authentication was rejected.",
            HttpRequestException => "Jenkins request failed. Check connectivity, authentication and certificate policy.",
            InvalidDataException => "Jenkins returned invalid data or a stored credential is unavailable.",
            _ => "Monitoring failed. Check this server's configuration and credentials."
        };
        lock (previous)
            return previous.TryGetValue(server.Id + "|" + project.Url, out var old)
                ? old with { Error = error } : new(server.Id, project, BuildResult.Unknown, false, false, null, null, null, error);
    }

    private void Publish(ProjectSnapshot snapshot)
    {
        // Serialize publications so UI batches and transition processing observe one consistent order.
        lock (previous)
        {
            previous[snapshot.Identity] = snapshot;
            Updated?.Invoke(previous.Values.ToImmutableArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await stop.CancelAsync();
        if (loop is not null) await loop;
        requests.Dispose();
        stop.Dispose();
    }
}
