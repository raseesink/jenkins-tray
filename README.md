# Jenkins Tray

Jenkins Tray monitors selected Jenkins jobs from the Windows tray or macOS menu bar. The .NET 10 / Avalonia application supports multiple servers, authenticated recursive folder and multibranch discovery, background polling, manual refresh, build details, browser links, and native regression/recovery notifications.

The tray shows the worst selected result and a separate blue building marker. Green/check means success, yellow/exclamation means unstable, red/cross means failure, gray/exclamation means incomplete health, and blue/dash means no selected projects. Unavailable data cannot appear all-good. Closing the window hides it; use **Show**, **Refresh**, **Settings**, or **Quit** in the tray/menu bar. Quit cancels monitoring and exits.

Self-contained distributions target Windows x64 and macOS arm64/x64. See [RELEASE.md](RELEASE.md) for the supported OS matrix, build commands, unsigned launch instructions, and signing prerequisites. The legacy `JenkinsTray.sln` remains as historical source; the active solution is `JenkinsTray.Modern.slnx`.

In Settings, add a server, save its URL and optional username/password or API token, discover projects, select jobs in the folder tree, and save the server again. Untrusted certificates are allowed only with an explicit per-server override. Polling defaults to 15 seconds after each completed cycle. Secret values go to Windows Credential Manager or macOS Keychain, never ordinary settings. Leave the password field blank to retain a credential; use the remove checkbox to clear it.

Notification preferences are saved independently. Enabling notifications requests macOS authorization when needed; **Check notification permission** also checks or requests it explicitly. Denied or unavailable notifications leave monitoring operational. Initial snapshots, repeated results, and unavailable data are silent; known completed-build regressions and recoveries notify once.

Settings are stored separately from the legacy application:

- Windows: `%APPDATA%\JenkinsTray\settings.json`
- macOS: `~/Library/Application Support/JenkinsTray/settings.json`

Use **Import legacy JSON…** to select `jenkins.configuration` or a copied legacy file on either platform. On Windows, the existing `%APPDATA%\Jenkins Tray\jenkins.configuration` is offered when new settings do not exist. Import requires an explicit confirmation, preserves the original file, and moves credentials and deferred project tokens into protected storage. Unsupported legacy settings remain inactive in the compatibility section. Invalid files or refused secret writes do not replace active settings. A damaged settings file is preserved until explicit recovery/import; recovery keeps a backup.

Build actions, acknowledgement controls, Claim integration, custom sounds, and updater flows are deferred to `restore-advanced-desktop-features`. They have no controls in this release. This application only reads Jenkins data and opens browser links.

For core development:

```sh
dotnet restore src/JenkinsTray.Core.Tests/JenkinsTray.Core.Tests.csproj --locked-mode
dotnet test src/JenkinsTray.Core.Tests/JenkinsTray.Core.Tests.csproj --no-restore
```

The tests cover XML fixtures, status transitions, bounded polling and cancellation, settings validation/persistence, secret failures, and non-destructive legacy import. Packaged acceptance evidence is tracked in [the change validation artifact](openspec/changes/port-dotnet10-cross-platform/build-validation.md).

Jenkins Tray originated as a fork of [Hudson Tray Tracker](https://github.com/aseigneurin/hudson-tray-tracker). The retained legacy code needs its original Windows/DevExpress toolchain; those dependencies are absent from the modern runtime.
