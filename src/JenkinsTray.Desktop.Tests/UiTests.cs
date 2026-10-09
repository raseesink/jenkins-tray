using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using JenkinsTray.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(JenkinsTray.Desktop.Tests.TestApplication))]
namespace JenkinsTray.Desktop.Tests;

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class UiTests
{
    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = new();
        public Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult(values.GetValueOrDefault(reference));
        public Task WriteAsync(string reference, string secret, CancellationToken cancellationToken = default) { values[reference] = secret; return Task.CompletedTask; }
        public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) { values.Remove(reference); return Task.CompletedTask; }
    }
    private sealed class Notifications : IDesktopNotifications
    {
        public Task<NotificationAvailability> GetAvailabilityAsync(bool requestPermission, CancellationToken cancellationToken = default) => Task.FromResult(NotificationAvailability.Denied);
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
    private sealed class Clients : IJenkinsClientFactory
    {
        public Task<IJenkinsClient> CreateAsync(ServerSettings server, CancellationToken cancellationToken) => Task.FromResult<IJenkinsClient>(new Client(server));
        private sealed class Client(ServerSettings server) : IJenkinsClient
        {
            public Task<ImmutableArray<ProjectSettings>> DiscoverAsync(CancellationToken cancellationToken) => server.DisplayName == "Bad"
                ? throw new HttpRequestException("Jenkins authentication was rejected.", null, System.Net.HttpStatusCode.Unauthorized)
                : Task.FromResult<ImmutableArray<ProjectSettings>>([new("folder/pipeline/main", "https://ci.test/job/folder/job/pipeline/job/main/")]);
            public Task<ProjectSnapshot> RefreshAsync(ProjectSettings project, CancellationToken cancellationToken) => throw new NotSupportedException();
            public void Dispose() { }
        }
    }
    private static IEnumerable<T> Descendants<T>(Control control) where T : Control => control.GetLogicalDescendants().OfType<T>();
    private static T Named<T>(Control control, string name) where T : Control => Descendants<T>(control).Single(c => c.Name == name);
    private static void Click(Control control, string text) => Descendants<Button>(control).Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    [AvaloniaFact] public async Task SettingsControlsPersistNestedSelectionsAndRejectInvalidInterval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "JenkinsTray-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new SettingsStore(Path.Combine(directory, "settings.json")); var service = new SettingsService(store, new Secrets());
        var window = new SettingsWindow(service, new Clients(), new Notifications()); window.Show();
        try
        {
            Named<TextBox>(window, "ServerUrl").Text = "https://ci.test/"; Named<TextBox>(window, "ServerName").Text = "Good";
            Click(window, "Save server"); await Until(() => service.Current.Servers.Length == 1);
            await Until(() => Named<ComboBox>(window, "Servers").SelectedIndex == 0);
            Click(window, "Discover projects"); await Until(() => Descendants<CheckBox>(window).Any(c => Equals(c.Content, "main")));
            Descendants<CheckBox>(window).Single(c => Equals(c.Content, "main")).IsChecked = true;
            Click(window, "Save server"); await Until(() => service.Current.Servers[0].Projects.Length == 1);
            var persisted = await store.LoadAsync(); Assert.Equal("folder/pipeline/main", persisted.Servers[0].Projects[0].Name);
            Named<TextBox>(window, "PollInterval").Text = "0"; Click(window, "Save preferences");
            await Until(() => Named<TextBlock>(window, "Feedback").Text!.Contains("positive")); Assert.Equal(15, service.Current.PollIntervalSeconds);
            Assert.Contains("denied", Descendants<TextBlock>(window).Single(t => t.Text?.Contains("Notifications are denied") == true).Text);
            Named<TextBox>(window, "ServerName").Text = "Renamed"; Click(window, "Save server");
            await Until(() => service.Current.Servers[0].DisplayName == "Renamed");
            Click(window, "Remove server"); await Until(() => window.OwnedWindows.Count == 1);
            Click(window.OwnedWindows.Single(), "Continue"); await Until(() => service.Current.Servers.IsEmpty);
            Assert.Empty((await store.LoadAsync()).Servers);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }
    [AvaloniaFact] public async Task FailedServerDiscoveryDoesNotBlockAnotherServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "JenkinsTray-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var service = new SettingsService(new(Path.Combine(directory, "settings.json")), new Secrets());
        await service.SaveAsync(new() { Servers = [new() { Id = "bad", Url = "https://ci.test/", DisplayName = "Bad" }, new() { Id = "good", Url = "https://ci.test/", DisplayName = "Good" }] });
        var window = new SettingsWindow(service, new Clients(), new Notifications()); window.Show();
        try
        {
            Named<ComboBox>(window, "Servers").SelectedIndex = 0; Click(window, "Discover projects");
            await Until(() => Named<TextBlock>(window, "Feedback").Text!.Contains("authentication"));
            Named<ComboBox>(window, "Servers").SelectedIndex = 1; Click(window, "Discover projects");
            await Until(() => Descendants<CheckBox>(window).Any(c => Equals(c.Content, "main")));
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }
    [AvaloniaFact] public void MonitoringViewShowsRawResultsStalenessAndExactNavigation()
    {
        var opened = new List<string>(); var view = new MonitoringView(opened.Add);
        var project = new ProjectSettings("folder/job", "https://ci.test/job/folder/job/job/");
        var settings = new Settings { Servers = [new() { Id = "server", Url = "https://ci.test/", DisplayName = "CI", Projects = [project] }] };
        var build = new BuildInfo(2, project.Url + "2/", "Build #2", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10), BuildResult.Failure);
        var snapshot = new ProjectSnapshot("server", project, BuildResult.Failure, true, true, build, build, DateTimeOffset.UtcNow);
        var window = new Window { Content = view }; window.Show();
        try
        {
            Assert.Equal(new(Health.Failure, true), view.Update(settings, [snapshot]));
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Failure · Building · Queued");
            Click(view, "folder/job"); Click(view, "Console"); Assert.Equal(new[] { project.Url, project.Url + "2/console" }, opened);
            Assert.Equal(Health.Incomplete, view.Update(settings, [snapshot with { Error = "Offline" }]).Health);
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text!.Contains("Unavailable / stale"));
            Assert.Equal(Health.Neutral, view.Update(new(), []).Health);
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text!.StartsWith("No projects selected"));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact] public void TrayIconsHaveDistinctStatusAndActivityRepresentations()
    {
        var images = new HashSet<string>();
        foreach (var health in Enum.GetValues<Health>()) foreach (var building in new[] { false, true })
        {
            using var stream = new MemoryStream(); using var bitmap = TrayVisual.Render(new(health, building)); bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Assert.True(images.Add(Convert.ToBase64String(stream.ToArray())));
        }
        Assert.Equal(10, images.Count);
    }
}
