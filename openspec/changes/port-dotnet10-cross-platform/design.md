## Context

See `proposal.md` for motivation and scope. The current application targets .NET Framework 4.8, uses DevExpress 10.1 WinForms controls, and wires services through Spring.NET XML configuration. `JenkinsService` synchronously consumes the XML API and recursively discovers folder/multibranch jobs. `ProjectsUpdateService` uses SmartThreadPool and starts the next cycle after a delay. Aggregate status and transition detection live in `TrayNotifier`; domain types and `ClaimService` also reference WinForms. Sound/window helpers invoke `winmm.dll` and `user32.dll`. Tests cover string utilities only.

## Goals / Non-Goals

**Goals:** Make monitoring behavior independently testable, share application logic across both operating systems, and contain native dependencies at the desktop boundary. Preserve the original configuration for rollback.

**Non-Goals:** Reuse WinForms designer files, reproduce the exact DevExpress appearance, implement deferred actions, support mobile/Linux, or retain the legacy installer as the new application's packaging path.

## Decisions

### Shared core and Avalonia interface

Use SDK-style projects, package references, and .NET 10. Place models, XML parsing, polling, transition policy, and persistence in a portable core. Place Avalonia views and view models in a shared desktop layer. Use narrow interfaces for desktop notifications, secret storage, and URL launching; platform hosts supply implementations. Use standard .NET dependency injection/logging and collections instead of retaining Spring.NET/Common.Logging in the new runtime.

WinForms modernization alone would retain Windows-only UI. MAUI supports both targets but adds a Catalyst UI path; Avalonia directly supports the tray/menu bar model central to this application. Two unrelated native interfaces would duplicate settings and project views.

```text
Windows host ----+
                +--> Avalonia views/view models --> portable core
macOS host -----+             |
                             +--> platform notification/secret adapters
```

Keep Avalonia code portable (`net10.0`); platform hosts/adapters can target `net10.0-windows` and `net10.0-macos` where native APIs require them. The macOS native adapter build requires the .NET macOS workload. Pin compatible stable dependency versions during implementation, rather than copying the old bundled binaries.

### Asynchronous monitoring with explicit state

Keep the XML API and existing tree filters initially; changing to JSON would expand the migration without changing the user outcome. Replace thread-static `WebClient` and the global certificate callback with per-server `HttpClient` handlers, timeouts, and cancellation. Preserve an explicit per-server untrusted-certificate override, defaulting off. Bound concurrency, isolate server/project failures, and publish immutable snapshots for UI updates.

Use one polling coordinator: immediate initial refresh, a default 15-second delay after completion, and coalesced manual refresh requests. No overlapping cycles. Stop cancels work. Failed retrieval marks data unavailable/stale rather than presenting cached success as current health. Extract status severity and transition rules from tray code; raw project state remains separate from the aggregate policy to enable acknowledgement later.

### Tray lifecycle and native notifications

Use Avalonia tray icons and native menus, with Show, Refresh, Settings, and Quit commands. Closing the window hides it; Quit terminates the application. Represent the worst selected project status and building activity without requiring the old animated ICO implementation. Retain project/console navigation.

Notification adapters use Windows App SDK app notifications and macOS UserNotifications. Register the Windows notification integration (supported for packaged and unpackaged apps), establish the macOS bundle identity, and request permission where required. Denial is visible in settings and does not stop monitoring. Emit regression and recovery notifications once per observed transition; initial snapshots and unchanged polls do not notify. Custom sounds remain deferred. Validate real distribution bundles, because development execution does not prove notification registration/identity works.

### Versioned settings and legacy import

Store versioned JSON in an OS-appropriate per-user application directory. Keep usernames and secret references in settings; use Windows Credential Manager and macOS Keychain for passwords/API tokens. A failed secret-store write does not fall back to Base64/plaintext. Prefer atomic settings replacement and explicit validation errors over overwriting damaged files.

On Windows, when no new configuration exists, offer import of `%APPDATA%/Jenkins Tray/jenkins.configuration`; on either OS allow the user to select a copied legacy JSON file. Decode the existing credential array format, preserve server/project associations and refresh/notification preferences, and copy deferred-feature values into an inactive compatibility section. Imported secrets enter the native secret store. Retain the source file untouched. Legacy `.properties` import is outside this release; existing JSON import is the supported migration path.

### Distribution and verification

Start with self-contained `win-x64`, `osx-arm64`, and `osx-x64` artifacts; Windows ARM64/32-bit support is outside the initial matrix. Supply a Windows distribution with notification identity and a macOS `.app` bundle. Document the supported OS versions as the intersection of the chosen .NET 10/Avalonia releases. Use Windows and macOS build jobs and focused core tests with Jenkins XML fixtures and a controlled HTTP server. Verify tray lifecycle, permissions, links, dark/light icon visibility, settings, and notifications manually on packaged targets. Public signing/notarization uses release credentials supplied separately; local unsigned artifacts remain reviewable.

## Risks / Trade-offs

- UI and domain logic are coupled -> Extract pure status/transition logic and test realistic snapshots before reconnecting the UI.
- Platform notification APIs depend on identity and permissions -> Validate platform adapters and bundles early, before rebuilding all screens.
- Large or unavailable servers could stall updates -> Bound requests, apply timeouts, and isolate failures with cancellation.
- Legacy secrets and unrecognized settings could be lost -> Use a dedicated importer, preserve source files and deferred values, and test failed imports.
- First-release feature reduction could surprise existing users -> Document the omissions and link the dependent follow-up change.
- Signed public macOS/Windows distribution needs external credentials -> Keep unsigned local build validation separate from release signing.

## Migration Plan

1. Prove .NET 10 Avalonia tray lifecycle, native notification delivery, and secret storage on both platforms.
2. Extract/port the core and add fixture-based monitoring and state tests.
3. Build settings/project views and the legacy importer; keep the old configuration read-only.
4. Produce and smoke-test each distribution, then switch the documented build/release entry points to the new application.
5. Keep old releases and source configuration available for rollback. New settings use a separate location and are not written back to the legacy format.
6. Verify and archive this change before implementing `restore-advanced-desktop-features`.

## References

- [Avalonia tray icons and platform interaction](https://docs.avaloniaui.net/controls/navigation/trayicon)
- [Avalonia macOS target framework and native API access](https://docs.avaloniaui.net/docs/platform-specific-guides/macos)
- [Windows .NET app notification registration](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)
- [Apple notification permission requests](https://developer.apple.com/documentation/usernotifications/asking-permission-to-use-notifications)
