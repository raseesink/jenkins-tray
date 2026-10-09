# Verification: port-dotnet10-cross-platform

## Summary

| Dimension | Result |
| --- | --- |
| Completeness | 13/23 tasks complete; implementations found for all 11 requirements |
| Correctness | 47 core and 4 headless UI tests pass; packaged macOS lifecycle/Keychain smoke passes; full native acceptance remains open |
| Coherence | Shared .NET 10 core/Avalonia UI, native adapter boundaries, async polling, protected credentials, non-destructive migration, and deferred feature boundaries follow the design |

This review reads proposal, design, tasks, and all four delta specs. It does not
substitute a code search or mock-provider test for an outstanding packaged
platform check. Detailed commands and platform evidence are in
[build-validation.md](build-validation.md).

## Requirement and scenario mapping

| Requirement | Implementation | Verification and limits |
| --- | --- | --- |
| Supported desktop distributions | `src/JenkinsTray.Windows/JenkinsTray.Windows.csproj`, `src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj`, `scripts/package-release.ps1`, `scripts/publish-macos.sh`, `.github/workflows/desktop.yml`, `RELEASE.md` | Both macOS artifacts produced and launched; Windows manifest publishing requires a Windows runner. Physical Intel acceptance remains open. |
| Background application lifecycle | `src/JenkinsTray.Desktop/App.cs:39`, `App.cs:109`, `App.cs:166`, `src/JenkinsTray.Core/PollingCoordinator.cs:113` | Packaged macOS close/hide/reopen/settings/Quit passes; deterministic cancellation passes. Windows tray lifecycle and interactive menu commands remain open. |
| Authenticated recursive project discovery | `src/JenkinsTray.Core/JenkinsClient.cs:19`, `JenkinsClient.cs:52`, `JenkinsClient.cs:70`, `src/JenkinsTray.Desktop/SettingsWindow.cs:173` | XML folder/multibranch/duplicate/cycle fixtures and controlled authenticated responses pass; failed authentication on one server leaves another configurable in UI tests. |
| Coordinated refresh and error isolation | `src/JenkinsTray.Core/PollingCoordinator.cs:20`, `PollingCoordinator.cs:55`, `src/JenkinsTray.Desktop/App.cs:184` | Immediate startup, deterministic completion-based interval, coalesced manual refresh, bounded requests, responsive-server progress, partial failure, and prompt cancellation pass. UI snapshot publication is coalesced; transition observation retains every publication. |
| Build status and navigation | `src/JenkinsTray.Core/JenkinsClient.cs:97`, `src/JenkinsTray.Desktop/MonitoringView.cs:16`, `src/JenkinsTray.Desktop/DesktopRuntime.cs` | Active build retains completed failure, missing/malformed builds and available details tested. UI checks exact project/console URLs and stale/raw result labels. Actual default-browser launch on both OSes remains open. |
| Server and project configuration | `src/JenkinsTray.Core/SettingsStore.cs:45`, `src/JenkinsTray.Core/SettingsService.cs:58`, `src/JenkinsTray.Desktop/SettingsWindow.cs` | Actual UI add/edit/remove and recursive selection tests pass; disk reload preserves selection; invalid intervals/URLs preserve active settings; certificate override defaults off. |
| Durable settings and protected credentials | `src/JenkinsTray.Core/SettingsStore.cs:25`, `src/JenkinsTray.Core/SettingsService.cs:45`, `src/JenkinsTray.Windows/WindowsSecretStore.cs`, `src/JenkinsTray.MacOS/MacSecretStore.cs` | Atomic persistence, damaged-file preservation and explicit backup recovery, failed disk/native-provider writes, and committed-secret protection pass. Packaged Keychain round trip passes; Windows Credential Manager and refused native writes remain open. |
| Non-destructive legacy JSON import | `src/JenkinsTray.Core/SettingsService.cs:114`, `src/JenkinsTray.Desktop/SettingsWindow.cs:188` | Fixture verifies source bytes, credentials, associations, polling/notification preferences, inactive compatibility values, protected project tokens, and rollback after partial secret failure. Explicit picker/confirmation and first-run Windows offer are implemented; full packaged import flow remains open. |
| Aggregate status and building indication | `src/JenkinsTray.Core/Models.cs:48`, `src/JenkinsTray.Desktop/TrayVisual.cs:12`, `src/JenkinsTray.Desktop/App.cs:200` | Mixed results, incomplete health with building activity, neutral state, and ten distinct bitmap representations pass. Icons load in native macOS bundles; actual light/dark tray appearance remains open. |
| Transition notifications | `src/JenkinsTray.Core/Models.cs:62`, `src/JenkinsTray.Core/NotificationDispatcher.cs`, `src/JenkinsTray.Windows/WindowsNotifications.cs`, `src/JenkinsTray.MacOS/MacNotifications.cs` | Initial, repeated, unavailable, regression/recovery, server-specific baselines, disabled preferences, and denied fake-provider permission pass. Native packaged delivery remains open. |
| Notification controls and permission handling | `src/JenkinsTray.Desktop/SettingsWindow.cs:92`, `src/JenkinsTray.Desktop/App.cs:100`, native notification adapters | Persisted controls, automatic request when enabled, explicit permission check, and denied/unavailable feedback are implemented without coupling to polling. Final native authorization queries return Denied and lifecycle/Keychain smoke checks still pass; actual permission prompts, delivery, and monitoring under native denial remain open. |

