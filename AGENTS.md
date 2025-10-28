UR.RTDE.Grasshopper — Agent Notes
=================================

Purpose
-------
This repository contains a minimal Grasshopper (Rhino) plugin scaffold that integrates the UR.RTDE C# wrapper to control Universal Robots via RTDE.

Key Links
---------
- NuGet package: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/#readme-body-tab)
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

Install in Grasshopper
----------------------
- Copy the built `.gha` to your Grasshopper Libraries folder:
  - Windows: `%AppData%\Grasshopper\Libraries`
  - Or package with Yak (Rhino 8): build triggers are set in the csproj to help create a Yak spec in the output directory.

UR.RTDE Package Usage
---------------------
- Add the runtime/control wrapper via NuGet to this project or your solution:
  - `dotnet add package UR.RTDE`
  - Or add `<PackageReference Include="UR.RTDE" Version="1.0.0" />` to the project file.
- The package includes native dependencies and requires no external Python.

Quick Component Sketch
----------------------
- The provided `RTDE_GrasshopperComponent` is a stub. Typical usage pattern inside `SolveInstance`:
  1. Read robot IP and optional motion parameters from inputs.
  2. Create `RTDEControl`/`RTDEReceive` instances from `UR.RTDE`.
  3. Perform motion/read operations.
  4. Dispose deterministically (`using`/`IDisposable`).

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

