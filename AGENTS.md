# AGENTS.md

This document provides information for AI agents and automated systems working with the UR.RTDE.Grasshopper codebase.

## Project Overview

**Name**: UR.RTDE.Grasshopper  
**Version**: 1.6.3.6  
**Type**: Grasshopper plugin for Rhino  
**Purpose**: Control Universal Robots via RTDE (Real-Time Data Exchange) protocol from Grasshopper, including Robotiq grippers (URCap)  
**Language**: C# (.NET)  
**Framework Targets**: net48 (Rhino 7), net8.0, net8.0-windows (Rhino 8)

## Project Structure

```
UR.RTDE.Grasshopper/
├── Components/           # Grasshopper components
│   ├── UR_SessionComponent.cs       # Session management component
│   ├── UR_SessionAttributes.cs      # Custom UI attributes for session component
│   ├── UR_ReadComponent.cs          # Read robot state (timer polling, FK/IK)
│   ├── UR_WriteComponent.cs         # Discrete commands (motion, IO, TCP)
│   ├── UR_StreamComponent.cs        # Continuous SpeedJ/ServoJ streaming
│   └── UR_GripperComponent.cs       # Robotiq gripper control component
├── Types/               # Custom Grasshopper types
│   ├── URSessionGoo.cs              # Grasshopper data type wrapper for URSession
│   └── URSessionParam.cs            # Custom parameter for session inputs
├── Runtime/             # Core runtime functionality
│   └── URSession.cs                # UR.RTDE wrapper; control connect fallback + port 30003 diagnostics
├── Utils/               # Utility functions
│   ├── PoseUtils.cs                 # Pose conversion utilities
│   ├── GrasshopperUiDraw.cs         # Shared UI drawing helpers for attributes
│   └── NativeBootstrap.cs           # macOS + Windows native preload before RTDE P/Invoke
├── UR_PluginPriority.cs             # GH_AssemblyPriority: bootstrap at Grasshopper load
├── Resources/
│   └── Icons/                       # Component icons (PNG files)
├── UR.RTDE.Grasshopper.csproj       # Main project file
├── UR.RTDE.Grasshopper.sln          # Solution file
└── UR.RTDE.GrasshopperInfo.cs      # Assembly metadata

Tests:
├── UR.RTDE.Grasshopper.Tests/      # Unit tests
│   ├── URSessionTests.cs
│   └── PoseUtilsTests.cs
```

## Key Components

### 1. UR_SessionComponent
**Purpose**: Manages RTDE connection to Universal Robot  
**Location**: `Components/UR_SessionComponent.cs`  
**Key Features**:
- Handles connection/disconnection
- Custom UI with connect/disconnect button (`UR_SessionAttributes`)
- Visual connection indicator in viewport
- Outputs session handle for other components

**Key Fields**:
- `_session`: `URSession` instance (internal)
- `_currentIp`: Current robot IP (internal)
- `_lastTimeoutMs`: Connection timeout (internal)

### 2. UR_SessionAttributes
**Purpose**: Custom UI rendering for session component  
**Location**: `Components/UR_SessionAttributes.cs`  
**Features**:
- Renders connect/disconnect button
- Handles mouse interactions
- Visual feedback (hover states, colors)
- Button automatically toggles connection state

### 3. UR_ReadComponent (Event-Driven)
**Purpose**: Reads robot state (joints, pose, IO, modes)  
**Location**: `Components/UR_ReadComponent.cs`  
**Architecture**: `GH_Component` with `System.Threading.Timer` for background polling  
**Key Features**:
- **Event-driven**: Uses timer-based polling instead of blocking calls
- **Non-blocking**: UI remains responsive during read operations
- Context menu: Joints, Pose, IO, Modes, Targets, Dynamics, Status, FK (compute), IK (compute)
- Auto-listen feature for periodic updates (event-driven with timer)
- Configurable interval presets (20, 50, 100, 200, 500, 1000 ms)
- Cached data pattern: Timer polls in background, component outputs cached results
- Thread-safe data caching with lock

