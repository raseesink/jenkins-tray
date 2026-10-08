## Purpose

Let users acknowledge known build failures locally and optionally manage Jenkins Claim plugin metadata without hiding raw project status.

## ADDED Requirements

### Requirement: Build-scoped local acknowledgement
The application SHALL allow users to acknowledge and clear acknowledgement of an observed completed failing build. Acknowledgement SHALL exclude that matching failure from aggregate severity and duplicate failure alerts while preserving its raw project result and running-build indication. It SHALL expire when a different completed build is observed and SHALL persist across restart while the same completed build remains current.

#### Scenario: Acknowledge an existing failure
- **WHEN** the user acknowledges a project's current completed failed build
- **THEN** that failure no longer contributes to aggregate failure severity, its raw failed status remains visible, and other projects are unaffected

#### Scenario: A new failure arrives
- **WHEN** an acknowledged project completes a different failed build
- **THEN** the old acknowledgement expires and the new failure contributes to aggregate status

#### Scenario: Restart or clear acknowledgement
- **WHEN** the application restarts while the acknowledged build is still current
- **THEN** acknowledgement remains effective until the user clears it or a different completed build appears

### Requirement: Optional Claim plugin integration
The application SHALL allow per-server opt-in to Claim plugin integration, display available claim metadata, and provide authenticated claim/unclaim actions for supported completed builds. Successful actions SHALL refresh metadata. Missing plugin support or a failed action SHALL leave monitoring and local acknowledgement functional and SHALL not fabricate a successful claim.

#### Scenario: Claim accepted
- **WHEN** the user claims a supported completed build with a reason and Jenkins accepts the action
- **THEN** refreshed metadata shows the resulting claim

#### Scenario: Plugin absent or action denied
- **WHEN** the plugin is unavailable or a claim/unclaim request is denied
- **THEN** the application explains the unavailable action or error while continuing monitoring and allowing local acknowledgement
