## 1. Runtime and platform foundations

- [ ] 1.1 Create SDK-style .NET 10 core, shared Avalonia desktop, platform host, and test projects; pin dependencies and verify restore/build on Windows and macOS.
- [x] 1.2 Replace Spring.NET container/collections and legacy logging in the new runtime with standard .NET services; verify the dependency graph excludes WinForms, DevExpress, and obsolete bundled runtime dependencies.
- [ ] 1.3 Implement tray/menu bar Show, Refresh, Settings, close-to-hide, and Quit behavior; verify window reopening and process termination on both platforms.
- [ ] 1.4 Establish Windows notification identity and a macOS application bundle with native notification adapters; verify delivery and denied-permission behavior from packaged applications on both platforms.
- [ ] 1.5 Implement Windows Credential Manager/macOS Keychain adapters; verify secret save/read/remove and denied-write behavior without plaintext fallback.

## 2. Shared monitoring core

- [x] 2.1 Port domain models and extract raw status, aggregate severity, and completed-build transition rules from Windows UI types; verify mixed results, unavailable data, initial snapshots, regression, and recovery with fixture-based tests.
- [x] 2.2 Replace synchronous/thread-static networking with per-server async clients, timeouts, cancellation, and certificate policy; verify authentication, one-server certificate overrides, timeout, and cancellation using controlled HTTP responses.
- [x] 2.3 Port XML discovery and recursive folder/multibranch traversal; verify nested jobs, duplicate references, and server/project identities using representative XML fixtures.
- [x] 2.4 Port selected-project build retrieval and publish snapshots with raw results, activity, and available details; verify missing builds, active builds, malformed responses, and partial failures with fixtures.
- [x] 2.5 Implement bounded polling with immediate startup, completion-based interval, and coalesced manual refresh; verify no overlapping cycles, independent server progress, and prompt cancellation with deterministic coordinator tests.

## 3. Settings and migration

- [x] 3.1 Add versioned settings, per-user paths, validation, and atomic persistence with secret references; verify round trips, invalid intervals/URLs, damaged-file handling, and failed writes retaining prior settings.
- [x] 3.2 Implement add/edit/remove server and recursive project-selection views; verify selected projects survive restart and one server's failure does not block settings for others.
- [x] 3.3 Implement opt-in legacy JSON import, including credential decoding, deferred secret references/settings, and explicit file selection; verify imported associations/preferences and byte-for-byte preservation of the source file.
- [x] 3.4 Implement import failure handling and deferred-setting preservation; verify invalid files or unavailable secret storage leave active settings unchanged and omit unsupported feature controls.

## 4. Monitoring interface and notifications

- [x] 4.1 Build the server-grouped project list with status, activity, available latest-build details, and stale/unavailable indications; verify snapshots update the UI without blocking interactions.
- [ ] 4.2 Add default-browser navigation for project and available console URLs; verify correct URLs on Windows and macOS without submitting Jenkins mutations.
- [ ] 4.3 Connect aggregate status and running-build indication to tray/menu bar visuals; verify mixed-state, no-project, and incomplete-data cases in light/dark desktop appearance.
- [x] 4.4 Add persisted notification preferences and permission/unavailability feedback; verify denied permissions and disabled preferences leave monitoring functional.
- [x] 4.5 Connect completed-build events to native notifications; verify initial/unchanged/unavailable snapshots remain silent and regression/recovery produce one notification per transition.

## 5. Distribution and acceptance

- [ ] 5.1 Replace active legacy packaging entry points with self-contained Windows x64 and macOS arm64/x64 artifacts and platform build jobs; verify published bundles launch without a separately installed .NET runtime.
- [ ] 5.2 Update build/release documentation with workload setup, supported OS matrix, settings migration, unsigned local launch steps, signing prerequisites, rollback, and deferred features; verify documented commands produce the named artifacts.
- [ ] 5.3 Run packaged application acceptance checks for configuration/import, nested jobs, polling, links, tray lifecycle, notification permission/delivery, and offline recovery on Windows and both macOS architectures; record results and limitations in a validation artifact.
- [ ] 5.4 Verify implementation against all four capability specs and archive this change before beginning `restore-advanced-desktop-features`; verify main specs contain the completed behavior and the dependent change remains unimplemented.
