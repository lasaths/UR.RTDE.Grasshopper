# AGENTS.md

This document provides information for AI agents and automated systems working with the UR.RTDE.Grasshopper codebase.

## Project Overview

**Name**: UR.RTDE.Grasshopper  
**Version**: 0.1.2  
**Type**: Grasshopper plugin for Rhino  
**Purpose**: Control Universal Robots via RTDE (Real-Time Data Exchange) protocol from Grasshopper  
**Language**: C# (.NET)  
**Framework Targets**: net48 (Rhino 7), net7.0, net7.0-windows (Rhino 8)

## Project Structure

```
UR.RTDE.Grasshopper/
├── Components/           # Grasshopper components
│   ├── UR_SessionComponent.cs       # Session management component
│   ├── UR_SessionAttributes.cs      # Custom UI attributes for session component
│   ├── UR_ReadComponent.cs          # Read robot state component
│   └── UR_CommandComponent.cs       # Send commands component
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

### 3. UR_ReadComponent
**Purpose**: Reads robot state (joints, pose, IO, modes)  
**Location**: `Components/UR_ReadComponent.cs`  
**Key Features**:
- Context menu to select read type (Joints, Pose, IO, Modes)
- Auto-listen feature for periodic updates
- Configurable interval presets (20, 50, 100, 200, 500, 1000 ms)

**Key Fields**:
- `_kind`: `URReadKind` enum (Joints, Pose, IO, Modes)
- `_autoListen`: Boolean flag for auto-listen
- `_autoIntervalMs`: Interval for auto-listen

### 4. UR_CommandComponent
**Purpose**: Sends commands to robot  
**Location**: `Components/UR_CommandComponent.cs`  
**Key Features**:
- Context menu to select command type (MoveJ, MoveL, StopJ, StopL, SetDO)
- Dynamic input/output based on selected action
- Async execution support

**Key Fields**:
- `_action`: `URActionKind` enum (MoveJ, MoveL, StopJ, StopL, SetDO)

### 5. URSession
**Purpose**: Wrapper around UR.RTDE library  
**Location**: `Runtime/URSession.cs`  
**Key Methods**:
- `Connect(timeoutMs)`: Establish RTDE connection
- `Dispose()`: Close connection and cleanup
- `IsConnected`: Property indicating connection status
- Various read/command methods

### 6. URSessionGoo
**Purpose**: Grasshopper data type for session  
**Location**: `Types/URSessionGoo.cs`  
**Inherits**: `GH_Goo<URSession>`  
**Purpose**: Wraps `URSession` to work with Grasshopper's type system

### 7. PoseUtils
**Purpose**: Utility functions for pose conversions  
**Location**: `Utils/PoseUtils.cs`  
**Key Methods**:
- Conversions between pose arrays and Rhino Planes
- Coordinate system transformations

## Dependencies

### NuGet Packages
- **UR.RTDE** (Version 1.0.0): Main dependency for RTDE communication
  - Provides native C++ P/Invoke wrapper
  - Includes native DLLs (rtde.dll, ur_rtde_c_api.dll, boost_thread)

### Native Dependencies
- `rtde.dll`: RTDE protocol implementation
- `ur_rtde_c_api.dll`: C API wrapper
- `boost_thread-vc143-mt-x64-1_89.dll`: Boost threading library

### Grasshopper API
- `Grasshopper.Kernel`: Core Grasshopper API
- `Grasshopper.Kernel.Attributes`: UI attributes
- `Rhino.Geometry`: Geometry types
- `Rhino.Display`: Display/viewport functionality

## Build System

### Project File
- **Main Project**: `UR.RTDE.Grasshopper.csproj`
- **Target Frameworks**: `net7.0-windows`, `net7.0`, `net48`
- **Output Type**: `.gha` (Grasshopper Addon)

### Build Targets
- **Debug**: `bin/Debug/<TargetFramework>/UR.RTDE.Grasshopper.gha`
- **Release**: `bin/Release/<TargetFramework>/UR.RTDE.Grasshopper.gha`

### Build Commands
```bash
# Build all targets
dotnet build -c Release

# Build specific target
dotnet build -c Release -f net7.0-windows

# Build with yak packaging (automatic when yak is available)
dotnet build -c Release -f net7.0-windows
```

### Custom Build Targets
- `CopyYakIcon`: Automatically copies icon to build output
- `BuildYakPackage`: Generates yak package after build
- `CopyURRTDEDependencies`: Copies native DLLs to output

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
4. Implement `SolveInstance` method
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
- **Generated**: `bin/Release/net7.0-windows/manifest.yml`
- **Auto-generated**: Yes, by `yak spec` command
- **Manual Updates**: Keywords and icon need to be added manually

### Manifest Requirements
- Package name: Only letters, numbers, dashes, underscores (no dots)
- Keywords: Only letters, numbers, dashes, underscores (no spaces)
- Icon: Must be in build output directory

### Publishing
```bash
# Build and package
dotnet build -c Release -f net7.0-windows

# Generate manifest
cd bin/Release/net7.0-windows
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
- **net7.0**: Rhino 8 (cross-platform, warnings expected for GDI+)
- **net7.0-windows**: Rhino 8 (Windows, recommended)

### Build Warnings
- CA1416 warnings are expected for GDI+ usage on non-Windows targets
- These are safe to ignore for cross-platform builds
- Windows-specific UI code is expected

### Icons
- Icons are embedded resources from `Resources/Icons/`
- Used icons: binoculars, plugs, plugs-connected, rocket-launch, robot-duotone
- Icons must be PNG format
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
3. Test in Rhino 8 (net7.0-windows)
4. Verify yak package builds correctly
5. Test installation via yak
6. Test with URSim (never skip this)
7. Check all component icons display
8. Verify context menus work
9. Test auto-listen functionality
10. Verify all read/command modes work

## Resources

- **NuGet Package**: https://www.nuget.org/packages/UR.RTDE/
- **C++ Library Docs**: https://sdurobotics.gitlab.io/ur_rtde/
- **Yak Package**: https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper
- **GitHub**: https://github.com/lasaths/UR.RTDE.Grasshopper
