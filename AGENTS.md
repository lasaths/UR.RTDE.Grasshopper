# AGENTS.md

This document provides information for AI agents and automated systems working with the UR.RTDE.Grasshopper codebase.

## Project Overview

**Name**: UR.RTDE.Grasshopper  
**Version**: 0.1.2  
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
│   ├── UR_ReadComponent.cs          # Read robot state component (ASYNC)
│   ├── UR_CommandComponent.cs       # Send commands component (ASYNC)
│   └── UR_GripperComponent.cs       # Robotiq gripper control component
├── Types/               # Custom Grasshopper types
│   ├── URSessionGoo.cs              # Grasshopper data type wrapper for URSession
│   └── URSessionParam.cs            # Custom parameter for session inputs
├── Runtime/             # Core runtime functionality
│   └── URSession.cs                # Wrapper around UR.RTDE library
├── Utils/               # Utility functions
│   └── PoseUtils.cs                 # Pose conversion utilities
├── Resources/
│   └── Icons/                       # Component icons (PNG files)
├── UR.RTDE.Grasshopper.csproj       # Main project file
├── UR.RTDE.Grasshopper.sln          # Solution file
└── UR.RTDE.GrasshopperInfo.cs      # Assembly metadata

Tests:
├── UR.RTDE.Grasshopper.Tests/      # Unit tests
│   ├── SimpleTests.cs
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
- Context menu to select read type (Joints, Pose, IO, Modes)
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

### 4. UR_CommandComponent (Simplified)
**Purpose**: Sends commands to robot  
**Location**: `Components/UR_CommandComponent.cs`  
**Architecture**: `GH_Component` with direct command execution  
**Key Features**:
- **Simple execution**: Direct method calls, no worker pattern
- **Async option**: Fire-and-forget with `Task.Run` for async moves
- **Synchronous by default**: Blocking calls for immediate feedback
- Context menu to select command type (MoveJ, MoveL, StopJ, StopL, SetDO)
- Dynamic input/output based on selected action
- Concurrency check to prevent overlapping commands

**Key Fields**:
- `_action`: `URActionKind` enum (MoveJ, MoveL, StopJ, StopL, SetDO)
- `_isExecuting`: Flag to prevent concurrent execution
- `_log`: Command history log

**Pattern**:
- User triggers component → SolveInstance → Execute command → Return result
- For async moves: Fire `Task.Run`, return immediately
- For sync moves: Block until complete, return result

### 5. UR_GripperComponent
**Purpose**: Control Robotiq grippers via URCap (native, RTDE bridge, or URScript backends)  
**Location**: `Components/UR_GripperComponent.cs`  
**Key Features**:
- Menu-selectable backends: Native (port 63352), RTDE bridge (auto-install), URScript (port 30002)
- Actions: Activate, Open, Close, Move (position/speed/force 0-255)
- Dynamic inputs based on action/backend (wait-for-motion, install bridge, timeout, port)

### 6. URSession
**Purpose**: Wrapper around UR.RTDE library  
**Location**: `Runtime/URSession.cs`  
**Key Methods**:
- `Connect(timeoutMs)`: Establish RTDE connection
- `Dispose()`: Close connection and cleanup
- `IsConnected`: Property indicating connection status
- Various read/command methods
- Robotiq helpers: `RobotiqActivate/Open/Close/Move` with backend selection and RTDE bridge install

### 7. URSessionGoo
**Purpose**: Grasshopper data type for session  
**Location**: `Types/URSessionGoo.cs`  
**Inherits**: `GH_Goo<URSession>`  
**Purpose**: Wraps `URSession` to work with Grasshopper's type system

### 8. PoseUtils
**Purpose**: Utility functions for pose conversions  
**Location**: `Utils/PoseUtils.cs`  
**Key Methods**:
- Conversions between pose arrays and Rhino Planes
- Coordinate system transformations

## Dependencies

### NuGet Packages
- **UR.RTDE** (Version 1.2.0): Main dependency for RTDE communication and Robotiq gripper support
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

## Async Components Architecture

### Pattern (GrasshopperAsyncComponent 2.0.3)

