## Context

See `proposal.md` for scope. Implementation depends on `port-dotnet10-cross-platform` being implemented, verified, and archived first. Its portable monitoring core, snapshot events, platform adapters, secret store, and versioned settings are the extension points. This change adds separate capabilities rather than editing the predecessor's unarchived deltas.

The legacy code includes start/stop requests, per-project build tokens, local acknowledgement, Claim plugin requests, custom sound paths, and an updater that checks a version file and opens an installer URL. Those implementations reference old networking or Windows UI/native APIs.

## Goals / Non-Goals

**Goals:** Restore these features through the new application's boundaries without blocking polling or changing raw Jenkins status. Keep all state-changing Jenkins requests explicit user actions.

**Non-Goals:** General parameterized-build forms, automated retries of mutation requests, remote Jenkins administration, mobile/Linux support, or unattended update installation.

## Decisions

### Dependency and configuration evolution

Begin with a task verifying the predecessor's archived contracts and packaged applications. Extend its settings through a versioned migration, activating the preserved deferred settings where valid. Keep secrets in the native secret store. New capability specs are additive and apply after the first change; this is a documented implementation dependency, not an assertion that the CLI automatically orders changes.

### Build actions use the shared Jenkins transport

Extend the existing per-server client with authenticated POST actions for starting a non-parameterized job and stopping its known running build. Target the displayed build number for stop to avoid cancelling a newer build through `/lastBuild/stop`. Reuse cookies and handle Jenkins crumb requirements for username/password sessions; requests authenticated with a user API token are exempt from CSRF protection. Keep user API tokens separate from legacy per-job build tokens. Store any per-job secret in the existing secret store.

Disable duplicate commands while one request is pending. Show acceptance separately from observed completion, and refresh monitoring after acceptance. Surface authentication, permissions, CSRF, and network failures in the UI without reporting success. Do not blindly retry POST requests after ambiguous network failures; offer refresh to determine the actual outcome. Parameter forms and broad endpoint redesign are unnecessary for restoring the existing action set.

### Local acknowledgement and Claim plugin are distinct

Local acknowledgement never changes Jenkins data or the project's raw status. Store an acknowledgement against project identity and the observed completed failing build; exclude only that matching failure from aggregate severity and duplicate failure alerts. A new completed build or explicit clear invalidates it. Running-build activity remains visible. This more predictable build-scoped policy avoids silently suppressing later failures of the same severity.

Enable Claim integration per server. Discover supported claim data for completed builds and show claim/unclaim actions only when supported. Use the shared authentication/crumb transport, return structured errors, and refresh claim metadata after success. Do not carry `ClaimService` message boxes or session assumptions into the core. Absence of the plugin must leave local acknowledgement and polling operational.

### Sound playback consumes transition events

Add a platform sound adapter to the same event stream used for notifications. Windows playback can use a platform API; macOS playback uses a native audio API. Configure and preview local WAV files for failed, still-failing, successful, and recovered completed builds. Emit at most one sound per newly observed completed build; still-failing means a new failed build following a failure, not every poll. Invalid paths or unsupported media produce a settings error and leave monitoring active. Imported paths are activated only if valid on the current machine.

Keeping playback in platform adapters is simpler than importing the old `winmm.dll` helper into the shared core. Sound and desktop notification preferences remain independent.

### Update discovery and approved release handoff

Use a versioned HTTPS release manifest carrying release version and download URL for each supported OS/architecture. Validate versions and HTTPS URLs, compare to the installed stable application version, and select only a matching artifact. Check at startup and at most hourly when enabled; allow a manual check. Repeated automatic checks do not redisplay the same dismissed version during the session.

On acceptance, open the matching release/download in the default browser. Keep the app running until the user chooses to quit; never exit merely because opening a browser was attempted. This restores the legacy handoff without introducing a second updater runtime or unattended binary replacement. Squirrel.Windows cannot be reused as the common platform updater. Missing or malformed metadata is reported on manual checks and logged unobtrusively on automatic checks.

## Risks / Trade-offs

- Jenkins installations vary in authentication/crumb policy -> Exercise accepted and denied actions against representative servers and controlled HTTP responses.
- A request can succeed before its response is lost -> Do not automatically retry; refresh and show uncertainty.
- Claim plugin endpoints vary -> Validate supported plugin behavior before enabling the integration and handle absence explicitly.
- Acknowledgement can obscure a later failure -> Scope it to a completed build and keep raw project status visible.
- Imported sound paths can be machine-specific -> Validate paths and require reselection when unavailable.
- Release links can target the wrong architecture -> Validate the manifest and refuse an unmatched platform rather than falling back.

## Migration Plan

1. Verify the initial port and archive it before beginning implementation here.
2. Upgrade settings non-destructively and add actions, acknowledgement/claims, and sound adapters in independent increments.
3. Add release-manifest production alongside update discovery; test platform selection using local fixtures.
4. Test all capabilities in packaged Windows/macOS applications while rerunning the core monitoring checks.
5. Publish release notes describing restored features. Retain a pre-migration settings backup; rollback uses that backup with the prior release.

## References

- [Jenkins CSRF protection and API-token exemption](https://www.jenkins.io/doc/book/security/csrf-protection/)
