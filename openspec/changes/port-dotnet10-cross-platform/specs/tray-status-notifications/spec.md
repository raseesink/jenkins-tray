## Purpose

Keep aggregate Jenkins health visible in the desktop tray or menu bar and notify users of meaningful build-result transitions.

## ADDED Requirements

### Requirement: Aggregate status and building indication
The tray/menu bar SHALL represent the worst selected project's status separately from the project's raw result display and SHALL indicate whether any selected project is building. Failed builds SHALL take precedence over unstable and successful builds; unavailable data SHALL prevent an all-good presentation. No selected projects SHALL produce a neutral state. Status meaning SHALL remain distinguishable in light and dark desktop appearance.

#### Scenario: Mixed build results
- **WHEN** selected projects include successful, unstable, and failed builds
- **THEN** aggregate status indicates failure

#### Scenario: Building and incomplete data
- **WHEN** one selected project is building and another cannot be refreshed
- **THEN** aggregate status indicates incomplete health and building activity rather than all-good health

#### Scenario: No projects selected
- **WHEN** no projects are selected
- **THEN** the tray/menu bar presents a neutral state and the application offers configuration

### Requirement: Transition notifications
When notifications are enabled and permitted by the OS, the application SHALL submit native desktop notifications for a known completed build regressing from success to unstable/failure, worsening from unstable to failure, or recovering from unstable/failure to success. It SHALL identify the server/project and result, and SHALL emit at most one notification for each observed completed-build transition. Initial snapshots, unchanged results, and unavailable-data transitions SHALL not generate build-regression or recovery notifications.

#### Scenario: Regression and repeated polling
- **WHEN** a known successful project completes a failed build and subsequent polls return that same build
- **THEN** the application submits one regression notification and does not repeat it on unchanged polls

#### Scenario: Recovery
- **WHEN** a known failed project completes a successful build
- **THEN** the application submits one recovery notification

#### Scenario: Initial or unavailable status
- **WHEN** the first snapshot contains a failure or a request becomes unavailable
- **THEN** status remains visible without generating a build-transition notification

### Requirement: Notification controls and permission handling
Users SHALL be able to enable or disable desktop notifications. The application SHALL handle required OS permission prompts and show notification unavailability without disrupting monitoring, settings, or tray status.

#### Scenario: Permission denied
- **WHEN** the user denies OS notification permission
- **THEN** monitoring continues and settings indicate that desktop notifications are unavailable

#### Scenario: Notifications disabled
- **WHEN** notifications are disabled and a build regresses
- **THEN** project and aggregate status update without submitting a notification
