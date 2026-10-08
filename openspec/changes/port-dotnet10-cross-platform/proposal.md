## Why

Jenkins Tray targets .NET Framework 4.8 and its WinForms/DevExpress interface only runs on Windows. A .NET 10 desktop port will make the essential monitoring experience available on Windows and macOS while keeping the first release small enough to validate both platforms.

## What Changes

- Replace the legacy desktop application with a .NET 10 application using Avalonia for the project list, settings, and tray/menu bar interface.
- Preserve multi-server configuration, authenticated project discovery, folder/multibranch project selection, automatic polling, manual refresh, build status, and links to Jenkins pages and console output.
- Provide aggregate tray status, an indication of builds in progress, and native desktop notifications for build regressions and recovery.
- Persist settings on both platforms and import the existing Windows JSON configuration without overwriting the original.
- Produce self-contained Windows and macOS distributions with platform smoke checks.
- **BREAKING**: The first cross-platform release omits build actions, Claim plugin integration, custom sound playback, and application update checks/install flows. These return in `restore-advanced-desktop-features` after this change.

## Capabilities

### New Capabilities

- `desktop-runtime`: Windows/macOS execution, application lifecycle, and distributable application bundles.
- `monitoring-configuration`: Server credentials, project selection, persistence, and legacy configuration import.
- `jenkins-monitoring`: Project discovery, status polling, error isolation, and Jenkins navigation.
- `tray-status-notifications`: Aggregate status, running-build indication, tray/menu bar commands, and transition notifications.

### Modified Capabilities

None. The repository has no durable OpenSpec specifications yet.

## Impact

The application project, domain models, Jenkins networking, polling services, UI, tests, and release scripts need migration. WinForms, DevExpress, Spring.NET collections/container, SmartThreadPool, Windows sound/window APIs, and the old packaging/update dependencies leave the new runtime path. Existing Jenkins servers remain external services with no server-side changes required. No changes to application code are part of this planning capture.
