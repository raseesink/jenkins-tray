using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using JenkinsTray.Core;

namespace JenkinsTray.Desktop;

public sealed class SettingsWindow : Window
{
    private readonly SettingsService settings;
    private readonly IJenkinsClientFactory clients;
    private readonly IDesktopNotifications notifications;
    private readonly CancellationTokenSource closed = new();
    private readonly ComboBox servers = new() { Name = "Servers", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox name = new() { Name = "ServerName" };
    private readonly TextBox url = new() { Name = "ServerUrl", PlaceholderText = "https://jenkins.example.com/" };
    private readonly TextBox username = new() { Name = "Username" };
    private readonly TextBox password = new() { Name = "Secret", PasswordChar = '●', PlaceholderText = "Leave blank to retain saved credential" };
    private readonly CheckBox removeCredential = new() { Content = "Remove saved credential" };
    private readonly CheckBox certificate = new() { Content = "Allow untrusted certificates for this server" };
    private readonly TextBox interval = new() { Name = "PollInterval" };
    private readonly CheckBox enabled = new() { Content = "Desktop notifications" };
    private readonly TextBlock availability = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock feedback = new() { Name = "Feedback", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TreeView tree = new();
    private readonly Dictionary<string, (ProjectSettings Project, CheckBox Selected)> projectChoices = new();
    private ServerSettings? editing;
    private ServerSettings[] listedServers = [];
    private CancellationTokenSource? discovery;

    public SettingsWindow(SettingsService settings, IJenkinsClientFactory clients, IDesktopNotifications notifications)
    {
        this.settings = settings;
        this.clients = clients;
        this.notifications = notifications;
        Title = "Jenkins Tray — Settings"; Width = 720; Height = 850;
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "Monitoring", FontSize = 22 });
        AddField(content, "Polling interval (seconds)", interval);
        content.Children.Add(enabled);
        content.Children.Add(availability);
        content.Children.Add(App.Button("Check notification permission", () => _ = RunAsync(CheckPermissionAsync)));
        content.Children.Add(App.Button("Save preferences", () => _ = RunAsync(async () =>
        {
            if (!int.TryParse(interval.Text, out var seconds) || seconds <= 0) throw new InvalidDataException("Polling interval must be positive.");
            await settings.SavePreferencesAsync(seconds, enabled.IsChecked == true, closed.Token);
            if (enabled.IsChecked == true) await CheckPermissionAsync();
        })));
        content.Children.Add(new Separator());
        content.Children.Add(new TextBlock { Text = "Servers and projects", FontSize = 22 });
        content.Children.Add(servers);
        servers.SelectionChanged += (_, _) =>
        {
            if (servers.SelectedIndex >= 0 && servers.SelectedIndex < listedServers.Length) Edit(listedServers[servers.SelectedIndex]);
        };
        content.Children.Add(App.Button("Add server", () => Edit(null)));
        AddField(content, "Display name", name); AddField(content, "Server URL", url);
        AddField(content, "Username", username); AddField(content, "Password or API token", password);
        content.Children.Add(removeCredential); content.Children.Add(certificate);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(App.Button("Save server", () => _ = RunAsync(SaveServerAsync)));
        actions.Children.Add(App.Button("Remove server", () => _ = RunAsync(RemoveServerAsync)));
        actions.Children.Add(App.Button("Discover projects", () => _ = RunAsync(DiscoverAsync)));
        content.Children.Add(actions);
        content.Children.Add(new TextBlock { Text = "Select jobs below, then Save server. Folders can be expanded." });
        content.Children.Add(tree);
        content.Children.Add(new Separator());
        content.Children.Add(App.Button("Import legacy JSON…", () => _ = RunAsync(ChooseImportAsync)));
        if (OperatingSystem.IsWindows() && !settings.HasFile)
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jenkins Tray", "jenkins.configuration");
            if (File.Exists(legacy))
                content.Children.Add(App.Button("Import existing Windows configuration", () => _ = RunAsync(() => ImportAsync(legacy))));
        }
        if (settings.NeedsRecovery)
        {
            content.Children.Add(new TextBlock { Text = "Stored settings are damaged and have been preserved. Recovery keeps a backup before creating empty settings." });
            content.Children.Add(App.Button("Recover with empty settings…", () => _ = RunAsync(async () =>
            {
                if (!await ConfirmAsync("Keep a backup and replace damaged settings with empty settings?")) return;
                await settings.RecoverAsync(closed.Token); Reload();
            })));
        }
        content.Children.Add(feedback);
        Content = new ScrollViewer { Content = content };
        Closed += (_, _) => { closed.Cancel(); discovery?.Cancel(); };
        Reload();
        _ = RunAsync(async () => SetNotificationAvailability(await notifications.GetAvailabilityAsync(false, closed.Token)));
    }
    public void SetNotificationAvailability(NotificationAvailability state) => availability.Text = state switch
    {
        NotificationAvailability.Available => "Desktop notifications are permitted.",
        NotificationAvailability.Denied => "Notifications are denied. Enable Jenkins Tray in system notification settings.",
        NotificationAvailability.Unknown => "Notification permission has not been requested.",
        _ => "Desktop notifications are unavailable; monitoring continues."
    };
    private async Task CheckPermissionAsync() => SetNotificationAvailability(await notifications.GetAvailabilityAsync(true, closed.Token));
    private static void AddField(StackPanel content, string label, Control control)
    {
        content.Children.Add(new TextBlock { Text = label }); content.Children.Add(control);
    }
    private void Reload(string? selectedId = null)
    {
        interval.Text = settings.Current.PollIntervalSeconds.ToString(); enabled.IsChecked = settings.Current.NotificationsEnabled;
        listedServers = settings.Current.Servers.ToArray();
        servers.ItemsSource = listedServers.Select(s => s.Label).ToArray();
        servers.SelectedIndex = selectedId is null ? -1 : Array.FindIndex(listedServers, s => s.Id == selectedId);
        if (servers.SelectedIndex < 0) Edit(null);
    }
    private void Edit(ServerSettings? server)
    {
        discovery?.Cancel(); editing = server;
        name.Text = server?.DisplayName ?? ""; url.Text = server?.Url ?? ""; username.Text = server?.Username ?? "";
        password.Text = ""; removeCredential.IsChecked = false; certificate.IsChecked = server?.IgnoreUntrustedCertificate ?? false;
        SetProjects(server?.Projects ?? []);
    }
    private void SetProjects(IEnumerable<ProjectSettings> projects, HashSet<string>? selectedUrls = null)
    {
        projectChoices.Clear();
        var roots = new List<TreeViewItem>();
        var folders = new Dictionary<string, TreeViewItem>();
        var selected = selectedUrls ?? editing?.Projects.Select(p => p.Url).ToHashSet() ?? [];
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var segments = project.Name.Split('/');
            List<TreeViewItem> parentItems = roots;
            TreeViewItem? parent = null;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var path = string.Join('/', segments.Take(i + 1));
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new TreeViewItem { Header = segments[i], IsExpanded = true, ItemsSource = new List<TreeViewItem>() };
                    folders[path] = folder;
                    if (parent is null) roots.Add(folder); else ((List<TreeViewItem>)parent.ItemsSource!).Add(folder);
                }
                parent = folder;
            }
            var check = new CheckBox { Content = segments[^1], IsChecked = selected.Contains(project.Url) };
            projectChoices[project.Url] = (project, check);
            var leaf = new TreeViewItem { Header = check };
            if (parent is null) parentItems.Add(leaf); else ((List<TreeViewItem>)parent.ItemsSource!).Add(leaf);
        }
        tree.ItemsSource = roots;
    }
    private async Task SaveServerAsync()
    {
        var server = editing ?? new ServerSettings();
        var serverUrl = SettingsStore.ValidateUrl(url.Text ?? "").AbsoluteUri.TrimEnd('/') + "/";
        var projects = projectChoices.Values.Where(p => p.Selected.IsChecked == true).Select(p => p.Project).ToImmutableArray();
        if (editing is not null && editing.Url.TrimEnd('/') != serverUrl.TrimEnd('/'))
        {
            var oldBase = new Uri(editing.Url.TrimEnd('/') + "/");
            projects = projects.Select(p => p with { Url = new Uri(new Uri(serverUrl), oldBase.MakeRelativeUri(new Uri(p.Url))).AbsoluteUri }).ToImmutableArray();
        }
        server = server with
        {
            Url = serverUrl, DisplayName = name.Text ?? "", Username = string.IsNullOrWhiteSpace(username.Text) ? null : username.Text,
            IgnoreUntrustedCertificate = certificate.IsChecked == true, Projects = projects
        };
        await settings.SaveServerAsync(server, string.IsNullOrEmpty(password.Text) ? null : password.Text,
            removeCredential.IsChecked == true || server.Username is null, closed.Token);
        password.Text = ""; Reload(server.Id);
    }
    private async Task RemoveServerAsync()
    {
        if (editing is null) return;
        if (!await ConfirmAsync("Remove this server and its selected projects?")) return;
        await settings.RemoveServerAsync(editing.Id, closed.Token); Reload();
    }
    private async Task DiscoverAsync()
    {
        if (editing is null) throw new InvalidDataException("Save the server before discovering projects.");
        var server = editing;
        var selected = projectChoices.Where(p => p.Value.Selected.IsChecked == true).Select(p => p.Key).ToHashSet();
        discovery?.Cancel(); discovery?.Dispose();
        discovery = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
        var token = discovery.Token;
        feedback.Text = "Discovering projects…";
        using var client = await clients.CreateAsync(server, token);
        var found = await Task.Run(() => client.DiscoverAsync(token), token);
        if (editing?.Id != server.Id || token.IsCancellationRequested) return;
        // Keep previously selected jobs visible when Jenkins no longer returns them.
        SetProjects(found.Concat(server.Projects).DistinctBy(p => p.Url), selected);
    }
    private async Task ChooseImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select legacy jenkins.configuration JSON", AllowMultiple = false });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath() ?? throw new InvalidDataException("Choose a local legacy file.");
        await ImportAsync(path);
    }
    private async Task ImportAsync(string path)
    {
        if (!await ConfirmAsync("Import this legacy JSON file and replace current monitoring settings? The source file will stay unchanged.")) return;
        await settings.ImportAsync(path, closed.Token); Reload();
    }
    private async Task<bool> ConfirmAsync(string message)
    {
        var prompt = new Window { Title = "Jenkins Tray", Width = 440, Height = 180, CanResize = false };
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(App.Button("Continue", () => prompt.Close(true)));
        panel.Children.Add(App.Button("Cancel", () => prompt.Close(false)));
        prompt.Content = panel;
        return await prompt.ShowDialog<bool>(this);
    }
    private async Task RunAsync(Func<Task> operation)
    {
        try { await operation(); if (!closed.IsCancellationRequested) feedback.Text = "Done."; }
        catch (OperationCanceledException) { }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        { feedback.Text = "Jenkins authentication was rejected for this server. Check its username and password or API token."; }
        catch (InvalidDataException exception) { feedback.Text = exception.Message; }
        catch (InvalidOperationException exception) { feedback.Text = exception.Message; }
        catch { feedback.Text = "The operation failed. Check the file, server credentials, or protected secret storage. Active settings are retained if saving did not complete."; }
    }
}