```csharp
public class MyComponent : GH_AsyncComponent<MyComponent>
{
    public MyComponent() : base(...)
    {
        BaseWorker = new MyWorker(this);
    }
    
    private class MyWorker : WorkerInstance<MyComponent>
    {
        public MyWorker(MyComponent parent, string id = "worker", 
                       CancellationToken cancellationToken = default)
            : base(parent, id, cancellationToken) { }
            
        public override WorkerInstance<MyComponent> Duplicate(
            string id, CancellationToken cancellationToken)
            => new MyWorker(Parent, id, cancellationToken);
            
        public override void GetData(IGH_DataAccess da, 
                                    GH_ComponentParamServer ghParams)
        {
            // Collect input data (UI thread)
        }
        
        public override Task DoWork(Action<string, double> reportProgress, 
                                   Action done)
        {
            try
            {
                CancellationToken.ThrowIfCancellationRequested();
                // Perform work (background thread)
                reportProgress(Id, progress);
                done();
            }
            catch (OperationCanceledException) when 
                  (CancellationToken.IsCancellationRequested)
            {
                // Handle cancellation
            }
            return Task.CompletedTask;
        }
        
        public override void SetData(IGH_DataAccess da)
        {
            // Output results (UI thread)
        }
    }
}
```

### Benefits
- **UI Responsiveness**: 10x improvement, no freezing
- **Auto-listen**: Can handle 20-50ms intervals without blocking
- **Cancellation**: User can stop operations mid-execution
- **Progress reporting**: Visual feedback during operations
- **Better UX**: Smoother, more professional experience

### Threading Model
```
┌─────────────────────────────────────┐
│         MAIN UI THREAD              │
│  ┌─────────────────────────────┐    │
│  │ Grasshopper                 │    │
│  │  ├─ Canvas (responsive!)    │    │
│  │  ├─ Components              │    │
│  │  └─ Async Components        │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘
              ↓ schedules work
┌─────────────────────────────────────┐
│       WORKER THREAD POOL            │
│  ┌───────────┐  ┌───────────┐       │
│  │ Worker 1  │  │ Worker 2  │       │
│  │ (Read)    │  │ (Command) │       │
│  └───────────┘  └───────────┘       │
└─────────────────────────────────────┘
```

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
```

### Custom Build Targets
- `CopyYakIcon`: Automatically copies icon to build output
- `BuildYakPackage`: Generates yak package after build (uses net48 output)
- `CopyURRTDEDependencies`: Copies native DLLs to output
- `CopyToGrasshopperLibraries`: Auto-deploys to Grasshopper Libraries folder

## Testing

### Test Project
- **Location**: `UR.RTDE.Grasshopper.Tests/`
- **Framework**: NUnit
- **Test Files**:
  - `SimpleTests.cs`: Basic functionality tests
  - `URSessionTests.cs`: Session management tests
  - `PoseUtilsTests.cs`: Pose conversion tests

### Running Tests
```bash
# From test project directory
pwsh run-tests.ps1
```

### Async Components Testing
When testing async components (UR Read, UR Command):
1. **UI Responsiveness**: Verify canvas interaction during operations
2. **Auto-listen**: Test at various intervals (20ms - 1000ms)
3. **Cancellation**: Test right-click → "Cancel" functionality
4. **Multiple instances**: Test concurrent operations
5. **Stress test**: 10+ components with auto-listen
6. **Error handling**: Test disconnect scenarios
7. **Persistence**: Save/load with configured settings

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

### Async Component Creation Pattern
```csharp
public class MyAsyncComponent : GH_AsyncComponent<MyAsyncComponent>
{
    public MyAsyncComponent()
      : base("Name", "Nickname",
            "Description",
            "Category", "Subcategory")
    {
        BaseWorker = new MyWorker(this);
    }
    
    protected override void RegisterInputParams(GH_InputParamManager p) { }
    protected override void RegisterOutputParams(GH_OutputParamManager p) { }
    
    // Add cancellation menu item
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Cancel", (s, e) => RequestCancellation());
    }
    
    private class MyWorker : WorkerInstance<MyAsyncComponent>
    {
        // Implement GetData, DoWork, SetData, Duplicate
    }
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
2. Inherit from `GH_Component` or `GH_AsyncComponent<T>`
3. Register inputs/outputs in constructor
4. Implement `SolveInstance` (sync) or worker pattern (async)
5. Add icon in `Resources/Icons/`
6. Build and test

