using JenkinsTray.Core;
using Xunit;

namespace JenkinsTray.Core.Tests;

public sealed class MemorySecrets : ISecretStore
{
    public readonly Dictionary<string, string> Values = new();
    public int FailWriteAt { get; set; } = int.MaxValue;
    private int writes;
    public Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(reference));
    public Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        if (++writes == FailWriteAt) throw new InvalidOperationException("Secret storage refused write.");
        Values[reference] = secret; return Task.CompletedTask;
    }
    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) { Values.Remove(reference); return Task.CompletedTask; }
}
public sealed class SettingsTests : IDisposable
{
    private sealed class GatedSecrets : ISecretStore
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<string?>("secret");
        public async Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
        public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private readonly string directory = Path.Combine(Path.GetTempPath(), "jenkins-tray-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");
    public SettingsTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private static ServerSettings Server => new() { Id = "ci", Url = "https://ci.test/", Projects = [new("folder/main", "https://ci.test/job/folder/job/main/")] };
    [Fact] public async Task VersionedSettingsRoundTripWithSelectedProjects()
    {
        var store = new SettingsStore(SettingsPath); var settings = new Settings { Servers = [Server], PollIntervalSeconds = 27 };
        await store.SaveAsync(settings); var loaded = await store.LoadAsync();
        Assert.Equal(27, loaded.PollIntervalSeconds); Assert.Equal(Server.Projects[0], loaded.Servers[0].Projects[0]);
        Assert.False(loaded.Servers[0].IgnoreUntrustedCertificate);
    }
    [Theory] [InlineData(0, "https://ci.test/")] [InlineData(-1, "https://ci.test/")] [InlineData(15, "file:///bad")]
    [InlineData(15, "https://user:password@ci.test/")]
    public async Task InvalidSettingsPreservePreviousBytes(int interval, string url)
    {
        var store = new SettingsStore(SettingsPath); await store.SaveAsync(new()); var before = File.ReadAllBytes(SettingsPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new() { PollIntervalSeconds = interval, Servers = [Server with { Url = url }] }));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }
    [Fact] public async Task DamagedFileNeedsExplicitRecoveryAndGetsBackup()
    {
        await File.WriteAllTextAsync(SettingsPath, "damaged");
        var service = new SettingsService(new(SettingsPath), new MemorySecrets());
        await Assert.ThrowsAnyAsync<Exception>(() => service.LoadAsync()); Assert.True(service.NeedsRecovery);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new()));
        Assert.Equal("damaged", File.ReadAllText(SettingsPath));
        await service.RecoverAsync();
        Assert.Equal("damaged", File.ReadAllText(Directory.GetFiles(directory, "*.recovery-*").Single()));
        Assert.False(service.NeedsRecovery);
    }
    [Fact] public async Task CancelledPersistenceLeavesValidFileAndCurrentSettings()
    {
        var service = new SettingsService(new(SettingsPath), new MemorySecrets()); await service.SaveAsync(new() { PollIntervalSeconds = 31 });
        var before = File.ReadAllBytes(SettingsPath); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(new() { PollIntervalSeconds = 42 }, cancellation.Token));
        Assert.Equal(31, service.Current.PollIntervalSeconds); Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }
    [Fact] public async Task FailedDiskWriteRetainsPreviousSettingsAndRollsBackNewSecret()
    {
        if (OperatingSystem.IsWindows()) return; // Windows sharing/ACL acceptance is exercised separately.
        var secrets = new MemorySecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        await service.SaveAsync(new() { PollIntervalSeconds = 31 }); var before = File.ReadAllBytes(SettingsPath);
        var permissions = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(new() { PollIntervalSeconds = 42 }));
            Assert.Equal(31, service.Current.PollIntervalSeconds); Assert.Equal(before, File.ReadAllBytes(SettingsPath));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveServerAsync(Server with { Username = "user" }, "secret", false));
            Assert.Empty(service.Current.Servers); Assert.Empty(secrets.Values); Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        }
        finally { File.SetUnixFileMode(directory, permissions); }
    }
    [Fact] public async Task RefusedCredentialWriteHasNoPlaintextFallback()
    {
        var secrets = new MemorySecrets { FailWriteAt = 1 }; var service = new SettingsService(new(SettingsPath), secrets);
        await service.SaveAsync(new()); var before = File.ReadAllBytes(SettingsPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveServerAsync(Server with { Username = "user" }, "secret", false));
        Assert.Empty(service.Current.Servers); Assert.Empty(secrets.Values); Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }
    [Fact] public async Task CredentialReplacementAndRemovalUseNewReferences()
    {
        var secrets = new MemorySecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        await service.SaveServerAsync(Server with { Username = "user" }, "first-secret", false);
        var reference = service.Current.Servers.Single().SecretReference!; Assert.Equal("first-secret", await secrets.ReadAsync(reference));
        await service.SaveServerAsync(service.Current.Servers.Single(), "second-secret", false);
        Assert.Null(await secrets.ReadAsync(reference)); Assert.Single(secrets.Values);
        Assert.DoesNotContain("second-secret", File.ReadAllText(SettingsPath));
        await service.RemoveServerAsync("ci"); Assert.Empty(secrets.Values);
    }
    [Fact] public async Task FailedObserverCannotDeleteCommittedCredential()
    {
        var secrets = new MemorySecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        service.Changed += () => throw new InvalidOperationException("Observer failed.");
        await service.SaveServerAsync(Server with { Username = "user" }, "secret", false);
        var loaded = await new SettingsStore(SettingsPath).LoadAsync();
        Assert.Equal("secret", await secrets.ReadAsync(loaded.Servers[0].SecretReference!));
    }
    [Fact] public async Task PreferenceSaveCannotLoseOverlappingServerSave()
    {
        var secrets = new GatedSecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        var serverSave = service.SaveServerAsync(Server with { Username = "user" }, "secret", false);
        await secrets.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var preferences = service.SavePreferencesAsync(37, false);
        secrets.Release.TrySetResult(); await Task.WhenAll(serverSave, preferences);
        var loaded = await new SettingsStore(SettingsPath).LoadAsync();
        Assert.Equal(37, loaded.PollIntervalSeconds); Assert.False(loaded.NotificationsEnabled); Assert.Equal("ci", loaded.Servers.Single().Id);
    }
    [Fact] public async Task LegacyImportPreservesSourceAssociationsAndDeferredValues()
    {
        var source = Path.Combine(directory, "jenkins.configuration"); await File.WriteAllTextAsync(source, JenkinsClientTests.Fixture("legacy.json"));
        var bytes = File.ReadAllBytes(source); var secrets = new MemorySecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        await service.ImportAsync(source); var current = service.Current;
        Assert.Equal(bytes, File.ReadAllBytes(source)); Assert.Equal(27, current.PollIntervalSeconds); Assert.True(current.NotificationsEnabled);
        var server = current.Servers.Single(); Assert.Equal("user", server.Username); Assert.True(server.IgnoreUntrustedCertificate);
        Assert.Equal("folder/main", server.Projects.Single().Name); Assert.Contains("secret", secrets.Values.Values);
        Assert.Contains("build-token", secrets.Values.Values); Assert.Single(current.CompatibilitySecretReferences);
        var compatibility = current.Compatibility!.Value;
        Assert.True(compatibility.GetProperty("generalSettings").GetProperty("integrateWithClaimPlugin").GetBoolean());
        Assert.True(compatibility.GetProperty("servers")[0].GetProperty("projects")[0].GetProperty("acknowledged").GetBoolean());
        var json = File.ReadAllText(SettingsPath); Assert.DoesNotContain("c2VjcmV0", json); Assert.DoesNotContain("build-token", json);
        var reloaded = await new SettingsStore(SettingsPath).LoadAsync(); Assert.Equal(current.CompatibilitySecretReferences.ToArray(), reloaded.CompatibilitySecretReferences.ToArray());
    }
    [Fact] public async Task FailedImportRollsBackSecretsAndKeepsActiveAndSourceBytes()
    {
        var source = Path.Combine(directory, "jenkins.configuration"); await File.WriteAllTextAsync(source, JenkinsClientTests.Fixture("legacy.json"));
        var sourceBytes = File.ReadAllBytes(source); var secrets = new MemorySecrets { FailWriteAt = 2 };
        var service = new SettingsService(new(SettingsPath), secrets); await service.SaveAsync(new() { PollIntervalSeconds = 31 });
        var before = File.ReadAllBytes(SettingsPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(source));
        Assert.Equal(31, service.Current.PollIntervalSeconds); Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(source)); Assert.Empty(secrets.Values);
    }
    [Fact] public async Task InvalidImportWritesNothing()
    {
        var source = Path.Combine(directory, "bad.json"); await File.WriteAllTextAsync(source, "{\"servers\":[{\"url\":\"bad\",\"projects\":[]}]} ");
        var secrets = new MemorySecrets(); var service = new SettingsService(new(SettingsPath), secrets);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(source)); Assert.False(File.Exists(SettingsPath)); Assert.Empty(secrets.Values);
    }
}
