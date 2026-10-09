# Build and release

The active distribution scripts publish .NET 10 self-contained Windows x64 and macOS Apple Silicon/Intel artifacts. The old WiX/Squirrel projects and binaries remain historical source for rollback; `Init.cmd`, `scripts/Releasify.cmd`, and `scripts/package-release.ps1` now use the modern Windows project.

## Supported systems

| Distribution | Supported desktop OS | Notes |
| --- | --- | --- |
| `JenkinsTray-win-x64.zip` | Windows 11 24H2 or later releases supported by .NET 10 | x64 only; notification registration needs a normal, non-elevated user session |
| `JenkinsTray-osx-arm64.zip` | macOS 15 and 26 | Apple Silicon; macOS 15 is Avalonia Tier 2 |
| `JenkinsTray-osx-x64.zip` | macOS 15 and 26 | Intel; Rosetta execution is supplementary validation |

This matrix is the intersection of the selected [.NET 10 supported OS list](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md), [Avalonia 12 platform support](https://docs.avaloniaui.net/docs/supported-platforms), and the application's architecture/platform minimums. Recheck this intersection before each public release. Windows ARM64/32-bit, Linux, and older OS versions are outside this release. Build success alone does not prove packaged acceptance on any listed OS.

## Toolchain

Install the SDK selected by `global.json` (10.0.401, latest patch permitted). Package versions are centralized in `src/Directory.Packages.props` and project dependency graphs are committed as `packages.lock.json`. Keep vulnerability auditing enabled. Normal CI restores use `--locked-mode`; regenerate locks deliberately after dependency changes, review them, and rerun locked restore.

macOS builds also need the .NET `macos` workload and its compatible Xcode installation. The native host uses .NET's Security and UserNotifications bindings, although shared Avalonia itself does not require that workload:

```sh
dotnet workload install macos
xcode-select -p
dotnet workload list
```

SDK 10.0.401 was validated with macOS workload 27.0.10722 and Xcode 27.0 locally. CI must provide a workload-compatible Xcode version; the macOS workflow uses a macOS 26 runner. Set `DEVELOPER_DIR` to the compatible Xcode installation if it is not selected by default. Public signing identities are not required to compile local bundles.

## Build the named artifacts

On Windows, with PowerShell 7:

```powershell
./scripts/package-release.ps1
```

This restores the Windows project in locked mode, publishes .NET and Windows App SDK runtime dependencies to `artifacts/win-x64/`, and creates `artifacts/JenkinsTray-win-x64.zip`. Keep every published file together; it is a directory distribution. Windows App SDK's [self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps) keeps its native runtime files beside the executable. [AppNotificationManager registration](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet) establishes notification identity for this unpackaged application at its installed executable location. Move the complete distribution to its final location before first launch. A separately installed .NET or Windows App SDK runtime is not part of this distribution's setup.

On macOS:

```sh
sh scripts/publish-macos.sh osx-arm64
sh scripts/publish-macos.sh osx-x64
```

Each script cleans generated native caches and checks that the executable and every bundled dylib support the requested architecture before archiving. These produce `artifacts/osx-arm64/JenkinsTray.app`, `artifacts/osx-x64/JenkinsTray.app`, and corresponding `artifacts/JenkinsTray-osx-arm64.zip` / `artifacts/JenkinsTray-osx-x64.zip`. The bundle identifier is `io.jenkinstray.desktop`. The macOS workload uses `LinkMode=None` so reflection-based desktop services retain their runtime dependencies; its required `PublishTrimmed=true` is left at the workload default.

`.github/workflows/desktop.yml` runs core tests and separate Windows/macOS build jobs and uploads the three artifacts. CI does not replace interactive tray, Keychain/Credential Manager, notification permission, or browser acceptance.

## Unsigned local launch

Extract the Windows ZIP into a stable directory and run `JenkinsTray.exe` as a normal user. Windows may show a publisher warning for unsigned artifacts. Use **More info → Run anyway** only for your locally built distribution if the system permits it.

On macOS, copy the `.app` to a stable location and open it. For your own unsigned downloaded build, use **System Settings → Privacy & Security → Open Anyway** if Gatekeeper blocks it. Do not disable Gatekeeper globally. Local bundles may use ad hoc signing. Native notifications must be checked from the actual bundle, and permission can be changed in system notification settings.

For an isolated diagnostic launch of a packaged application, the `--smoke-report <absolute-path>` flag writes a JSON report, checks window hide/reopen, tests a unique native secret reference, reads notification authorization without prompting, and exits. It uses a temporary settings path and removes the test secret. It does not establish permission-denial, notification-delivery, browser-launch, or full acceptance results.

## Public signing

Windows public releases need an Authenticode certificate, access to its private key, and a timestamp service. Sign the published executable and applicable native binaries before creating the ZIP. macOS public releases need an Apple Developer ID Application identity, a matching Team ID, hardened runtime entitlements appropriate for the .NET runtime, and notarization credentials. Sign nested binaries and the app, submit the final archive with `notarytool`, staple the ticket, then recreate the distribution ZIP. Preserve `io.jenkinstray.desktop` and use stable signed identity across upgrades so native secret/notification permissions remain consistent. Credentials are supplied separately; none are embedded in the repository or automated unsigned jobs.

## Acceptance and rollback

Check each target with no separately installed .NET runtime: add/edit/remove servers, persist nested job selections across restart, import a copied legacy JSON file with unchanged bytes, deny a native secret write without plaintext fallback, poll multiple servers through offline/recovery, open exact project/console browser URLs, hide/show the window while monitoring continues, inspect tray icons in light/dark appearance, disable/deny/permit notifications, observe exactly one regression/recovery notification, and Quit with an in-flight request. Record platform, architecture, bundle, results, and limitations in the change validation artifact. A Rosetta run is not an Intel-machine acceptance check.

Rollback uses a prior legacy Windows release and its original `%APPDATA%\Jenkins Tray\jenkins.configuration`, which the modern application never overwrites. Modern settings are separate; keep them and their protected secret references if you intend to return to this port. Replacing a ZIP/app does not convert modern settings back to the legacy format. Explicit recovery writes a uniquely named backup before resetting damaged settings.

Build actions, acknowledgement, Claim integration, custom sound playback, and application update checks/install are deferred. Their imported settings and protected project-token references remain inactive. Verify and archive `port-dotnet10-cross-platform` only after its required acceptance checks pass; leave `restore-advanced-desktop-features` unimplemented until then.
