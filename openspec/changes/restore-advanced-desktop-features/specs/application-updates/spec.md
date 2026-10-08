## Purpose

Discover newer desktop releases and guide users to an appropriate platform download through an explicit approved handoff.

## ADDED Requirements

### Requirement: Automatic and manual update discovery
The application SHALL allow users to enable or disable automatic update checks and initiate a manual check. When enabled, automatic checks SHALL run at startup and no more frequently than hourly. Checks SHALL compare the installed version to valid stable-release metadata fetched over HTTPS without blocking monitoring.

#### Scenario: Automatic checks disabled
- **WHEN** automatic checks are disabled and the application starts
- **THEN** no automatic check occurs and a manual check remains available

#### Scenario: No newer release
- **WHEN** a manual check returns a version no newer than the installed version
- **THEN** the application reports that it is up to date

### Requirement: Platform-matching approved update handoff
The application SHALL offer an update only when a newer release has an HTTPS download matching the current operating system and architecture. Only explicit user acceptance SHALL open that download in the default browser. Dismissal SHALL not cause repeated automatic prompts for the same version during the session. The application SHALL not install updates or exit automatically as part of this handoff.

#### Scenario: Accept a matching release
- **WHEN** a macOS Apple Silicon user accepts an available update
- **THEN** the macOS Apple Silicon download opens and the application continues monitoring

#### Scenario: Dismiss an update
- **WHEN** the user dismisses an offered version and another automatic check finds the same version
- **THEN** that version is not prompted again during the same session

### Requirement: Invalid or unavailable update metadata
The application SHALL reject malformed versions, non-HTTPS download URLs, and releases without a matching platform artifact. Manual checks SHALL report these problems; automatic check failures SHALL not disrupt monitoring or launch an unrelated installer.

#### Scenario: Missing matching artifact
- **WHEN** a newer release includes only downloads for other platforms or architectures
- **THEN** the application does not offer those downloads as an update for this machine

#### Scenario: Unavailable update service
- **WHEN** the update service cannot be reached
- **THEN** a manual check reports the error and monitoring remains active
