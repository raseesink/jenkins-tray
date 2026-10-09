using JenkinsTray.Core;
using Xunit;

namespace JenkinsTray.Core.Tests;

public sealed class StatusTests
{
    internal static ProjectSnapshot Snapshot(BuildResult result, long number = 1, string? error = null, bool building = false) =>
        new("server", new("job", "https://ci.test/job/plain/"), result, building, false,
            new(number, "https://ci.test/job/plain/" + number + "/", null, null, null, result),
            new(number, null, null, null, null, result), DateTimeOffset.UtcNow, error);
    [Fact] public void AggregateKeepsRawResultsAndPrioritizesFailure()
    {
        var snapshots = new[] { Snapshot(BuildResult.Success), Snapshot(BuildResult.Unstable), Snapshot(BuildResult.Failure, building: true) };
        Assert.Equal(new(Health.Failure, true), StatusPolicy.Aggregate(snapshots));
        Assert.Equal(BuildResult.Success, snapshots[0].Result);
        Assert.Equal(new(Health.Neutral, false), StatusPolicy.Aggregate([]));
        Assert.Equal(new(Health.Incomplete, true), StatusPolicy.Aggregate([Snapshot(BuildResult.Success, error: "offline", building: true)]));
    }
    [Theory]
    [InlineData(BuildResult.Unknown)] [InlineData(BuildResult.Disabled)] [InlineData(BuildResult.Aborted)]
    public void NonHealthyStatesAreNotAllGood(BuildResult result) => Assert.Equal(Health.Incomplete, StatusPolicy.Aggregate([Snapshot(result)]).Health);
    [Theory]
    [InlineData(BuildResult.Success, BuildResult.Failure, TransitionKind.Regression)]
    [InlineData(BuildResult.Success, BuildResult.Unstable, TransitionKind.Regression)]
    [InlineData(BuildResult.Unstable, BuildResult.Failure, TransitionKind.Regression)]
    [InlineData(BuildResult.Failure, BuildResult.Success, TransitionKind.Recovery)]
    [InlineData(BuildResult.Unstable, BuildResult.Success, TransitionKind.Recovery)]
    public void CompletedTransitionsNotifyOnce(BuildResult before, BuildResult after, TransitionKind kind)
    {
        var tracker = new TransitionTracker();
        Assert.Null(tracker.Observe(Snapshot(before)));
        Assert.Null(tracker.Observe(Snapshot(after, 2, "offline")));
        Assert.Equal(kind, tracker.Observe(Snapshot(after, 2))!.Kind);
        Assert.Null(tracker.Observe(Snapshot(after, 2)));
        Assert.Null(tracker.Observe(Snapshot(before, 1)));
    }
    [Fact] public void SameResultAndAbortedAreSilent()
    {
        var tracker = new TransitionTracker();
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Failure)));
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Failure, 2)));
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Aborted, 3)));
        tracker.Retain([]);
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Success, 4)));
    }
    [Fact] public void IdenticalProjectUrlsOnDifferentServersHaveIndependentBaselines()
    {
        var tracker = new TransitionTracker();
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Success)));
        Assert.Null(tracker.Observe(Snapshot(BuildResult.Failure) with { ServerId = "other" }));
        Assert.Equal(TransitionKind.Recovery, tracker.Observe(Snapshot(BuildResult.Success, 2) with { ServerId = "other" })!.Kind);
        Assert.Equal(TransitionKind.Regression, tracker.Observe(Snapshot(BuildResult.Failure, 2))!.Kind);
    }
}
