# Changelog

All notable changes to this project are documented in this file.

## [1.3.2] - 2026-03-21

### Added
- Direct gripper position-drive workflow in `UR Gripper`, including queued move handling for rapid input changes.
- Visual `AUTO-SENT` run-button state in `UR Write` when auto-send motion is active.
- Runtime assembly resolver in `URSession` to improve dependency loading reliability.

### Changed
- Refactored `UR Gripper` execution flow to async background task handling with safer state updates.
- Improved session connect UI feedback to surface detailed `LastError` messages on failures.
- Updated build copy step to clean per-target framework output folder in Grasshopper Libraries.
- Bumped project version to `1.3.2`.

### Fixed
- Motion trigger/run checks in `UR Write` to better align with active motion actions.
- Connection button behavior in session attributes to reduce unnecessary full recomputes.

### Docs
- Clarified README connection guidance: enable **Remote Control** in PolyScope for motion/control commands.

### Compatibility
- No breaking API or component GUID changes.

## [1.3.1] - 2025-09-20

- Previous release.