**Key Fields**:
- `_kind`: `URReadKind` enum (Joints, Pose, IO, Modes)
- `_autoListen`: Boolean flag for auto-listen
- `_autoIntervalMs`: Interval for auto-listen
- `_readTimer`: `System.Threading.Timer` for background polling
- `_lastReadData`: Cached read results
- `_lock`: Thread synchronization object

**Threading**:
- UI thread: Input collection, output cached data
- Timer thread: Polls RTDE in background, caches results
- Pattern similar to MQTT Subscribe: event-driven with cached data

### 4. UR_StreamComponent
**Purpose**: Continuous live control via SpeedJ/ServoJ  
**Location**: `Components/UR_StreamComponent.cs`  
**Ribbon**: `UR` / `RTDE`, `GH_Exposure.tertiary` (same sub-panel as **UR Write**)  
**Icon**: `broadcast-duotone.png` (Phosphor *broadcast*, duotone, `#00A3E0` — same cyan as `rocket-launch-duotone.png`)  
**Key Features**:
- SpeedJ (`qd[6]`) or ServoJ (`q[6]`) modes via on-component dropdown
- **Arm stream** button (not persisted on save/load)
- Min interval rate limiting (20, 50, 100, 200, 500 ms; default 50)
- Outputs: `OK`, `Message`, `Armed`
- SpeedStop/ServoStop on disconnect, disarm, or component removal

**Key Fields**:
- `_kind`: `URStreamKind` (SpeedJ, ServoJ)
- `_armed`: stream armed flag (reset on Read)
- `_minIntervalMs`: duplicate-send rate limit
- `_lastSignature` / `_lastSendUtc`: skip unchanged values within interval

### 5. UR_WriteComponent
**Purpose**: Sends discrete commands to robot  
**Location**: `Components/UR_WriteComponent.cs`  
**Ribbon**: `UR` / `RTDE`, `GH_Exposure.tertiary`  
**Icon**: `rocket-launch-duotone.png` (Phosphor *rocket-launch*, duotone, `#00A3E0`)  
**Architecture**: `GH_Component` with direct command execution  
**Key Features**:
- **Simple execution**: Direct method calls, no worker pattern
- Motion uses an on-component **Run** button instead of an `Execute` input
- Context menu can arm **Auto Send** for MoveJ/MoveL so new targets send automatically
- `Auto Send` is intentionally **not persisted** in component serialization for safety
- Dropdown/button UI selects command type (MoveJ, MoveL, Stop, SetDO)
- Dynamic input/output based on selected action
- Concurrency check to prevent overlapping commands

**Key Fields**:
- `_action`: `URActionKind` enum (MoveJ, MoveL, Stop, SetDO)
- `_isRunning`: Flag to prevent overlapping motion runs
- `_runRequested`: One-shot run request from the component button
- `_autoSend`: Non-persisted auto-send flag for motion targets
- `_log`: Command history log

**Pattern**:
- User clicks `Run` for MoveJ/MoveL, or enables `Auto Send` and changes the target input
- Stop and SetDO execute directly during solve
- Move sequences run in the background and update `Running`, `CurrentIndex`, `Total`, and `Done`

### 6. UR_GripperComponent
**Purpose**: Control Robotiq grippers via URCap (native, RTDE bridge, or URScript backends)  
**Location**: `Components/UR_GripperComponent.cs`  
**Key Features**:
- Menu-selectable backends: Native (port 63352), RTDE bridge (auto-install), URScript (port 30002)
- Actions: Activate, Open, Close, Move (position/speed/force 0-255)
- Dynamic inputs based on action/backend (wait-for-motion, install bridge, timeout, port)

### 7. URSession
**Purpose**: Wrapper around UR.RTDE library  
**Location**: `Runtime/URSession.cs`  
**Key Methods**:
- `Connect(timeoutMs)`: Establish RTDE connection
- `Dispose()`: Close connection and cleanup
- `IsConnected`: Property indicating connection status
- Various read/command methods
- Robotiq helpers: `RobotiqActivate/Open/Close/Move` with backend selection and RTDE bridge install

