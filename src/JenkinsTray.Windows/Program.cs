using JenkinsTray.Desktop;

namespace JenkinsTray.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) =>
        DesktopRuntime.Run<WindowsSecretStore, WindowsNotifications>(args);
}
