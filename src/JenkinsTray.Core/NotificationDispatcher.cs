using System.Collections.Immutable;
using System.Threading.Channels;

namespace JenkinsTray.Core;

public sealed class NotificationDispatcher(IDesktopNotifications notifications, Func<Settings> settings) : IAsyncDisposable
{
    private readonly TransitionTracker tracker = new();
    private readonly Channel<BuildTransition> pending = Channel.CreateUnbounded<BuildTransition>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private bool disposed;
    public event Action<NotificationAvailability>? AvailabilityChanged;
    public void Start() => worker ??= Task.Run(() => RunAsync(stop.Token));
    public void Observe(ImmutableArray<ProjectSnapshot> snapshots)
    {
        var current = settings();
        var identities = current.Servers.SelectMany(s => s.Projects.Select(p => s.Id + "|" + p.Url)).ToHashSet();
        tracker.Retain(identities);
        foreach (var snapshot in snapshots.Where(s => identities.Contains(s.Identity)))
        {
            var transition = tracker.Observe(snapshot);
            if (current.NotificationsEnabled && transition is not null) pending.Writer.TryWrite(transition);
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await foreach (var transition in pending.Reader.ReadAllAsync(token))
            {
                if (!settings().NotificationsEnabled) continue;
                try
                {
                    var availability = await notifications.GetAvailabilityAsync(false, token);
                    AvailabilityChanged?.Invoke(availability);
                    if (availability != NotificationAvailability.Available) continue;
                    var snapshot = transition.Snapshot;
                    var server = settings().Servers.FirstOrDefault(s => s.Id == snapshot.ServerId);
                    if (server is null || !server.Projects.Any(p => p.Url == snapshot.Project.Url)) continue;
                    await notifications.ShowAsync("Jenkins Tray — " + transition.Kind,
                        server.Label + " / " + snapshot.Project.Name + ": " + snapshot.CompletedBuild!.Result, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { AvailabilityChanged?.Invoke(NotificationAvailability.Unavailable); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await stop.CancelAsync();
        if (worker is not null) await worker;
        stop.Dispose();
    }
}
