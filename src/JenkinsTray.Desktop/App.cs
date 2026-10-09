using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using JenkinsTray.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JenkinsTray.Desktop;

public sealed class App : Application
{
    internal static IServiceProvider Services { get; set; } = null!;
    internal static string? SmokeReportPath { get; set; }
    private IClassicDesktopStyleApplicationLifetime desktop = null!;
    private SettingsService settings = null!;
    private PollingCoordinator polling = null!;
    private NotificationDispatcher notifications = null!;
    private IDesktopNotifications nativeNotifications = null!;
    private ILogger<App> logger = null!;
    private Window main = null!;
    private SettingsWindow? settingsWindow;
    private TrayIcon tray = null!;
    private readonly MonitoringView projects;
    public App() => projects = new MonitoringView(OpenUrl);
    private readonly TextBlock feedback = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private ImmutableArray<ProjectSnapshot> snapshots = [];
    private ImmutableArray<ProjectSnapshot> pendingSnapshots = [];
    private readonly object snapshotLock = new();
    private bool updateQueued;
    private AggregateStatus? shownStatus;
    private volatile bool quitting;
    private bool stopped;

    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            desktop = lifetime;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            settings = Services.GetRequiredService<SettingsService>();
            polling = Services.GetRequiredService<PollingCoordinator>();
            notifications = Services.GetRequiredService<NotificationDispatcher>();
            nativeNotifications = Services.GetRequiredService<IDesktopNotifications>();
            logger = Services.GetRequiredService<ILogger<App>>();
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16) };
            toolbar.Children.Add(Button("Refresh", polling.Refresh));
            toolbar.Children.Add(Button("Settings", OpenSettings));
            toolbar.Children.Add(feedback);
            var layout = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);
            layout.Children.Add(new ScrollViewer { Content = projects });
            main = new Window { Title = "Jenkins Tray", Width = 940, Height = 620, Content = layout };
            main.Closing += (_, e) => { if (!quitting) { e.Cancel = true; main.Hide(); } };
            desktop.MainWindow = main;
            var menu = new NativeMenu();
            AddMenu(menu, "Show", ShowWindow);
            AddMenu(menu, "Refresh", polling.Refresh);
            AddMenu(menu, "Settings", OpenSettings);
            menu.Items.Add(new NativeMenuItemSeparator());
            AddMenu(menu, "Quit", () => _ = QuitAsync());
            tray = new TrayIcon { Menu = menu, ToolTipText = "Jenkins Tray", Icon = TrayVisual.Create(new(Health.Neutral, false)), IsVisible = true };
            tray.Clicked += (_, _) => ShowWindow();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            desktop.ShutdownRequested += (_, e) => { if (!stopped) { e.Cancel = true; _ = QuitAsync(); } };
            polling.Updated += QueueSnapshots;
            notifications.AvailabilityChanged += availability => Dispatcher.UIThread.Post(() => settingsWindow?.SetNotificationAvailability(availability));
            settings.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                Render(); polling.Refresh();
                if (SmokeReportPath is null) _ = UpdatePermissionAsync();
            });
            _ = StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
    private async Task StartAsync()
    {
        try { await settings.LoadAsync(); }
        catch
        {
            feedback.Text = "Settings could not be loaded. The file is preserved. Open Settings to recover or import.";
            logger.LogWarning("Settings could not be loaded; explicit recovery is required.");
        }
        Render();
        notifications.Start();
        polling.Start();
        if (SmokeReportPath is not null) await SmokeAsync(SmokeReportPath);
        else
        {
            if (!settings.HasFile || settings.NeedsRecovery) OpenSettings();
            _ = UpdatePermissionAsync();
        }
    }
    private async Task UpdatePermissionAsync()
    {
        try
        {
            var availability = await nativeNotifications.GetAvailabilityAsync(settings.Current.NotificationsEnabled && !settings.NeedsRecovery);
            settingsWindow?.SetNotificationAvailability(availability);
        }
        catch { settingsWindow?.SetNotificationAvailability(NotificationAvailability.Unavailable); }
    }
    public void ShowWindow()
    {
        main.Show();
        main.WindowState = WindowState.Normal;
        main.Activate();
    }
    private async Task SmokeAsync(string reportPath)
    {
        var report = new Dictionary<string, object?> { ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() };
        var secrets = Services.GetRequiredService<ISecretStore>();
        var reference = "JenkinsTray/smoke/" + Guid.NewGuid().ToString("N");
        try
        {
            await Task.Delay(150); // Allow the normal desktop lifetime to show its main window.
            report["windowShown"] = main.IsVisible;
            main.Close(); report["closeHidesWindow"] = !main.IsVisible;
            ShowWindow(); report["showReopensWindow"] = main.IsVisible;
            OpenSettings(); report["settingsShown"] = settingsWindow?.IsVisible == true;
            settingsWindow?.Close();
            report["trayVisible"] = tray.IsVisible;
            foreach (var health in Enum.GetValues<Health>()) tray.Icon = TrayVisual.Create(new(health, true));
            report["allTrayIconsLoaded"] = true;
            await secrets.WriteAsync(reference, "temporary-smoke-value");
            report["secretRoundTrip"] = await secrets.ReadAsync(reference) == "temporary-smoke-value";
            await secrets.RemoveAsync(reference);
            report["secretRemoved"] = await secrets.ReadAsync(reference) is null;
            var available = await nativeNotifications.GetAvailabilityAsync(false);
            report["notificationAvailability"] = available.ToString();
            if (available == NotificationAvailability.Available)
            {
                await nativeNotifications.ShowAsync("Jenkins Tray validation", "Packaged application notification adapter smoke check.");
                report["notificationSubmitted"] = true;
            }
        }
        catch (Exception exception) { report["errorType"] = exception.GetType().Name; }
        finally
        {
            try { await secrets.RemoveAsync(reference); } catch { report["secretCleanupFailed"] = true; }
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var required = new[] { "windowShown", "closeHidesWindow", "showReopensWindow", "settingsShown", "trayVisible", "allTrayIconsLoaded", "secretRoundTrip", "secretRemoved" };
            var passed = !report.ContainsKey("errorType") && !report.ContainsKey("secretCleanupFailed") && required.All(key => report.GetValueOrDefault(key) is true);
            await QuitAsync(passed ? 0 : 1);
        }
    }
    private void OpenSettings()
    {
        ShowWindow();
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings, Services.GetRequiredService<IJenkinsClientFactory>(), nativeNotifications);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show(main);
        }
        else { settingsWindow.Show(); settingsWindow.Activate(); }
    }
    public async Task QuitAsync(int exitCode = 0)
    {
        if (quitting) return;
        quitting = true;
        try
        {
            await polling.DisposeAsync();
            await notifications.DisposeAsync();
        }
        catch { logger.LogWarning("A background service could not stop cleanly."); }
        finally
        {
            tray.IsVisible = false;
            tray.Dispose();
            stopped = true;
            desktop.Shutdown(exitCode);
        }
    }
    private void QueueSnapshots(ImmutableArray<ProjectSnapshot> updated)
    {
        if (quitting) return;
        notifications.Observe(updated);
        lock (snapshotLock)
        {
            pendingSnapshots = updated;
            if (updateQueued) return;
            updateQueued = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (snapshotLock) { snapshots = pendingSnapshots; updateQueued = false; }
            if (!quitting) Render();
        }, DispatcherPriority.Background);
    }
    private void Render()
    {
        var status = projects.Update(settings.Current, snapshots);
        if (status != shownStatus)
        {
            tray.Icon = TrayVisual.Create(status);
            tray.ToolTipText = "Jenkins Tray — " + status.Health + (status.Building ? " · Building" : "");
            shownStatus = status;
        }
    }
    private void OpenUrl(string url)
    {
        try { Services.GetRequiredService<IUrlLauncher>().Open(url); }
        catch { feedback.Text = "The browser could not open this Jenkins URL."; }
    }
    internal static Button Button(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }
    private static void AddMenu(NativeMenu menu, string label, Action action)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
