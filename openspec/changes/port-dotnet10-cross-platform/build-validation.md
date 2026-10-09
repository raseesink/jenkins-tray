# Port validation — 2026-10-09

Current implementation progress: **13/23 tasks complete**. Current results are
under **Implementation and acceptance** below; preceding investigation sections
are retained as history.

## Initial build investigation

Implementation progress remains 0/23 tasks. This records build investigation,
not packaged application acceptance or completion of task 1.1.

## Environment

- macOS arm64, .NET SDK 10.0.401 / MSBuild 18.9.11.
- Installed runtimes: .NET 10.0.8 and 10.0.12.
- macOS workload 27.0.10722; Xcode 27.0.
- Existing untracked scaffold: portable core, Avalonia desktop, Windows/macOS
  hosts, and xUnit test project. No application functionality added during this
  investigation.

## Results

- Core, test project, and shared desktop compile from the existing restored
  assets. The test project has no tests, so compilation is not behavioral proof.
- Avalonia build telemetry initially attempts a write outside the workspace.
  `AVALONIA_TELEMETRY_OPTOUT=1` avoids that write.
- macOS dependency copying crashes with `System.OverflowException` in
  `Microsoft.Build.Tasks.FileState.FileDirInfo.ThrowNonIoExceptionIfPending`.
  Single-threaded copying and execution outside the sandbox both reproduce it.
- A standalone .NET probe identifies the underlying exception as
  `Interop.Sys.IsMemberOfGroup(UInt32 gid)`, reached via `FileInfo.Length` when
  checking root-owned files. Both installed runtimes reproduce it for the
  system apphost and dotnet executable. User-owned dependency files do not.
  This is an environment/runtime interaction; no application code is involved.
- Copying the required installed packs into a user-owned temporary location and
  selecting that location gets the macOS build past copying and through host
  assembly compilation. Native bundle construction has not been verified.
- Windows build cannot copy its generated `apphost.exe`; the required Windows
  apphost package is absent from the local cache.
- Locked restore reports NU1004: core/desktop locks contain `osx-arm64` while
  those projects declare no runtime identifiers; the macOS lock contains only
  `osx-arm64` while the host declares both `osx-arm64` and `osx-x64`.
- Restore cannot resolve `api.nuget.org:443` (NU1301). Vulnerability metadata is
  also unavailable (NU1900). Requesting execution with network access does not
  resolve this. Lock regeneration and complete platform restore remain blocked.

## Temporary pack workaround

Do not change ownership or permissions on the system installation. Copy these
installed pack directories into a user-owned temporary dotnet root, retaining
the `packs/<pack-name>/<version>/` structure:

- `Microsoft.macOS.Sdk.net10.0_27.0`
- `Microsoft.macOS.Ref.net10.0_27.0`
- `Microsoft.macOS.Runtime.osx.net10.0_27.0`
- `Microsoft.macOS.Runtime.osx-arm64.net10.0_27.0`
- `Microsoft.macOS.Runtime.osx-x64.net10.0_27.0`
- `Microsoft.NETCore.App.Host.osx-arm64`

For this investigation they were copied beneath
`/private/tmp/jenkins-tray-dotnet/packs`. Other installed pack directories were
linked into that directory so the SDK could still locate them. The command
which passed the original macOS copy failure was:

```sh
AVALONIA_TELEMETRY_OPTOUT=1 \
DOTNETSDK_WORKLOAD_PACK_ROOTS=/private/tmp/jenkins-tray-dotnet \
dotnet build src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj \
  --no-restore --disable-build-servers -m:1 \
  -p:NetCoreTargetingPackRoot=/private/tmp/jenkins-tray-dotnet/packs
```

Once NuGet connectivity is restored, regenerate inconsistent locks with a
normal restore, inspect the resulting dependency changes, then repeat restore
in locked mode before building. Keep package versions pinned and vulnerability
checking enabled. Windows runtime validation and macOS packaged acceptance
remain outstanding; no change has been archived.

