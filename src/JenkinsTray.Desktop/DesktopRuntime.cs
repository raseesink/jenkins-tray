using System.Diagnostics;
using Avalonia;
using JenkinsTray.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JenkinsTray.Desktop;

public static class DesktopRuntime
{
    public static void Run<TSecrets, TNotifications>(string[] args)
        where TSecrets : class, ISecretStore where TNotifications : class, IDesktopNotifications
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ISecretStore, TSecrets>();
        builder.Services.AddSingleton<IDesktopNotifications, TNotifications>();
        builder.Services.AddSingleton<IUrlLauncher, BrowserLauncher>();
        var smokeIndex = Array.IndexOf(args, "--smoke-report");
        if (smokeIndex >= 0)
        {
            if (smokeIndex + 1 >= args.Length) throw new ArgumentException("--smoke-report needs an absolute output path.");
            App.SmokeReportPath = Path.GetFullPath(args[smokeIndex + 1]);
        }
        builder.Services.AddSingleton(new SettingsStore(App.SmokeReportPath is null ? SettingsStore.DefaultPath
            : Path.Combine(Path.GetTempPath(), "JenkinsTray-smoke-" + Guid.NewGuid().ToString("N"), "settings.json")));
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<IJenkinsClientFactory, JenkinsClientFactory>();
        builder.Services.AddSingleton(services => new PollingCoordinator(services.GetRequiredService<IJenkinsClientFactory>(),
            () => services.GetRequiredService<SettingsService>().Current));
        builder.Services.AddSingleton(services => new NotificationDispatcher(services.GetRequiredService<IDesktopNotifications>(),
            () => services.GetRequiredService<SettingsService>().Current));
        var host = builder.Build();
        App.Services = host.Services;
        try { AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        finally { ((IAsyncDisposable)host).DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
public sealed class BrowserLauncher : IUrlLauncher
{
    public void Open(string url)
    {
        var uri = SettingsStore.ValidateUrl(url);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
