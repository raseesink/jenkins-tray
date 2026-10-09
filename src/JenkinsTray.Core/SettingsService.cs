using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JenkinsTray.Core;

public sealed class SettingsService(SettingsStore store, ISecretStore secrets)
{
    private readonly SemaphoreSlim writes = new(1);
    public Settings Current { get; private set; } = new();
    public bool NeedsRecovery { get; private set; }
    public event Action? Changed;
    public bool HasFile => store.Exists;

    public async Task LoadAsync(CancellationToken token = default)
    {
        try { Current = await store.LoadAsync(token); NeedsRecovery = false; }
        catch { NeedsRecovery = true; throw; }
    }

    public async Task SaveAsync(Settings candidate, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            if (NeedsRecovery) throw new InvalidOperationException("The damaged settings file is preserved. Choose explicit recovery or import first.");
            await CommitAsync(candidate, token);
        }
        finally { writes.Release(); }
    }

    public async Task SavePreferencesAsync(int intervalSeconds, bool notificationsEnabled, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            if (NeedsRecovery) throw new InvalidOperationException("Choose recovery or import before saving.");
            // Read current servers inside the write lock so an overlapping server save is retained.
            await CommitAsync(Current with { PollIntervalSeconds = intervalSeconds, NotificationsEnabled = notificationsEnabled }, token);
        }
        finally { writes.Release(); }
    }

    private async Task CommitAsync(Settings candidate, CancellationToken token)
    {
        await store.SaveAsync(candidate, token);
        Current = candidate;
        NeedsRecovery = false;
        // Observer failures cannot roll back a successfully persisted configuration or its secrets.
        if (Changed is { } changed)
            foreach (Action observer in changed.GetInvocationList())
            {
                try { observer(); } catch { }
            }
    }

    public async Task SaveServerAsync(ServerSettings server, string? replacementSecret, bool removeCredential, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        string? newReference = null;
        try
        {
            if (NeedsRecovery) throw new InvalidOperationException("Choose recovery or import before saving.");
            var old = Current.Servers.FirstOrDefault(s => s.Id == server.Id);
            server = server with { SecretReference = removeCredential ? null : old?.SecretReference };
            if (replacementSecret is not null && !removeCredential)
            {
                newReference = "JenkinsTray/server/" + Guid.NewGuid().ToString("N");
                server = server with { SecretReference = newReference };
            }
            var servers = Current.Servers.Where(s => s.Id != server.Id).Append(server).ToImmutableArray();
            var candidate = Current with { Servers = servers };
            SettingsStore.Validate(candidate);
            if (newReference is not null) await secrets.WriteAsync(newReference, replacementSecret!, token);
            await CommitAsync(candidate, token);
            newReference = null;
            // Commit before removing the superseded credential; a refused cleanup cannot break active settings.
            if (old?.SecretReference is { } obsolete && obsolete != server.SecretReference)
                await secrets.RemoveAsync(obsolete, token);
        }
        finally
        {
            try { if (newReference is not null) await secrets.RemoveAsync(newReference, CancellationToken.None); }
            finally { writes.Release(); }
        }
    }

    public async Task RemoveServerAsync(string id, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            if (NeedsRecovery) throw new InvalidOperationException("Choose recovery or import before saving.");
            var old = Current.Servers.First(s => s.Id == id);
            await CommitAsync(Current with { Servers = Current.Servers.Where(s => s.Id != id).ToImmutableArray() }, token);
            if (old.SecretReference is { } reference) await secrets.RemoveAsync(reference, token);
        }
        finally { writes.Release(); }
    }

    public async Task RecoverAsync(CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            // Explicit recovery preserves a byte-for-byte backup of the damaged source.
            if (store.Exists) File.Copy(store.Path, store.Path + ".recovery-" + Guid.NewGuid().ToString("N"));
            await CommitAsync(new(), token);
        }
        finally { writes.Release(); }
    }

    public async Task ImportAsync(string source, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        var created = new List<string>();
        try
        {
            if (System.IO.Path.GetFullPath(source) == System.IO.Path.GetFullPath(store.Path))
                throw new InvalidDataException("Select a legacy file, not the active settings file.");
            var root = JsonNode.Parse(await File.ReadAllTextAsync(source, token))?.AsObject()
                ?? throw new InvalidDataException("Legacy configuration is empty.");
            var serversNode = root["servers"] as JsonArray ?? throw new InvalidDataException("Legacy servers are missing.");
            var pendingSecrets = new Dictionary<string, string>();
            var deferredReferences = ImmutableArray.CreateBuilder<string>();
            var servers = ImmutableArray.CreateBuilder<ServerSettings>();
            foreach (var item in serversNode)
            {
                var node = item?.AsObject() ?? throw new InvalidDataException("Invalid legacy server.");
                string? username = null;
                string? reference = null;
                if (node["credentials"] is { } credentialNode)
                {
                    var credentials = credentialNode.AsArray();
                    if (credentials.Count != 2) throw new InvalidDataException("Invalid legacy credentials.");
                    username = credentials[0]?.GetValue<string>() ?? throw new InvalidDataException("Invalid legacy username.");
                    var encoded = credentials[1]?.GetValue<string>() ?? throw new InvalidDataException("Invalid legacy credential encoding.");
                    reference = NewReference();
                    pendingSecrets.Add(reference, Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                    // Retain the credential association, replacing its encoded value with a protected reference.
                    node["credentials"] = new JsonObject { ["username"] = username, ["secretReference"] = reference };
                }
                var projects = ImmutableArray.CreateBuilder<ProjectSettings>();
                foreach (var projectItem in node["projects"] as JsonArray ?? throw new InvalidDataException("Legacy projects are missing."))
                {
                    var project = projectItem?.AsObject() ?? throw new InvalidDataException("Invalid legacy project.");
                    projects.Add(new(Required(project, "name"), Required(project, "url"), project["displayName"]?.GetValue<string>()));
                    if (project["token"] is { } tokenNode)
                    {
                        var tokenReference = NewReference();
                        pendingSecrets.Add(tokenReference, tokenNode.GetValue<string>());
                        deferredReferences.Add(tokenReference);
                        project.Remove("token");
                        project["tokenSecretReference"] = tokenReference;
                    }
                }
                servers.Add(new()
                {
                    Url = Required(node, "url"), DisplayName = node["displayName"]?.GetValue<string>() ?? "",
                    Username = username, SecretReference = reference,
                    IgnoreUntrustedCertificate = node["ignoreUntrustedCertificate"]?.GetValue<bool>() ?? false,
                    Projects = projects.ToImmutable()
                });
            }
            var candidate = new Settings
            {
                Servers = servers.ToImmutable(),
                PollIntervalSeconds = root["generalSettings"]?["refreshIntervalInSeconds"]?.GetValue<int>() ?? 15,
                NotificationsEnabled = root["notificationSettings"]?["balloonNotifications"]?.GetValue<bool>() ?? false,
                Compatibility = JsonSerializer.SerializeToElement(root),
                CompatibilitySecretReferences = deferredReferences.ToImmutable()
            };
            SettingsStore.Validate(candidate);
            foreach (var (reference, secret) in pendingSecrets)
            {
                // Include even a refused write in rollback: a provider can fail after writing.
                created.Add(reference);
                await secrets.WriteAsync(reference, secret, token);
            }
            if (NeedsRecovery && store.Exists) File.Copy(store.Path, store.Path + ".recovery-" + Guid.NewGuid().ToString("N"));
            await CommitAsync(candidate, token);
            created.Clear();
        }
        finally
        {
            try
            {
                foreach (var reference in created)
                {
                    try { await secrets.RemoveAsync(reference, CancellationToken.None); }
                    catch { /* An orphan remains protected; the active configuration stays unchanged. */ }
                }
            }
            finally { writes.Release(); }
        }
    }

    private static string NewReference() => "JenkinsTray/import/" + Guid.NewGuid().ToString("N");
    private static string Required(JsonObject node, string name) => node[name]?.GetValue<string>() is { Length: > 0 } value
        ? value : throw new InvalidDataException("Legacy " + name + " is missing.");
}
