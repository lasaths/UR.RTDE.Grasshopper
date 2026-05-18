# Changelog

All notable changes to this project are documented in this file.

## [1.6.3.1] - 2026-05-16

### Added
- Integration test coverage expansion with `TestComprehensiveFeatureSurfaceWhenConnected` to validate end-to-end `URSession` behavior across telemetry, IO, setup, kinematics, streaming, and motion.

### Changed
- Bumped plugin version to `1.6.3.1` for a patch release while keeping `UR.RTDE` dependency pinned to `1.6.3`.
- Hardened macOS Rhino 8 connect path with explicit native preload isolation (`RTLD_LOCAL`) before first RTDE client creation.
- Moved session connect/disconnect execution off the Grasshopper UI thread with safer in-flight guarding.
- Tightened macOS native copy behavior to prefer a single architecture runtime set beside the `.gha` (arm64 first, x64 fallback).
- Updated VS Code/Cursor launch configuration to prepend `RHINO_PACKAGE_DIRS` while preserving existing environment entries.

### Fixed
- `URSession` receive compatibility fallbacks for API-shape differences in `UR.RTDE` 1.6.3:
  - digital input/output bit aggregation from per-channel APIs when bitfield methods are unavailable
  - indexed analog input/output fallback when dedicated channel methods are unavailable
  - `IsProgramRunning` fallback to runtime-state evaluation when direct method is unavailable

### Compatibility
- No breaking API or component GUID changes.
- Upstream `UR.RTDE` package remains on official `1.6.3`.

## [1.3.3] - 2026-05-16

### Changed
- Upgraded **UR.RTDE** NuGet dependency from `1.2.0` to `1.6.3` (latest on nuget.org).
- Bumped plugin version to `1.3.3`.

### Compatibility
- No breaking API or component GUID changes.

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
