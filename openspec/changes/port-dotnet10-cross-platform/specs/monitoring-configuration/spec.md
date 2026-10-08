## Purpose

Let users configure monitored Jenkins servers and projects and retain their settings across restarts and migration from the legacy Windows application.

## ADDED Requirements

### Requirement: Server and project configuration
The application SHALL allow users to add, edit, and remove servers with a URL, display name, optional username/password or API token, and an explicit per-server untrusted-certificate override that defaults off. Users SHALL select monitored projects independently for each server and configure a positive polling interval, defaulting to 15 seconds.

#### Scenario: Save server and selected projects
- **WHEN** the user saves a valid server and selects projects
- **THEN** the application monitors only the selected projects and restores the configuration after restart

#### Scenario: Reject invalid settings
- **WHEN** the user supplies an invalid server URL or nonpositive polling interval
- **THEN** the application shows a validation error and retains the prior valid configuration

### Requirement: Durable settings and protected credentials
The application SHALL persist settings in a per-user location, store credentials in the operating system's protected secret storage, and omit secret values from application settings and logs. Failed persistence SHALL be reported without replacing the previous valid settings with an incomplete file or silently storing unprotected credentials.

#### Scenario: Secret storage unavailable
- **WHEN** the operating system refuses to save a credential
- **THEN** the application reports the failure and does not write the secret into ordinary settings as a fallback

#### Scenario: Damaged settings file
- **WHEN** stored configuration cannot be parsed
- **THEN** the application reports the problem and leaves that file intact until the user explicitly chooses recovery

### Requirement: Non-destructive legacy JSON import
The application SHALL offer import of the legacy Windows JSON configuration when no new settings exist, and SHALL allow selection of a copied legacy file on either platform. Import SHALL preserve server/project associations, credential values, polling interval, and notification preference, while retaining deferred-feature values as inactive data. The source file SHALL remain unchanged.

#### Scenario: Import legacy settings
- **WHEN** the user imports a valid `jenkins.configuration` file
- **THEN** supported preferences and selected projects become available, credentials enter protected storage, and deferred values remain inactive without being discarded
- **AND** the original file is unchanged

#### Scenario: Import cannot complete
- **WHEN** the selected file is invalid or imported credentials cannot be saved
- **THEN** import reports the failure without replacing the active configuration or modifying the source file
