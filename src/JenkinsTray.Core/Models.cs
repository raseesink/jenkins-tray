using System.Collections.Immutable;
using System.Text.Json;

namespace JenkinsTray.Core;

public enum BuildResult { Unknown, Disabled, Aborted, Success, Unstable, Failure }
public enum Health { Neutral, Success, Incomplete, Unstable, Failure }
public enum TransitionKind { Regression, Recovery }
public enum NotificationAvailability { Unknown, Available, Denied, Unavailable }

public sealed record ProjectSettings(string Name, string Url, string? DisplayName = null);
public sealed record ServerSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Url { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Username { get; init; }
    public string? SecretReference { get; init; }
    public bool IgnoreUntrustedCertificate { get; init; }
    public ImmutableArray<ProjectSettings> Projects { get; init; } = [];
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Url : DisplayName;
}

public sealed record Settings
{
    public int Version { get; init; } = 1;
    public int PollIntervalSeconds { get; init; } = 15;
    public bool NotificationsEnabled { get; init; } = true;
    public ImmutableArray<ServerSettings> Servers { get; init; } = [];
    public JsonElement? Compatibility { get; init; }
    // Native secret references for deferred project tokens, never ordinary JSON secrets.
    public ImmutableArray<string> CompatibilitySecretReferences { get; init; } = [];
}

public sealed record BuildInfo(long Number, string? Url, string? DisplayName,
    DateTimeOffset? Timestamp, TimeSpan? Duration, BuildResult Result, bool Building = false);
public sealed record ProjectSnapshot(string ServerId, ProjectSettings Project,
    BuildResult Result, bool Building, bool Queued, BuildInfo? LatestBuild,
    BuildInfo? CompletedBuild, DateTimeOffset? RefreshedAt, string? Error = null)
{
    public string Identity => ServerId + "|" + Project.Url;
    public bool Available => Error is null;
    public string? ConsoleUrl => LatestBuild?.Url is { } url ? url.TrimEnd('/') + "/console" : null;
}
public sealed record BuildTransition(ProjectSnapshot Snapshot, TransitionKind Kind);
public sealed record AggregateStatus(Health Health, bool Building);

public static class StatusPolicy
{
    public static AggregateStatus Aggregate(IEnumerable<ProjectSnapshot> snapshots)
    {
        var projects = snapshots.ToArray();
        var health = projects.Length == 0 ? Health.Neutral
            : projects.Any(p => p.Result == BuildResult.Failure && p.Available) ? Health.Failure
            : projects.Any(p => p.Result == BuildResult.Unstable && p.Available) ? Health.Unstable
            : projects.Any(p => !p.Available || p.Result != BuildResult.Success) ? Health.Incomplete
            : Health.Success;
        return new(health, projects.Any(p => p.Building));
    }
}

public sealed class TransitionTracker
{
    private readonly Dictionary<string, BuildInfo> previous = new();

    public BuildTransition? Observe(ProjectSnapshot snapshot)
    {
        // Unavailable data cannot reset a known baseline or generate a transition.
        if (!snapshot.Available || snapshot.CompletedBuild is not { } build ||
            build.Result is not (BuildResult.Success or BuildResult.Unstable or BuildResult.Failure)) return null;
        previous.TryGetValue(snapshot.Identity, out var old);
        if (old is not null && build.Number <= old.Number) return null;
        previous[snapshot.Identity] = build;
        if (old is null) return null;
        if ((old.Result == BuildResult.Success && build.Result is BuildResult.Unstable or BuildResult.Failure) ||
            (old.Result == BuildResult.Unstable && build.Result == BuildResult.Failure))
            return new(snapshot, TransitionKind.Regression);
        if (old.Result is BuildResult.Unstable or BuildResult.Failure && build.Result == BuildResult.Success)
            return new(snapshot, TransitionKind.Recovery);
        return null;
    }

    public void Retain(IEnumerable<string> identities)
    {
        var active = identities.ToHashSet();
        foreach (var key in previous.Keys.Where(k => !active.Contains(k)).ToArray()) previous.Remove(key);
    }
}

public interface ISecretStore
{
    Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default);
    Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default);
    Task RemoveAsync(string reference, CancellationToken cancellationToken = default);
}
public interface IDesktopNotifications : IDisposable
{
    Task<NotificationAvailability> GetAvailabilityAsync(bool requestPermission, CancellationToken cancellationToken = default);
    Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
public interface IUrlLauncher { void Open(string url); }
