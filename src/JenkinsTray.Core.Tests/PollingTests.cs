using System.Collections.Immutable;
using System.Threading.Channels;
using JenkinsTray.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JenkinsTray.Core.Tests;

public sealed class PollingTests
{
    private sealed class Factory(Func<ServerSettings, ProjectSettings, CancellationToken, Task<ProjectSnapshot>> refresh) : IJenkinsClientFactory
    {
        public Task<IJenkinsClient> CreateAsync(ServerSettings server, CancellationToken cancellationToken) => Task.FromResult<IJenkinsClient>(new Client(server, refresh));
        private sealed class Client(ServerSettings server, Func<ServerSettings, ProjectSettings, CancellationToken, Task<ProjectSnapshot>> refresh) : IJenkinsClient
        {
            public Task<ImmutableArray<ProjectSettings>> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(server.Projects);
            public Task<ProjectSnapshot> RefreshAsync(ProjectSettings project, CancellationToken cancellationToken) => refresh(server, project, cancellationToken);
            public void Dispose() { }
        }
    }
    private static ServerSettings Server(string id) => new() { Id = id, Url = "https://ci.test/", Projects = [new(id, "https://ci.test/job/" + id + "/")] };
    private static ProjectSnapshot Snapshot(ServerSettings server, ProjectSettings project) => new(server.Id, project, BuildResult.Success, false, false, null, null, DateTimeOffset.UtcNow);
    [Fact] public async Task PollingDoesNotCaptureTheCallingUiSynchronizationContext()
    {
        var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory((server, project, _) => Task.FromResult(Snapshot(server, project)));
        await using var coordinator = new PollingCoordinator(factory, () => new() { Servers = [Server("a")] });
        coordinator.Updated += snapshots => { if (!snapshots.IsEmpty) published.TrySetResult(SynchronizationContext.Current is null); };
        var prior = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(new SynchronizationContext()); coordinator.Start(); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        Assert.True(await published.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    private sealed class Clock : TimeProvider
    {
        public readonly FakeTimeProvider Fake = new();
        public readonly Channel<bool> Timers = Channel.CreateUnbounded<bool>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = Fake.CreateTimer(callback, state, dueTime, period);
            Timers.Writer.TryWrite(true); return timer;
        }
        public override DateTimeOffset GetUtcNow() => Fake.GetUtcNow();
    }
    [Fact] public async Task IntervalStartsAfterCompletionRatherThanStartup()
    {
        var clock = new Clock(); var starts = Channel.CreateUnbounded<bool>(); var release = Channel.CreateUnbounded<bool>();
        var factory = new Factory(async (server, project, token) =>
        {
            starts.Writer.TryWrite(true); await release.Reader.ReadAsync(token); return Snapshot(server, project);
        });
        await using var coordinator = new PollingCoordinator(factory, () => new() { Servers = [Server("a")] }, clock);
        coordinator.Start(); await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        clock.Fake.Advance(TimeSpan.FromHours(1)); Assert.False(starts.Reader.TryRead(out _));
        release.Writer.TryWrite(true); await clock.Timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        clock.Fake.Advance(TimeSpan.FromSeconds(14)); Assert.False(starts.Reader.TryRead(out _));
        clock.Fake.Advance(TimeSpan.FromSeconds(1)); await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact] public async Task ManyJobsOnBlockedServerCannotOccupyAllRequestSlots()
    {
        var fast = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var active = 0; var maximum = 0;
        var factory = new Factory(async (server, project, token) =>
        {
            var count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
            try
            {
                if (server.Id == "slow") await Task.Delay(Timeout.InfiniteTimeSpan, token);
                else fast.TrySetResult();
                return Snapshot(server, project);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var slow = Server("slow") with { Projects = Enumerable.Range(0, 20).Select(i => new ProjectSettings("job" + i, "https://ci.test/job/" + i + "/")).ToImmutableArray() };
        await using var coordinator = new PollingCoordinator(factory, () => new() { Servers = [slow, Server("fast")] }, concurrency: 3);
        coordinator.Start(); await fast.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.InRange(maximum, 1, 3);
    }
    [Fact] public async Task LargePositiveIntervalDoesNotStopTheCoordinator()
    {
        var clock = new Clock(); var starts = Channel.CreateUnbounded<bool>();
        var factory = new Factory((server, project, _) => { starts.Writer.TryWrite(true); return Task.FromResult(Snapshot(server, project)); });
        await using var coordinator = new PollingCoordinator(factory, () => new() { PollIntervalSeconds = int.MaxValue, Servers = [Server("a")] }, clock);
        coordinator.Start(); await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await clock.Timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Refresh(); await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact] public async Task StartupManualRequestsCoalesceAndNeverOverlap()
    {
        var started = Channel.CreateUnbounded<int>(); var release = Channel.CreateUnbounded<bool>(); var cycles = 0; var active = 0; var maximum = 0;
        var factory = new Factory(async (server, project, token) =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref active)); started.Writer.TryWrite(Interlocked.Increment(ref cycles));
            await release.Reader.ReadAsync(token); Interlocked.Decrement(ref active); return Snapshot(server, project);
        });
        await using var coordinator = new PollingCoordinator(factory, () => new() { PollIntervalSeconds = 3600, Servers = [Server("a")] });
        coordinator.Start(); Assert.Equal(1, await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        for (var i = 0; i < 20; i++) coordinator.Refresh();
        release.Writer.TryWrite(true); Assert.Equal(2, await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, maximum); Assert.Equal(2, cycles);
    }
    [Fact] public async Task ResponsiveServerPublishesBeforeBlockedServerAndStopCancels()
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory(async (server, project, token) =>
        {
            if (server.Id == "slow")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            }
            return Snapshot(server, project);
        });
        var coordinator = new PollingCoordinator(factory, () => new() { Servers = [Server("slow"), Server("fast")] });
        coordinator.Updated += snapshots => { if (snapshots.Any(s => s.ServerId == "fast")) published.TrySetResult(); };
        coordinator.Start(); await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact] public async Task PartialFailureKeepsOtherProjectsAvailable()
    {
        var published = new TaskCompletionSource<ImmutableArray<ProjectSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory((server, project, _) => server.Id == "bad" ? throw new HttpRequestException("offline") : Task.FromResult(Snapshot(server, project)));
        await using var coordinator = new PollingCoordinator(factory, () => new() { Servers = [Server("bad"), Server("good")] });
        coordinator.Updated += snapshots => { if (snapshots.Length == 2) published.TrySetResult(snapshots); };
        coordinator.Start(); var result = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Single(s => s.ServerId == "bad").Available); Assert.True(result.Single(s => s.ServerId == "good").Available);
        Assert.Equal(Health.Incomplete, StatusPolicy.Aggregate(result).Health);
    }
}