## Critical: finish before archive

Each unchecked task is an archive gate:

1. **1.1:** Run final locked restore/build on Windows; current evidence is macOS and diagnostic Windows cross-compilation.
2. **1.3:** Verify actual Windows tray Show/Refresh/Settings/close-to-hide/Quit, and interactive menu commands on both platforms.
3. **1.4:** Verify native notification identity, delivery, and denied permission from both final distributions.
4. **1.5:** Verify Windows native credential round trip and denied native writes on both platforms without plaintext fallback.
5. **4.2:** Open actual default-browser project and console links on both platforms; confirm URLs and absence of Jenkins mutations.
6. **4.3:** Inspect mixed/no-project/incomplete/building tray visuals in light and dark desktop appearance.
7. **5.1:** Produce final Windows ZIP and launch each target with bundled runtime; confirm physical Intel execution.
8. **5.2:** Run the documented Windows packaging command and confirm the named artifact; macOS commands are verified.
9. **5.3:** Complete the full packaged acceptance matrix for configuration/import, nested jobs, polling, links, native lifecycle/permissions, and offline recovery.
10. **5.4:** Once the above passes, repeat capability verification, sync/archive the completed change, and confirm the dependent advanced-feature change stays unimplemented.

## Warnings

- Monitoring under native permission denial, refused credential writes, browser handoff, and full
  end-to-end packaged Jenkins scenarios lack native acceptance evidence. Run
  the matrix in `RELEASE.md` and attach results to `build-validation.md`.
- The added CI workflow has not executed remotely. Run its Windows job to
  validate the production manifest/publish path and its platform jobs to confirm
  hosted-runner workload/Xcode compatibility.
- Rosetta execution is not physical Intel-machine acceptance. Run the Intel
  bundle on a supported Intel Mac before completing task 5.3.

## Coherence and deferred scope

The new runtime has no legacy project references or Spring/WinForms/DevExpress
runtime dependencies. Settings and import use separate per-user paths and
protected secret references, preserve deferred values without controls, and
never overwrite the legacy source. All Jenkins requests are HTTP GETs; no
build/Claim/sound/updater implementation was introduced. The dependent
`restore-advanced-desktop-features/tasks.md` remains fully unchecked. Main specs
have not been updated to claim unverified completed behavior.

**Assessment:** Ten incomplete-task gates remain. The implementation is not
ready for archive until their required platform acceptance checks pass.
