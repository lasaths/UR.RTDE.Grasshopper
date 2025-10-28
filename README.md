UR.RTDE.Grasshopper
===================

A minimal Grasshopper (Rhino) plugin scaffold intended to integrate the `UR.RTDE` C# wrapper for Universal Robots RTDE.

Links
-----
- NuGet package: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/#readme-body-tab)
- ur_rtde C++ library docs: [SDU Robotics ur_rtde](https://sdurobotics.gitlab.io/ur_rtde/)

Status
------
- Targets: `net48`, `net7.0`, `net7.0-windows` (see `UR.RTDE.Grasshopper.csproj`).
- Rhino 7: use `net48`. Rhino 8: use `net7.0` or `net7.0-windows`.

Install the UR.RTDE package
---------------------------
- From NuGet.org:
  - `dotnet add package UR.RTDE`
  - Or add `<PackageReference Include="UR.RTDE" Version="1.0.0" />` to your project.
- Package includes native dependencies; no external Python required.

Build
-----
1. Open `UR.RTDE.Grasshopper.sln` in Visual Studio 2022.
2. Select the desired configuration and target framework.
3. Build. The `.gha` outputs under `bin/<Configuration>/<TargetFramework>/`.

Install into Grasshopper
------------------------
- Copy the built `.gha` file to your Grasshopper Libraries folder:
  - Windows: `%AppData%\Grasshopper\Libraries`
- Alternatively, for Rhino 8, you can package with Yak (the csproj includes a helper target to generate a spec and build in the output directory if Yak is found).

Usage sketch
------------
The component `RTDE_GrasshopperComponent` is currently a stub. A basic RTDE workflow inside `SolveInstance` usually:

1. Read inputs (robot IP, motion parameters).
2. Create `RTDEControl` and `RTDEReceive` from `UR.RTDE`.
3. Execute motion or state queries.
4. Dispose instances deterministically (`using`).

Example (conceptual)
--------------------
```csharp
using UR.RTDE;

using var control = new RTDEControl("192.168.1.100");
using var receive = new RTDEReceive("192.168.1.100");

double[] homeQ = { 0, -1.57, 1.57, -1.57, -1.57, 0 };
control.MoveJ(homeQ, speed: 1.05, acceleration: 1.4);

double[] q = receive.GetActualQ();
```

Safety and Testing
------------------
- Test with URSim first (e‑Series ≥ 5.23.0 recommended) before real hardware.
- Follow your organization’s safety procedures.

License
-------
MIT

Credits
-------
- Built for use with the `UR.RTDE` NuGet package (native C++ P/Invoke wrapper).
- Underlying C++ library: `ur_rtde` by SDU Robotics. See docs linked above.


