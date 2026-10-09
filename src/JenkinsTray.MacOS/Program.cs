using JenkinsTray.Desktop;

namespace JenkinsTray.MacOS;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) =>
        DesktopRuntime.Run<MacSecretStore, MacNotifications>(args);
}
