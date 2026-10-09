using System.Text.Json;

namespace JenkinsTray.Core;

public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;
    public bool Exists => File.Exists(Path);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath => OperatingSystem.IsMacOS()
        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "JenkinsTray", "settings.json")
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JenkinsTray", "settings.json");

    public async Task<Settings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Exists) return new();
        await using var stream = File.OpenRead(Path);
        var settings = await JsonSerializer.DeserializeAsync<Settings>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Settings are empty. Choose explicit recovery or import to replace this file.");
        Validate(settings);
        return settings;
    }

    public async Task SaveAsync(Settings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Validate(Settings settings)
    {
        if (settings.Version != 1) throw new InvalidDataException("Unsupported settings version.");
        if (settings.PollIntervalSeconds <= 0) throw new InvalidDataException("Polling interval must be positive.");
        if (settings.Servers.IsDefault || settings.CompatibilitySecretReferences.IsDefault)
            throw new InvalidDataException("Invalid settings collections.");
        var ids = new HashSet<string>();
        foreach (var server in settings.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Id) || !ids.Add(server.Id)) throw new InvalidDataException("Server identities must be unique.");
            ValidateUrl(server.Url);
            if (server.SecretReference is not null && string.IsNullOrWhiteSpace(server.Username))
                throw new InvalidDataException("A stored credential requires a username.");
            if (server.Projects.IsDefault) throw new InvalidDataException("Invalid project collection.");
            var urls = new HashSet<string>();
            foreach (var project in server.Projects)
            {
                ValidateUrl(project.Url);
                if (string.IsNullOrWhiteSpace(project.Name) || !urls.Add(project.Url)) throw new InvalidDataException("Projects need unique URLs and names.");
                if (!SameOrigin(new Uri(server.Url), new Uri(project.Url))) throw new InvalidDataException("Project URL must belong to its server.");
            }
        }
    }

    public static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Enter an absolute HTTP or HTTPS URL without embedded credentials, query, or fragment.");
        return uri;
    }
    public static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;
}
