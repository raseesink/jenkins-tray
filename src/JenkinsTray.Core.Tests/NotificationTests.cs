using System.Threading.Channels;
using JenkinsTray.Core;
using Xunit;

namespace JenkinsTray.Core.Tests;

public sealed class NotificationTests
{
    private sealed class Notifications(NotificationAvailability state) : IDesktopNotifications
    {
        public readonly Channel<string> Delivered = Channel.CreateUnbounded<string>();
        public int Count;
        public Task<NotificationAvailability> GetAvailabilityAsync(bool requestPermission, CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Count); Delivered.Writer.TryWrite(message); return Task.CompletedTask; }
        public void Dispose() { }
    }
    private static Settings Settings(bool enabled = true) => new() { NotificationsEnabled = enabled, Servers = [new() { Id = "server", Url = "https://ci.test/", DisplayName = "CI", Projects = [new("job", "https://ci.test/job/plain/")] }] };
    [Fact] public async Task NotificationsContainIdentityAndEmitOnePerTransition()
    {
        var native = new Notifications(NotificationAvailability.Available);
        await using var dispatcher = new NotificationDispatcher(native, () => Settings()); dispatcher.Start();
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Success)]);
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Failure, 2)]);
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Failure, 2)]);
        Assert.Contains("CI / job: Failure", await native.Delivered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Success, 3)]);
        Assert.Contains("Success", await native.Delivered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, native.Count);
    }
    [Fact] public async Task DeniedPermissionReportsFeedbackWithoutDelivery()
    {
        var native = new Notifications(NotificationAvailability.Denied);
        var reported = new TaskCompletionSource<NotificationAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var dispatcher = new NotificationDispatcher(native, () => Settings()); dispatcher.AvailabilityChanged += value => reported.TrySetResult(value); dispatcher.Start();
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Success)]); dispatcher.Observe([StatusTests.Snapshot(BuildResult.Failure, 2)]);
        Assert.Equal(NotificationAvailability.Denied, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5))); Assert.Equal(0, native.Count);
    }
    [Fact] public async Task DisabledPreferencesTrackBaselineButNeverReplayOldTransitions()
    {
        var native = new Notifications(NotificationAvailability.Available); var current = Settings(false);
        await using var dispatcher = new NotificationDispatcher(native, () => current); dispatcher.Start();
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Success)]); dispatcher.Observe([StatusTests.Snapshot(BuildResult.Failure, 2)]);
        current = Settings(); dispatcher.Observe([StatusTests.Snapshot(BuildResult.Failure, 2)]);
        dispatcher.Observe([StatusTests.Snapshot(BuildResult.Success, 3)]);
        Assert.Contains("Success", await native.Delivered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))); Assert.Equal(1, native.Count);
    }
}
