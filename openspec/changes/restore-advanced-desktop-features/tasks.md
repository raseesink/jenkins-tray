## 1. Predecessor and settings

- [ ] 1.1 Confirm `port-dotnet10-cross-platform` is implemented, verified, and archived; verify its four main specs exist and packaged Windows/macOS monitoring checks pass before implementing this change.
- [ ] 1.2 Extend versioned settings for action tokens, acknowledgement, Claim integration, sounds, and update preferences; verify a non-destructive migration activates valid deferred values, retains protected secret references, and creates a rollback backup.

## 2. Build actions

- [ ] 2.1 Extend shared Jenkins transport with authenticated start/stop POST requests, cookie/crumb handling, and separate per-job token references; verify accepted, authentication-denied, authorization-denied, and CSRF cases using controlled HTTP responses.
- [ ] 2.2 Add explicit start and known-running-build stop commands with pending-state duplicate prevention and refresh after acceptance; verify stopping displayed build 12 never targets a newer build 13 and accepted requests are not shown as completed builds.
- [ ] 2.3 Surface structured action failures and ambiguous outcomes without automatic mutation retries; verify dropped responses do not produce a second POST and monitoring continues.

## 3. Acknowledgement and Claim integration

- [ ] 3.1 Implement persisted project/build-scoped local acknowledgement and clear commands; verify raw failure/building activity remain visible, only matching aggregate failures are suppressed, and newer completed builds expire acknowledgement.
- [ ] 3.2 Implement per-server optional Claim metadata discovery and supported claim/unclaim requests; verify supported-plugin behavior with representative responses and absent-plugin behavior leaves monitoring operational.
- [ ] 3.3 Add claim metadata and action views with reason input and structured errors; verify accepted claim/unclaim refreshes metadata and rejected actions do not fabricate success.

## 4. Custom sounds

- [ ] 4.1 Add Windows/macOS WAV playback adapters and file selection/clear/preview controls; verify valid preview on both platforms and imported missing paths request reselection.
- [ ] 4.2 Connect sound preferences to completed-build events independently of desktop notification preferences; verify failed/still-failing/successful/recovered mapping and silence for initial or unchanged snapshots.
- [ ] 4.3 Isolate playback errors and disable repeated attempts for invalid selections until corrected; verify missing/unsupported files do not interrupt polling or other notifications.

## 5. Update discovery

- [ ] 5.1 Define and produce a versioned HTTPS release manifest with stable version and per-OS/architecture download URLs; verify fixtures cover every supported artifact and reject malformed versions/non-HTTPS URLs.
- [ ] 5.2 Implement cancellable startup/hourly automatic checks, opt-out, and manual checks; verify timing, version comparison, disabled checks, missing artifacts, and service failures without blocking monitoring.
- [ ] 5.3 Implement session dismissal tracking and explicit approval before opening a matching download; verify each target selects its own artifact, dismissed versions do not reprompt automatically, and accepting does not install or exit the application.

## 6. Cross-platform acceptance

- [ ] 6.1 Exercise build actions and optional Claim integration against representative authenticated Jenkins setups; verify explicit action results, denied requests, and continued polling.
- [ ] 6.2 Run packaged Windows/macOS checks for acknowledgement persistence/expiry, custom sounds, and update handoff while rerunning initial-port monitoring checks; record results in a validation artifact.
- [ ] 6.3 Update feature and release documentation, including non-parameterized action scope and approved download handoff; verify instructions match actual behavior and all four new capability specs pass implementation review.
