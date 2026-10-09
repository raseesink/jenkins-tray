using Foundation;
using JenkinsTray.Core;
using UserNotifications;

namespace JenkinsTray.MacOS;

public sealed class MacNotifications : IDesktopNotifications
{
    private readonly ForegroundDelegate foreground = new();
    private UNUserNotificationCenter? center;
    private UNUserNotificationCenter Center
    {
        get
        {
            if (center is null)
            {
                center = UNUserNotificationCenter.Current;
                center.Delegate = foreground;
            }
            return center;
        }
    }

    public async Task<NotificationAvailability> GetAvailabilityAsync(bool requestPermission, CancellationToken cancellationToken = default)
    {
        try
        {
            using var settings = await Center.GetNotificationSettingsAsync().WaitAsync(cancellationToken);
            if (settings.AuthorizationStatus == UNAuthorizationStatus.NotDetermined && requestPermission)
            {
                var response = await Center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert).WaitAsync(cancellationToken);
                return response.Item1 ? NotificationAvailability.Available : NotificationAvailability.Denied;
            }
            return settings.AuthorizationStatus switch
            {
                UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional => NotificationAvailability.Available,
                UNAuthorizationStatus.Denied => NotificationAvailability.Denied, _ => NotificationAvailability.Unknown
            };
        }
        catch (OperationCanceledException) { throw; }
        catch { return NotificationAvailability.Unavailable; }
    }

    public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (await GetAvailabilityAsync(false, cancellationToken) != NotificationAvailability.Available) return;
        using var content = new UNMutableNotificationContent { Title = title, Body = message };
        using var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString("N"), content, null);
        await Center.AddNotificationRequestAsync(request).WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (center is not null) center.Delegate = null;
        foreground.Dispose();
    }
    private sealed class ForegroundDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter userNotificationCenter, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);
    }
}
