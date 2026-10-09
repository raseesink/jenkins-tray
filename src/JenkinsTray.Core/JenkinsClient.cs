using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JenkinsTray.Core;

public interface IJenkinsClient : IDisposable
{
    Task<ImmutableArray<ProjectSettings>> DiscoverAsync(CancellationToken cancellationToken);
    Task<ProjectSnapshot> RefreshAsync(ProjectSettings project, CancellationToken cancellationToken);
}
public interface IJenkinsClientFactory
{
    Task<IJenkinsClient> CreateAsync(ServerSettings server, CancellationToken cancellationToken);
}
public sealed class JenkinsClientFactory(ISecretStore secrets) : IJenkinsClientFactory
{
    public async Task<IJenkinsClient> CreateAsync(ServerSettings server, CancellationToken cancellationToken)
    {
        var secret = server.SecretReference is { } reference
            ? await secrets.ReadAsync(reference, cancellationToken).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken) : null;
        if (server.SecretReference is not null && secret is null) throw new InvalidOperationException("Stored credential is unavailable. Edit the server credentials.");
        return new JenkinsClient(server, secret);
    }
}

public sealed class JenkinsClient : IJenkinsClient
{
    private readonly ServerSettings server;
    private readonly HttpClient http;

    public static HttpClientHandler CreateHandler(ServerSettings server) => new()
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = server.IgnoreUntrustedCertificate
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator : null
    };

    public JenkinsClient(ServerSettings server, string? secret, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        this.server = server;
        SettingsStore.ValidateUrl(server.Url);
        http = new HttpClient(handler ?? CreateHandler(server)) { Timeout = timeout ?? TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
        if (!string.IsNullOrEmpty(server.Username))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(server.Username + ":" + (secret ?? ""))));
    }

    private async Task<XElement> ReadXmlAsync(string url, string tree, CancellationToken cancellationToken)
    {
        var uri = SettingsStore.ValidateUrl(url);
        if (!SettingsStore.SameOrigin(new Uri(server.Url), uri)) throw new InvalidDataException("Jenkins returned a URL on another server.");
        var endpoint = url.TrimEnd('/') + "/api/xml?tree=" + Uri.EscapeDataString(tree);
        using var response = await http.GetAsync(endpoint, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("Jenkins authentication was rejected.", null, response.StatusCode);
        if ((int)response.StatusCode is >= 300 and < 400) throw new HttpRequestException("Jenkins redirected the request. Configure its canonical URL.");
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024
        });
        return XElement.Load(reader);
    }

    public async Task<ImmutableArray<ProjectSettings>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var projects = ImmutableArray.CreateBuilder<ProjectSettings>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Url, string Prefix)>();
        pending.Enqueue((server.Url.TrimEnd('/') + "/", ""));
        while (pending.TryDequeue(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(entry.Url)) continue;
            var root = await ReadXmlAsync(entry.Url, "jobs[name,displayName,url,color,_class]", cancellationToken);
            foreach (var job in root.Elements("job"))
            {
                var name = Required(job, "name");
                var url = Required(job, "url").TrimEnd('/') + "/";
                var fullName = entry.Prefix + name;
                var type = (string?)job.Element("_class") ?? (string?)job.Attribute("_class") ?? "";
                // Jenkins supplies color on selectable leaf jobs; folders/multibranch containers omit it.
                if (type.Contains("Folder", StringComparison.OrdinalIgnoreCase) || type.Contains("MultiBranch", StringComparison.OrdinalIgnoreCase) || job.Element("color") is null)
                    pending.Enqueue((url, fullName + "/"));
                else if (visited.Add(url))
                    projects.Add(new(fullName, url, (string?)job.Element("displayName")));
            }
        }
        return projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    public async Task<ProjectSnapshot> RefreshAsync(ProjectSettings project, CancellationToken cancellationToken)
    {
        var xml = await ReadXmlAsync(project.Url, "name,color,buildable,inQueue,lastBuild[number,url],lastCompletedBuild[number,url]", cancellationToken);
        if (xml.Element("color") is null && xml.Element("buildable") is null && xml.Element("lastBuild") is null)
            throw new InvalidDataException("Jenkins returned invalid project data.");
        var color = (string?)xml.Element("color") ?? "";
        var building = color.EndsWith("_anime", StringComparison.Ordinal);
        var latest = await BuildAsync(xml.Element("lastBuild"), cancellationToken);
        var completedElement = xml.Element("lastCompletedBuild");
        var completed = completedElement is not null && (string?)completedElement.Element("url") == latest?.Url
            ? latest : await BuildAsync(completedElement, cancellationToken);
        // lastBuild may be running; retain the known last completed result alongside activity.
        var disabled = color.StartsWith("disabled", StringComparison.Ordinal) || (string?)xml.Element("buildable") == "false";
        var result = disabled ? BuildResult.Disabled : completed?.Result ?? BuildResult.Unknown;
        return new(server.Id, project, result, building || latest?.Building == true, (string?)xml.Element("inQueue") == "true", latest, completed, DateTimeOffset.UtcNow);
    }

    private async Task<BuildInfo?> BuildAsync(XElement? element, CancellationToken cancellationToken)
    {
        if (element is null) return null;
        var url = Required(element, "url");
        var xml = await ReadXmlAsync(url, "number,url,fullDisplayName,timestamp,duration,result,building", cancellationToken);
        if (!long.TryParse((string?)xml.Element("number"), out var number)) throw new InvalidDataException("Build number is missing or invalid.");
        var result = ((string?)xml.Element("result")) switch
        {
            "SUCCESS" => BuildResult.Success, "UNSTABLE" => BuildResult.Unstable,
            "FAILURE" => BuildResult.Failure, "ABORTED" => BuildResult.Aborted, _ => BuildResult.Unknown
        };
        return new(number, (string?)xml.Element("url") ?? url, (string?)xml.Element("fullDisplayName"),
            long.TryParse((string?)xml.Element("timestamp"), out var time) ? DateTimeOffset.FromUnixTimeMilliseconds(time) : null,
            long.TryParse((string?)xml.Element("duration"), out var duration) ? TimeSpan.FromMilliseconds(duration) : null, result,
            (string?)xml.Element("building") == "true");
    }

    private static string Required(XElement xml, string name) => (string?)xml.Element(name) is { Length: > 0 } value
        ? value : throw new InvalidDataException("Jenkins XML is missing " + name + ".");
    public void Dispose() => http.Dispose();
}