### 8. URSessionGoo
**Purpose**: Grasshopper data type for session  
**Location**: `Types/URSessionGoo.cs`  
**Inherits**: `GH_Goo<URSession>`  
**Purpose**: Wraps `URSession` to work with Grasshopper's type system

### 9. PoseUtils
**Purpose**: Utility functions for pose conversions  
**Location**: `Utils/PoseUtils.cs`  
**Key Methods**:
- Conversions between pose arrays and Rhino Planes
- Coordinate system transformations

## Dependencies

### NuGet Packages
- **UR.RTDE** (Version 1.6.3.10): Main dependency for RTDE communication and Robotiq gripper support
  - Provides native C++ P/Invoke wrapper
  - Includes native DLLs (rtde.dll, ur_rtde_c_api.dll, boost_thread)
  - Robotiq drivers: `RobotiqGripperNative`, `RobotiqGripperRtde`, `RobotiqGripper` (URScript)

### Native Dependencies
- `rtde.dll`: RTDE protocol implementation
- `ur_rtde_c_api.dll`: C API wrapper
- `boost_thread-vc143-mt-x64-1_89.dll`: Boost threading library

### Grasshopper API
- `Grasshopper.Kernel`: Core Grasshopper API
- `Grasshopper.Kernel.Attributes`: UI attributes
- `Rhino.Geometry`: Geometry types
- `Rhino.Display`: Display/viewport functionality

## Component Architecture

All Grasshopper components inherit from `GH_Component` (not `GH_AsyncComponent`). There is no `GrasshopperAsyncComponent` package dependency.

### UR Read threading
- **UI thread**: `SolveInstance` outputs cached data
- **Timer thread**: `System.Threading.Timer` polls RTDE and updates cache under a lock
- Auto-listen uses the same timer pattern (20–1000 ms presets)

### UR Write execution
- MoveJ/MoveL: explicit **Run** button or optional **Auto Send** (not persisted)
- Stop/SetDO: run during solve
- Long motion may continue on a background thread; UI uses `Running` / `Done` outputs

## Build System

### Project File
- **Main Project**: `UR.RTDE.Grasshopper.csproj`
- **Target Frameworks**: `net8.0-windows`, `net8.0`, `net48`
- **Output Type**: `.gha` (Grasshopper Addon)

### Build Targets
- **Debug**: `bin/Debug/<TargetFramework>/UR.RTDE.Grasshopper.gha`
- **Release**: `bin/Release/<TargetFramework>/UR.RTDE.Grasshopper.gha`

### Build Commands
```bash
# Build all targets
dotnet build -c Release

# Build specific target
dotnet build -c Release -f net8.0-windows

# Build with yak packaging (automatic when yak is available)
dotnet build -c Release -f net8.0-windows

# macOS Rhino 8 debug-focused build
dotnet build UR.RTDE.Grasshopper.csproj -c Debug -f net8.0 /p:BuildYakPackage=false

# macOS Rhino 8 — build and copy to Grasshopper Libraries (Release)
dotnet build -c Release -f net8.0
# Debug copy: add /p:CopyToGrasshopperLibrariesOnDebug=true
```

### Custom Build Targets
- `CopyYakIcon`: Automatically copies icon to build output
- `BuildYakPackage`: Optional MSBuild yak step (flat net48 folder only). **Releases use `tools/package-yak.sh` instead** — see **Yak Package**.
- `CopyURRTDEDependencies`: Copies native DLLs to output (flat beside `.gha` + `runtimes/**`)
- `CopyToGrasshopperLibraries`: Auto-deploys to Grasshopper Libraries folder

### Cross-platform native loading (Windows + macOS)

Rhino 8 on Windows runs Grasshopper plugins on **.NET 8 (CoreCLR)**. A **net48-only** Yak package does not include the `NativeLibrary` / `DllImportResolver` path compiled for `net8.0-windows`, so RTDE P/Invoke can fail with `BaseDir: C:\Program Files\Rhino 8\System\netcore` even when natives exist under `runtimes/win-x64/native/`.

