UR.RTDE.Grasshopper — Agent Notes
=================================

Purpose
-------

This repository contains a minimal Grasshopper (Rhino) plugin scaffold that integrates the UR.RTDE C# wrapper to control Universal Robots via RTDE.

Key Links
---------

- NuGet package: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/)
- ur_rtde C++ library docs: [SDU Robotics ur_rtde](https://sdurobotics.gitlab.io/ur_rtde/)

Targets and Compatibility
-------------------------

- Project targets: `net48`, `net7.0`, `net7.0-windows` (see `UR.RTDE.Grasshopper.csproj`).
- Rhino 7: `net48`. Rhino 8: `net7.0`/`net7.0-windows`.

Local Build
-----------

1. Open `UR.RTDE.Grasshopper.sln` in Visual Studio 2022.
2. Select desired target framework configuration.
3. Build. Output `.gha` will be placed under `bin/Debug/<tfm>` or `bin/Release/<tfm>`.
4. Yak packaging runs by default when Yak is available.

   - Default Yak paths: Windows `C:\\Program Files\\Rhino 8\\System\\Yak.exe`, macOS `/Applications/Rhino 8.app/Contents/Resources/bin/yak`.
   - If Yak is elsewhere, pass `-p:YakExecutable="<path-to-yak>"` at build.
   - To disable Yak packaging for a build: `-p:BuildYakPackage=false`.

Install in Grasshopper
----------------------

- Copy the built `.gha` to your Grasshopper Libraries folder:

  - Windows: `%AppData%\Grasshopper\Libraries`
  - Or package with Yak (Rhino 8): build triggers are set in the csproj to help create a Yak spec in the output directory.
  - Yak packages are generated in the output directory when Yak is available.

UR.RTDE Package Usage
---------------------

- Add the runtime/control wrapper via NuGet to this project or your solution:

  - `dotnet add package UR.RTDE`
  - Or add `<PackageReference Include="UR.RTDE" Version="1.0.0" />` to the project file.

- The package includes native dependencies and requires no external Python.
- This repo intentionally avoids local project references; only the NuGet package is used.

Architecture
------------

- **Components/**: Three main GH components

  - `UR_SessionComponent.cs`: Creates/manages RTDE session with Connect/Disconnect button
  - `UR_ReadComponent.cs`: Reads Joints, Pose, IO, or Modes via context menu
  - `UR_CommandComponent.cs`: Sends MoveJ/MoveL/StopJ/StopL/SetDO commands

- **Runtime/URSession.cs**: Session wrapper using reflection for API compatibility
- **Types/**: GH type wrapper (`URSessionGoo`) and parameter (`URSessionParam`)
- **Utils/PoseUtils.cs**: Conversions between Rhino Planes and UR poses

Code Review Notes
----------------

- All verbose comments removed; code is self-documenting
- Platform-specific warnings for Bitmap are expected (Windows-only component)
- No LLM-generated comments remain in codebase
- Test files (`TestURRTDE/`) are temporary and not committed
- Dynamic input rebuilding in `UR_CommandComponent` adjusts parameters per action
- Session lifecycle: connected on button click, persists until disposal
- Read component supports 4 modes: Joints (6 values), Pose (6 DOF), IO (tree), Modes (status)

Quick Component Sketch
----------------------

- Minimal component set:

  - UR Session: manage connection and expose a `session` handle.
  - UR Read: dropdown selects Joints, Pose, IO, Modes.
  - UR Command: dropdown selects MoveJ, MoveL, StopJ, StopL, SetDO.
  - Use a GH Timer for polling reads; avoid background threads.

Test Against URSim First
------------------------

- Validate flows in URSim (e‑Series ≥ 5.23.0 recommended) before real hardware.
- Refer to ur_rtde examples and timing considerations in the official docs.

Conventions for Agents
----------------------

- Keep the repo minimal; avoid extra files unless a refactor clearly adds value.
- Prefer small, focused components; explicit names and guard clauses.
- Do not introduce runtime prompts or interactive steps in build.
- If you need UR connectivity smoke-tests, write quick, disposable code in the terminal or a temporary test harness—avoid committing files.
- Remove verbose/explanatory comments; code should be self-documenting.

Icons
-----

- Use Phosphor Icons (<https://phosphoricons.com>), MIT License.
- To override the generated fallback icons, add 24x24 PNGs as Embedded Resources under `Resources/Icons/` with names:

  - `plug-duotone.png` (UR Session)
  - `eye-duotone.png` (UR Read)
  - `play-duotone.png` (UR Command)