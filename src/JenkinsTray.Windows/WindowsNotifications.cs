using Avalonia.Threading;
using JenkinsTray.Core;
using JenkinsTray.Desktop;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace JenkinsTray.Windows;

public sealed class WindowsNotifications : IDesktopNotifications
{
    private bool registered;
    private bool attempted;
    private AppNotificationManager? manager;
    private void Activated(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Dispatcher.UIThread.Post(() => (Avalonia.Application.Current as App)?.ShowWindow());

    public Task<NotificationAvailability> GetAvailabilityAsync(bool requestPermission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!attempted)
            {
                attempted = true;
                manager = AppNotificationManager.Default;
                manager.NotificationInvoked += Activated;
                manager.Register();
                registered = true;
            }
            return Task.FromResult(!registered ? NotificationAvailability.Unavailable
                : manager!.Setting == AppNotificationSetting.Enabled ? NotificationAvailability.Available : NotificationAvailability.Denied);
        }
        catch { return Task.FromResult(NotificationAvailability.Unavailable); }
    }

    public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (await GetAvailabilityAsync(false, cancellationToken) != NotificationAvailability.Available) return;
        var notification = new AppNotificationBuilder().AddText(title).AddText(message).BuildNotification();
        manager!.Show(notification);
        if (notification.Id == 0) throw new InvalidOperationException("Windows refused the notification.");
    }

    public void Dispose()
    {
        if (manager is not null) manager.NotificationInvoked -= Activated;
        if (registered) manager!.Unregister();
    }
}
