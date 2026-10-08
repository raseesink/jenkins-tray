## Purpose

Allow users to start and stop Jenkins builds from the desktop application with authenticated requests and accurate action feedback.

## ADDED Requirements

### Requirement: Explicit build triggering
The application SHALL allow an explicit user command to trigger a selected non-parameterized job using its server credentials and any configured per-job build token. It SHALL satisfy applicable Jenkins CSRF requirements, prevent duplicate submissions while pending, and distinguish request acceptance from build completion. Parameter entry forms are outside this capability.

#### Scenario: Trigger accepted
- **WHEN** the user triggers a job and Jenkins accepts the request
- **THEN** the application reports acceptance and refreshes monitoring without claiming that the build has completed

#### Scenario: Trigger denied
- **WHEN** Jenkins rejects the request due to authentication, authorization, or CSRF policy
- **THEN** the application reports the actionable failure and does not report a queued build

### Requirement: Stop the displayed running build
The application SHALL provide an explicit stop action only for a known running build and SHALL target that build's identity. Action failures SHALL not stop polling, and mutation requests with an ambiguous outcome SHALL not be retried automatically.

#### Scenario: A newer build appears
- **WHEN** the user stops displayed running build 12 while build 13 has since become the latest build
- **THEN** the application targets build 12 and does not cancel build 13

#### Scenario: Response lost
- **WHEN** a build-action request ends without a reliable response
- **THEN** the application reports that its outcome is uncertain and offers refresh without automatically resubmitting the action
