## Purpose

Provide a distributable desktop monitoring application with consistent lifecycle behavior on Windows and macOS.

## ADDED Requirements

### Requirement: Supported desktop distributions
The application SHALL provide self-contained distributions for Windows x64, macOS Apple Silicon, and macOS Intel, without requiring a separately installed .NET runtime. Release documentation SHALL state the supported OS versions and any unsigned-distribution setup steps.

#### Scenario: Launch on each target
- **WHEN** a user launches the distribution matching a supported operating system and architecture without a separately installed .NET runtime
- **THEN** the application starts and exposes settings and the project list

### Requirement: Background application lifecycle
The application SHALL continue monitoring when its main window is closed and SHALL provide tray/menu bar commands to show the window, refresh, open settings, and quit. Explicit quit SHALL stop polling and remove the tray/menu bar icon.

#### Scenario: Close and reopen the window
- **WHEN** the user closes the main window and later selects Show from the tray/menu bar
- **THEN** monitoring has continued and the same application reopens its window

#### Scenario: Quit the application
- **WHEN** the user selects Quit
- **THEN** in-flight monitoring is cancelled and the application exits without leaving its tray/menu bar icon active
