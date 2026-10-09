using System.Net;
using System.Text;
using JenkinsTray.Core;
using Xunit;

namespace JenkinsTray.Core.Tests;

public sealed class JenkinsClientTests
{
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    internal static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml) };
    private static ServerSettings Server => new() { Id = "server", Url = "https://ci.test/", Username = "user" };
    [Fact] public async Task RecursiveDiscoveryDeduplicatesAndRetainsServerPaths()
    {
        var paths = new List<string>();
        using var client = new JenkinsClient(Server, "secret", new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("user:secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            return Task.FromResult(Xml(Fixture(request.RequestUri.AbsolutePath switch
            { "/api/xml" => "root.xml", "/job/folder/api/xml" => "folder.xml", _ => "branches.xml" })));
        }));
        var projects = await client.DiscoverAsync(default);
        Assert.Equal(new[] { "folder/pipeline/main", "plain" }, projects.Select(p => p.Name));
        Assert.Equal(3, paths.Count);
        Assert.Equal("https://ci.test/job/folder/job/pipeline/job/main/", projects[0].Url);
    }
    [Fact] public async Task ActiveBuildRetainsLastCompletedFailureAndDetails()
    {
        using var client = new JenkinsClient(Server, null, new Handler((request, _) => Task.FromResult(Xml(Fixture(request.RequestUri!.AbsolutePath switch
        { "/job/plain/api/xml" => "active.xml", "/job/plain/12/api/xml" => "build-running.xml", _ => "build-failed.xml" })))));
        var snapshot = await client.RefreshAsync(new("plain", "https://ci.test/job/plain/"), default);
        Assert.Equal(BuildResult.Failure, snapshot.Result); Assert.True(snapshot.Building); Assert.True(snapshot.Queued);
        Assert.Equal(12, snapshot.LatestBuild!.Number); Assert.Equal(11, snapshot.CompletedBuild!.Number);
        Assert.Equal("https://ci.test/job/plain/12/console", snapshot.ConsoleUrl);
        Assert.Equal(15, snapshot.CompletedBuild.Duration!.Value.TotalSeconds);
    }
    [Theory] [InlineData("<job><color>notbuilt</color></job>", BuildResult.Unknown)]
    [InlineData("<job><color>disabled</color><buildable>false</buildable></job>", BuildResult.Disabled)]
    public async Task MissingBuildsRemainUnknownOrDisabled(string xml, BuildResult result)
    {
        using var client = new JenkinsClient(Server, null, new Handler((_, _) => Task.FromResult(Xml(xml))));
        var snapshot = await client.RefreshAsync(new("plain", "https://ci.test/job/plain/"), default);
        Assert.Equal(result, snapshot.Result); Assert.Null(snapshot.ConsoleUrl);
    }
    [Theory] [InlineData("<invalid/>")] [InlineData("bad xml")]
    public async Task MalformedResponsesFail(string xml)
    {
        using var client = new JenkinsClient(Server, null, new Handler((_, _) => Task.FromResult(Xml(xml))));
        await Assert.ThrowsAnyAsync<Exception>(() => client.RefreshAsync(new("plain", "https://ci.test/job/plain/"), default));
    }
    [Theory] [InlineData(HttpStatusCode.Unauthorized)] [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticationErrorsAreExplicit(HttpStatusCode status)
    {
        using var client = new JenkinsClient(Server, null, new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.DiscoverAsync(default));
        Assert.Contains("authentication", exception.Message);
    }
    [Fact] public void CertificateOverrideIsPerServer()
    {
        using var normal = JenkinsClient.CreateHandler(Server); using var insecure = JenkinsClient.CreateHandler(Server with { IgnoreUntrustedCertificate = true });
        Assert.Null(normal.ServerCertificateCustomValidationCallback); Assert.NotNull(insecure.ServerCertificateCustomValidationCallback);
        Assert.False(normal.AllowAutoRedirect);
    }
    [Fact] public async Task RequestsHaveTimeoutAndCallerCancellation()
    {
        using var client = new JenkinsClient(Server, null, new Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Xml(""); }), TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DiscoverAsync(default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DiscoverAsync(cancellation.Token));
    }
    [Fact] public async Task CrossOriginUrlsCannotReceiveCredentials()
    {
        var requests = 0;
        using var client = new JenkinsClient(Server, "secret", new Handler((_, _) => { requests++; return Task.FromResult(Xml("")); }));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.RefreshAsync(new("other", "https://other.test/job/"), default));
        Assert.Equal(0, requests);
    }
}