## Follow-up — 2026-10-09

The checks below supersede the earlier restore/build blockers, while retaining
the investigation above as history.

- NuGet DNS still fails inside the sandbox. An approved connectivity check
  outside the sandbox returns HTTP 200 from the NuGet v3 service index.
- `dotnet restore JenkinsTray.Modern.slnx --disable-parallel --force-evaluate`
  succeeds outside the sandbox and regenerates the five project lock files
  with the declared runtime identifiers.
- A subsequent `dotnet restore JenkinsTray.Modern.slnx --locked-mode
  --disable-parallel` succeeds outside the sandbox for all five projects.
- With `AVALONIA_TELEMETRY_OPTOUT=1`, the Windows host builds on this macOS
  arm64 machine using `dotnet build
  src/JenkinsTray.Windows/JenkinsTray.Windows.csproj --no-restore
  --disable-build-servers -m:1`: zero warnings and zero errors. This is a
  cross-compilation check, not verification on Windows.
- The sandboxed solution build reaches macOS host assembly compilation but
  stops producing output during native bundle construction. It was cancelled
  and terminated before retrying outside the sandbox.
- With the telemetry opt-out, `dotnet build
  src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj --no-restore
  --disable-build-servers -m:1` succeeds outside the sandbox: zero warnings and
  zero errors. It produces both `osx-arm64/JenkinsTray.app` and
  `osx-x64/JenkinsTray.app` beneath `bin/Debug/net10.0-macos/`. No temporary pack
  workaround is needed for this build.

Task 1.1 remains unchecked because restore/build on a Windows machine has not
been verified. This workspace provides macOS arm64 only. Application launch,
tray lifecycle, native notifications, secret storage, and packaged acceptance
were not tested; the existing scaffold still only creates an empty window.
Progress remains 0/23 tasks complete. No change has been archived and the
dependent advanced-features change has not been implemented.

## Implementation and acceptance — 2026-10-09

Current progress is **13/23 tasks complete**. The earlier 0/23 sections are
historical investigation notes. Windows build verification was explicitly
postponed to the end at the user's request; implementation continued through
all feature areas before the final Windows checks.

### Automated results

- Core: **47 tests passed**, no failures or skips. Covers raw results and
  aggregate health, server-specific transition baselines, XML folder and
  multibranch fixtures, duplicate/cyclic discovery references, active/missing
  builds, malformed responses, authentication, isolated certificate policy,
  timeouts/cancellation, polling bounds and coalescing, completion-based
  intervals with a fake clock, very large positive intervals, independent
  server progress, persistence, refused disk/secret writes, observer failure,
  non-destructive legacy import with protected deferred project tokens,
  overlapping preference/server saves, and polling outside the calling UI
  synchronization context.
- Desktop: **4 headless tests passed**, no failures or skips. Uses actual
  settings controls for server add/edit/remove and confirmation, nested job
  selection and disk reload, invalid interval feedback, failed-server discovery
  isolation, notification-denial feedback, monitoring rows and stale states,
  exact project/console navigation arguments, and ten distinct status/activity
  bitmap representations.
- Locked restore succeeds for core tests, desktop tests, macOS, and Windows.
  Six project lock files pin the resolved dependency graphs. No Spring.NET,
  Common.Logging, WinForms, DevExpress, SmartThreadPool, or Squirrel runtime
  dependencies occur in the new dependency locks.
- `openspec validate port-dotnet10-cross-platform --strict` succeeds.
- `git diff --check` succeeds. Existing untracked scaffold files were preserved
  and extended; no legacy configuration or release was modified.

### macOS packaged checks

Both documented commands produce the named ZIP and `.app` artifacts:

```sh
sh scripts/publish-macos.sh osx-arm64
sh scripts/publish-macos.sh osx-x64
```