### Converting Component to Async
1. Change base class to `GH_AsyncComponent<YourComponent>`
2. Add `using GrasshopperAsyncComponent;` and `using System.Threading.Tasks;`
3. Create worker class inheriting `WorkerInstance<YourComponent>`
4. Move `SolveInstance` logic to worker's `DoWork` method
5. Implement `GetData`, `SetData`, and `Duplicate` in worker
6. Add cancellation support with `CancellationToken.ThrowIfCancellationRequested()`
7. Add cancellation menu item

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
- **Version**: 0.1.2 (in `.csproj`)
- **Tag**: `v0.1.2` (in git)
- **Yak Package**: Available on `yak.rhino3d.com`

### Version Bump Process
1. Update `<Version>` in `.csproj`
2. Rebuild release packages
3. Create git tag: `git tag -a v<version> -m "Release <version>"`
4. Push tag: `git push origin v<version>`
5. Update yak manifest and push

## Yak Package

### Package Name
- **Yak Name**: `UR-RTDE-Grasshopper` (dashes, no dots)
- **Package ID**: `UR.RTDE.Grasshopper` (with dots, for internal use)

### Manifest Location
- **Generated**: `bin/Release/net48/manifest.yml` (changed from net8.0-windows)
- **Auto-generated**: Yes, by `yak spec` command
- **Manual Updates**: Keywords and icon need to be added manually

### Manifest Requirements
- Package name: Only letters, numbers, dashes, underscores (no dots)
- Keywords: Only letters, numbers, dashes, underscores (no spaces)
- Icon: Must be in build output directory

### Publishing
```bash
# Build and package
dotnet build -c Release

# Generate manifest (in net48 output)
cd bin/Release/net48
yak spec

# Edit manifest.yml (add keywords, icon)
# Then build yak package
yak build

# Push to test server
yak push --source https://test.yak.rhino3d.com ur-rtde-grasshopper-<version>-rh8_0-any.yak

# Push to production
yak push ur-rtde-grasshopper-<version>-rh8_0-any.yak
```

## Important Notes

### Safety
- ⚠️ **Always test with URSim first**
- ⚠️ Robot commands can cause injury
- ⚠️ No warranties or liability assumed

### Platform Support
- **net48**: Rhino 7 only
- **net8.0**: Rhino 8 (cross-platform, warnings expected for GDI+)
- **net8.0-windows**: Rhino 8 (Windows, recommended)

### Build Warnings
- CA1416 warnings are expected for GDI+ usage on non-Windows targets
- These are safe to ignore for cross-platform builds
- Windows-specific UI code is expected
- net48 uses C# latest; ensure `LangVersion` remains aligned if adding pattern matching/using decls

### Async Components Performance
- UI responsiveness: 10x improvement during operations
- Auto-listen can now handle 20-50ms intervals without freezing
- Small memory overhead: ~2KB per component instance
- Additional dependency: GrasshopperAsyncComponent.dll (21KB)
- Fully backward compatible with existing .gh files

### Icons
- Icons are embedded resources from `Resources/Icons/`
- Used icons: binoculars, plugs, plugs-connected, hand-grabbing (gripper), rocket-launch, robot-duotone
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
4. Verify yak package builds correctly
5. Test installation via yak
6. Test with URSim (never skip this)
7. Check all component icons display
8. Verify context menus work
9. Test auto-listen functionality (especially for async components)
10. Test cancellation (for async components)
11. Verify UI responsiveness during operations
12. Verify all read/command modes work
13. Verify Robotiq gripper actions on URCap: activate/open/close/move across Native (63352), RTDE bridge (with install), URScript (30002)

## Resources

- **NuGet Package**: https://www.nuget.org/packages/UR.RTDE/
- **C++ Library Docs**: https://sdurobotics.gitlab.io/ur_rtde/
- **GrasshopperAsyncComponent**: https://www.nuget.org/packages/GrasshopperAsyncComponent
- **Async Blog Post**: https://v1.speckle.systems/blog/async-gh/
- **Yak Package**: https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper
- **GitHub**: https://github.com/lasaths/UR.RTDE.Grasshopper