**Bootstrap chain (do not remove):**
1. `UR_PluginPriority` (`GH_AssemblyPriority.PriorityLoad`) — runs before any component loads.
2. `URSession.EnsurePluginInitialized()` — `AssemblyResolve` for `UR.RTDE.dll` from the plugin folder, then `NativeBootstrap.EnsureLoaded()`.
3. `NativeBootstrap` — macOS: `MacOsNativeLibraryBootstrap` (UR.RTDE); Windows: `LoadLibrary` + dependency preload + `AddDllDirectory`, and on **net5+ builds** `NativeLibrary.Load` + `SetDllImportResolver` for `ur_rtde_c_api`.

**Native files required beside each `.gha` (and under `runtimes/`):**
- Windows: `ur_rtde_c_api.dll`, `rtde.dll`, `boost_thread-vc143-mt-x64-1_89.dll` (and/or vc145)
- macOS: `libur_rtde_c_api.dylib` under `runtimes/osx-arm64/native/` and `runtimes/osx-x64/native/` (no flat macOS copy beside `.gha`)

**Windows troubleshooting:** If load still fails after a correct Yak install, install the [VC++ 2015–2022 x64 redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

## Testing

### Test Project
- **Location**: `UR.RTDE.Grasshopper.Tests/`
- **Framework**: NUnit
- **Test Files**:
  - `URSessionTests.cs`: Session and RTDE API tests (some need URSim)
  - `PoseUtilsTests.cs`: Pose conversion tests (Rhino.Testing)

### Running Tests
```bash
# Unit tests only (no live robot)
dotnet test UR.RTDE.Grasshopper.Tests/UR.RTDE.Grasshopper.Tests.csproj --filter "Category!=Integration"

# All tests including URSim at 127.0.0.1
pwsh UR.RTDE.Grasshopper.Tests/run-tests.ps1
```

Integration tests are tagged `[Category("Integration")]` and require URSim (or a robot) at `127.0.0.1`. CI runs non-Integration tests only.

### URSim via Docker (manual setup)

No Docker launcher component in this repo. Start URSim before **UR Session** using the sibling **UR.RTDE** compose file (ports match [Multi-Actor-Interface-Library/docker/ursim](https://github.com/lasaths/Multi-Actor-Interface-Library/tree/main/docker/ursim)):

```bash
cd /path/to/UR.RTDE
docker compose -f docker/ursim/docker-compose.yml up -d
```

See `UR.RTDE` → [docker/ursim/README.md](../UR.RTDE/docker/ursim/README.md) and this repo `README.md` → **Testing with URSim**.

- IP: **`127.0.0.1`** (not `192.168.56.1` on macOS)
- **`30004`**: RTDE receive; **`30003`**: RTDE control (required for session connect / MoveJ)
- Verify: `nc -zv 127.0.0.1 30003` and `nc -zv 127.0.0.1 30004`
- PolyScope: [http://127.0.0.1:6080/vnc.html?host=localhost&port=6080](http://127.0.0.1:6080/vnc.html?host=localhost&port=6080) — power on, release brakes, wait **1–2 min** before **Connect**
- `URSession.CreateControlWithFallback()` tries multiple control flags; `LastError` reports if port 30003 is unreachable

### Starting URSim from Grasshopper (not implemented)

| In scope | Out of scope |
|----------|----------------|
| Future: `docker compose up` via background process, TCP readiness on 30003/30004 | Installing Docker Desktop |
| Reuse `URSession` port-check patterns | Linux `--network host` on Mac/Windows |
| Non-blocking (same as UR Read timer pattern) | Guaranteeing URControl on Apple Silicon |

### Manual component testing
1. **UR Read auto-listen**: intervals 20ms–1000ms, canvas stays responsive
2. **UR Write**: Run button, Auto Send (not saved), Stop during motion
3. **Multiple instances**: concurrent read/write components
4. **Persistence**: save/load `.gh` with configured read kind and intervals

## Code Patterns

### Component Creation Pattern
```csharp
public class MyComponent : GH_Component
{
    public MyComponent()
      : base("Name", "Nickname",
            "Description",
            "Category", "Subcategory")
    {
    }
    
    protected override void RegisterInputParams(GH_InputParamManager p) { }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    protected override void SolveInstance(IGH_DataAccess da) { }
}
```

### Context Menu Pattern
```csharp
protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
{
    Menu_AppendItem(menu, "Option", Menu_OptionClick);
}

private void Menu_OptionClick(object sender, EventArgs e)
{
    // Handle menu click
    ExpireSolution(true);
}
```

### Session Access Pattern
```csharp
URSessionGoo goo = null;
if (!da.GetData(0, ref goo)) return;

var session = goo?.Value;
if (session == null || !session.IsConnected)
{
    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
    return;
}
```

## Common Tasks

### Adding a New Component
1. Create new class in `Components/` directory
2. Inherit from `GH_Component`
3. Register inputs/outputs in constructor
4. Implement `SolveInstance`
5. Add icon in `Resources/Icons/`
6. Build and test

### Modifying Component UI
1. Create custom `GH_ComponentAttributes` class
2. Override `Layout()` for bounds calculation
3. Override `Render()` for custom drawing
4. Handle mouse events for interactivity
5. Assign in component constructor

### Updating Dependencies
1. Update version in `UR.RTDE.Grasshopper.csproj`
2. Restore packages: `dotnet restore`
3. Rebuild: `dotnet build`
4. Test all target frameworks

## Version Management

### Current Version
- **Version**: 1.6.3.6 (in `.csproj`)
- **Yak Package**: Available on `yak.rhino3d.com`

### Version Bump Process
1. Update `<Version>` in `.csproj` and `CHANGELOG.md`
2. `dotnet build UR.RTDE.Grasshopper.csproj -c Release -p:BuildYakPackage=false`
3. `bash tools/package-yak.sh` (multi-target Yak; verifies native files)
4. Test on **Windows and Mac** (Package Manager or manual copy of full output folder)
5. `yak push` test server, then production (`rh8_0-any` + `rh7_0-any`)
6. Git tag: `git tag -a v<version> -m "Release <version>"` and push; create GitHub release with `.yak` artifacts

## Yak Package

### Package Name
- **Yak Name**: `UR-RTDE-Grasshopper` (dashes, no dots)
- **Package ID**: `UR.RTDE.Grasshopper` (with dots, for internal use)

### Multi-target layout (required for Rhino 8 Win + Mac)

Use **`tools/package-yak.sh`** as the only release packaging path (`-p:BuildYakPackage=false` on `dotnet build`). Do not ship a single flat net48 folder for Rhino 8 — Rhino picks the framework subfolder at load time ([McNeel multi-target guide](https://developer.rhino3d.com/en/guides/yak/creating-a-multi-targeted-rhino-plugin-package)).

```
ur-rtde-grasshopper-<version>-rh8_0-any.yak
├── manifest.yml
├── icon.png
├── net48/              # Rhino 7
│   ├── UR.RTDE.Grasshopper.gha
│   ├── UR.RTDE.dll
│   ├── ur_rtde_c_api.dll, rtde.dll, boost_thread-*.dll
│   ├── libur_rtde_c_api.dylib
│   └── runtimes/...
├── net8.0-windows/     # Rhino 8 Windows (CoreCLR + DllImport resolver)
│   └── (same native layout as net48)
└── net8.0/             # Rhino 8 Mac
    └── (same native layout; macOS uses dylib)
```

`package-yak.sh` copies flat natives + `runtimes/` into each framework folder and **fails the build** if required entries are missing from the `.yak` zip.

### Manifest
- Written by `package-yak.sh` into `bin/Release/yak-staging/manifest.yml` (`name: UR-RTDE-Grasshopper`, `guid:` in keywords)
- Package name: letters, numbers, dashes, underscores only (no dots)

### Publishing
```bash
dotnet build UR.RTDE.Grasshopper.csproj -c Release -p:BuildYakPackage=false
bash tools/package-yak.sh

# Test server first
yak push --source https://test.yak.rhino3d.com \
  bin/Release/net48/ur-rtde-grasshopper-<version>-rh8_0-any.yak
yak push --source https://test.yak.rhino3d.com \
  bin/Release/net48/ur-rtde-grasshopper-<version>-rh7_0-any.yak

# Production (after Win + Mac smoke test)
yak push bin/Release/net48/ur-rtde-grasshopper-<version>-rh8_0-any.yak
yak push bin/Release/net48/ur-rtde-grasshopper-<version>-rh7_0-any.yak
```

`rh7_0-any` is produced by renaming the `rh8_0-any` artifact (same net48 payload) — see script output.

**Yak CLI paths:** Windows `C:\Program Files\Rhino 8\System\Yak.exe`; macOS `/Applications/Rhino 8.app/Contents/Resources/bin/yak` or `tools/yak` from `tools/install-yak.sh`.

## Important Notes

### Safety
- ⚠️ **Always test with URSim first** (see **URSim via Docker** under Testing)
- ⚠️ Robot commands can cause injury
- ⚠️ No warranties or liability assumed

### URSim / Docker
- Use `UR.RTDE` → `docker compose -f docker/ursim/docker-compose.yml up -d`
- Plugin does not start Docker; session connect needs **30003** + **30004** on the host
- Mac ARM: allow 1–2 min PolyScope boot; `nc` on 30003 succeeding does not mean `RTDEControl` is ready yet

### Platform Support
- **net48**: Rhino 7 only
- **net8.0**: Rhino 8 (cross-platform, warnings expected for GDI+)
- **net8.0-windows**: Rhino 8 (Windows, recommended)

### Build Warnings
- CA1416 warnings are expected for GDI+ usage on non-Windows targets
- These are safe to ignore for cross-platform builds
- Windows-specific UI code is expected
- net48 uses C# latest; ensure `LangVersion` remains aligned if adding pattern matching/using decls

### Read component performance
- Timer-based polling keeps the Grasshopper canvas responsive during auto-listen
- Auto-listen supports 20–50 ms intervals when the network and robot keep up

### Icons
- Icons are embedded resources from `Resources/Icons/`
- Used icons: binoculars, plugs, plugs-connected, hand-grabbing (gripper), rocket-launch (write), broadcast (stream), robot-duotone
- Command/stream duotone color: `#00A3E0` (regenerate with phosphor-icons CLI: `node dist/cli.js icon <name> --weight duotone --color "#00A3E0" --size 24 --format png --out <file>`)
- Icons must be 24x24 PNG files (all component icons)
- Icon auto-copy happens during yak build

## File Conventions

### Naming
- Components: `UR_<Name>Component.cs`
- Types: `UR<Name>Goo.cs` or `UR<Name>Param.cs`
- Utilities: `PoseUtils.cs`
- Icons: `<name>-duotone.png`

### Code Style
- Public components and types
- Internal fields for component state
- XML documentation for public APIs
- Comments removed in favor of clear code

## Testing Checklist

When making changes:
1. Build all target frameworks
2. Test in Rhino 7 (net48)
3. Test in Rhino 8 (net8.0-windows)
4. Verify yak package via `bash tools/package-yak.sh` (multi-target + native verify)
5. Test Yak install on **Windows and Mac** Rhino 8 before production push
6. Test with URSim (never skip; `docker/ursim` in UR.RTDE repo, both ports 30003+30004)
7. Check all component icons display
8. Verify context menus work
9. Test auto-listen at several intervals
10. Verify UI responsiveness during auto-listen and motion
11. Verify all read/command modes work
12. Verify Robotiq gripper actions on URCap: activate/open/close/move across Native (63352), RTDE bridge (with install), URScript (30002)

## Resources

- **NuGet Package**: https://www.nuget.org/packages/UR.RTDE/
- **C++ Library Docs**: https://sdurobotics.gitlab.io/ur_rtde/
- **Yak Package**: https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper
- **GitHub**: https://github.com/lasaths/UR.RTDE.Grasshopper
