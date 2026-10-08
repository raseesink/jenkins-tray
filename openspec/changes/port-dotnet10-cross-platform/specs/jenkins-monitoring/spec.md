## Purpose

Discover Jenkins projects and keep selected build statuses current across multiple servers while keeping failures isolated and navigation available.

## ADDED Requirements

### Requirement: Authenticated recursive project discovery
The application SHALL discover projects from configured servers, including jobs nested in folders and multibranch pipelines, using each server's configured credentials and certificate policy. Projects with duplicate references SHALL appear once per server.

#### Scenario: Discover nested jobs
- **WHEN** a server exposes jobs inside nested folders and multibranch pipelines
- **THEN** selectable leaf jobs are returned with identities and URLs that retain their server association

#### Scenario: Discovery denied
- **WHEN** Jenkins rejects the configured credentials
- **THEN** the application reports an authentication error for that server without preventing configuration of other servers

### Requirement: Coordinated refresh and error isolation
The application SHALL refresh selected projects at startup, delay the configured interval after each completed cycle, and allow manual refresh without overlapping cycles. Requests SHALL have bounded duration and be cancellable. A failed server/project request SHALL leave other monitored projects operational and identify affected data as unavailable or stale.

#### Scenario: Manual refresh during polling
- **WHEN** the user requests refresh while a cycle is active
- **THEN** the application coalesces that request without starting an overlapping cycle

#### Scenario: One server is unreachable
- **WHEN** one configured server times out while another responds
- **THEN** the responding server's project statuses update and the unreachable server's data is marked unavailable or stale

### Requirement: Build status and navigation
The application SHALL show projects grouped by server, their raw build result, building/queued activity where supplied, and available latest-build details. It SHALL distinguish successful, unstable, failed, aborted, disabled, and unknown/unavailable states. It SHALL allow opening project and available console URLs in the default browser without submitting build mutations.

#### Scenario: Observe an active failed project
- **WHEN** Jenkins reports a project building after its last failed completed build
- **THEN** the project list retains the failure result and also indicates building activity

#### Scenario: Navigate to Jenkins
- **WHEN** the user opens a project page or an available build console
- **THEN** the corresponding Jenkins URL opens in the default browser
