## Why

The initial .NET 10 port deliberately focuses on monitoring and notifications. Once that application works on Windows and macOS, the deferred interactive features can return without delaying the core migration or reintroducing Windows-only dependencies.

## What Changes

- Depend on implementation and verification of `port-dotnet10-cross-platform`; implement and archive this change afterward.
- Restore manual build triggering and stopping, including authenticated requests and actionable permission/CSRF errors.
- Restore optional Claim plugin integration and local acknowledgement of a failure's contribution to tray status.
- Restore configurable sound playback for failed, still-failing, successful, and recovered builds on both operating systems.
- Restore automatic update checks and an explicit user-approved route to the release matching the operating system and architecture.
- Preserve monitoring, tray lifecycle, and notification behavior from the first change.

## Capabilities

### New Capabilities

- `jenkins-build-actions`: Start and stop Jenkins builds with feedback and authentication handling.
- `build-acknowledgement-claims`: Local failure acknowledgement and optional Claim plugin actions.
- `custom-sound-notifications`: Configurable transition sounds with platform playback and error handling.
- `application-updates`: Background/manual update discovery and platform-specific release handoff.

### Modified Capabilities

None. These are additive capabilities; the initial port's contracts remain in force. Its core status contract will expose raw build status separately from aggregate status so acknowledgement can be added here without hiding project failures.

## Impact

Extends the new desktop UI, Jenkins client, configuration, aggregate-status policy, platform services, and release metadata. Reuses the first change's shared core and adapters. The old updater only checks a version file and opens an installer URL, so this scope restores that user-approved handoff rather than adding unattended binary replacement. No application implementation is included in this planning capture.