The workload requires `PublishTrimmed=true`; `LinkMode=None` disables linking.
The scripts select one RID while clearing the multi-RID override, so each
publish uses the intended architecture. A final launch exposed stale mixed-architecture runtime libraries left by earlier incremental multi-RID publishes. The scripts now clean generated native caches and verify the executable and every bundled dylib with `lipo -verify_arch` before archiving; both distributions were rebuilt and rechecked. Bundles have identifier
`io.jenkinstray.desktop`, minimum macOS 15.0, menu-bar application metadata,
and bundled `libhostfxr.dylib` / `libcoreclr.dylib`.

An isolated diagnostic launch used a nonexistent `DOTNET_ROOT` and disabled
multilevel runtime lookup. It used temporary settings and a unique native
secret reference that was removed after the check. Both processes exited
successfully. The arm64 check ran natively on macOS 26.6.2; the x64 check ran
under Rosetta on the same Apple Silicon machine.

| Packaged check | arm64 native | x64 under Rosetta |
| --- | --- | --- |
| Main window initially visible | Passed | Passed |
| Close hides window | Passed | Passed |
| Show reopens window | Passed | Passed |
| Settings opens | Passed | Passed |
| Tray requested visible; all status icons load | Passed | Passed |
| Keychain secret save/read/remove | Passed | Passed |
| Notification authorization query | Denied (final non-prompting query) | Denied (final non-prompting query) |
| Native notification delivery / permission denial | Not verified | Not verified |
| Explicit Quit ends the process | Passed | Passed |

These checks do not establish actual tray-menu interaction, light/dark desktop
inspection, refused native Keychain writes, browser launch, notification
presentation, or the full Jenkins configuration/import/offline acceptance
scenario in the packaged UI. Rosetta is supplementary evidence; physical
Intel-machine acceptance is still required.

Final machine-readable smoke evidence is retained in
`smoke-macos-arm64.json` and `smoke-macos-x64-rosetta.json`. Earlier queries
returned Unknown; both final queries returned Denied without requesting
permission, and the lifecycle and Keychain checks still completed successfully.

### Final Windows checks

- Windows restore and locked restore succeed with the final notification SDK
  and self-contained .NET runtime pack.
- Production `dotnet publish` reaches Windows App SDK native manifest
  construction and fails because its `mt.exe` cannot execute on macOS (exit
  126 / MSB3073). The first attempt also exposed a missing .NET runtime pack;
  declaring `SelfContained=true` in the host and restoring resolved that issue.
- Diagnostic assembly compilation succeeds with **zero warnings and errors**:

  ```sh
  AVALONIA_TELEMETRY_OPTOUT=1 dotnet build \
    src/JenkinsTray.Windows/JenkinsTray.Windows.csproj -c Release \
    --no-restore --disable-build-servers \
    -p:WindowsAppSDKSelfContained=false -p:AppxGeneratePriEnabled=false
  ```

  This override is only a compilation check. It does not validate or replace the
  production self-contained manifest, notification registration, or shipped
  distribution. Production project and packaging settings retain
  `WindowsAppSDKSelfContained=true`.
- The Windows build job invokes `scripts/package-release.ps1` on a Windows
  runner, where `mt.exe` can execute. The workflow has been added locally, not
  run remotely. No final Windows ZIP or Windows runtime acceptance is claimed.

### Remaining acceptance and archive gate

Tasks 1.1, 1.3, 1.4, 1.5, 4.2, 4.3, 5.1, 5.2, 5.3, and 5.4 remain unchecked.
Their code/scripts/documentation are implemented, but their full required
platform checks are not complete. Finish the Windows build and packaged
lifecycle/credential/browser/notification checks, run physical Intel and full
macOS acceptance, and inspect light/dark status visuals. Native permission
and denied-write cases must be recorded rather than inferred from fake-provider
tests.

The capability review is in `verification.md`. This change has not been
archived or copied to main specs. The dependent
`restore-advanced-desktop-features` change remains entirely unchecked and no
advanced-feature implementation was added.
