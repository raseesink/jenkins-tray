## Purpose

Provide configurable local audio feedback for completed Jenkins build results on Windows and macOS without disrupting monitoring.

## ADDED Requirements

### Requirement: Configurable and previewable sounds
The application SHALL allow users to enable sound notifications independently of desktop notifications and select or clear local WAV sounds for failed, still-failing, successful, and recovered completed-build events. It SHALL provide preview and persist valid preferences on both supported operating systems.

#### Scenario: Configure and preview
- **WHEN** the user selects a valid WAV file and previews it
- **THEN** it plays on the current operating system and the selection is restored after restart

### Requirement: Completed-build sound delivery
When enabled, the application SHALL play at most one configured sound per newly observed completed build: failed when entering failure, still-failing for another failed build following failure, recovered for success following failure/instability, and successful for other successful builds. Initial snapshots and unchanged polls SHALL not play transition sounds; missing sound selections SHALL be silent.

#### Scenario: Repeated failure polling
- **WHEN** repeated polls return the same completed failed build
- **THEN** no additional sound is played for that build

#### Scenario: Another failed build completes
- **WHEN** a known failed project completes a different failed build
- **THEN** the configured still-failing sound plays once

### Requirement: Playback errors remain isolated
The application SHALL report unavailable or unsupported selected files and playback failures without interrupting monitoring. Imported legacy sound paths SHALL be validated before activation on the current machine.

#### Scenario: Imported path is unavailable
- **WHEN** a migrated sound path does not exist on the current machine
- **THEN** the application requests reselection and continues monitoring without repeated playback attempts for that invalid selection
