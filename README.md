UR.RTDE.Grasshopper
===================

A minimal Grasshopper (Rhino) plugin scaffold intended to integrate the `UR.RTDE` C# wrapper for Universal Robots RTDE.

Disclaimer
----------
This codebase was built with assistance from AI tools. It is provided “AS IS”, without warranty of any kind, express or implied. The authors and contributors assume no liability for any damages or consequences arising from its use. Use at your own risk, validate in simulation (URSim) first, and follow all applicable safety procedures.

Links
-----
- NuGet package: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/#readme-body-tab)
- ur_rtde C++ library docs: [SDU Robotics ur_rtde](https://sdurobotics.gitlab.io/ur_rtde/)

Colors
------
- UR robot blue (RAL Design 240 70 20, approx): `#82B2C9` (RGB 130,178,201)
- UR robot grey (RAL 9007 Grey aluminium, approx): `#8F8F8C` (RGB 143,143,140)

Status
------
- Targets: `net48`, `net7.0`, `net7.0-windows` (see `UR.RTDE.Grasshopper.csproj`).
- Rhino 7: use `net48`. Rhino 8: use `net7.0` or `net7.0-windows`.
- Uses `UR.RTDE` NuGet package only (no local project refs).

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

Yak packaging
-------------
- Yak packaging runs by default when Yak is available.
- Default discovery paths:
  - Windows: `C:\\Program Files\\Rhino 8\\System\\Yak.exe`
  - macOS: `/Applications/Rhino 8.app/Contents/Resources/bin/yak`
- If Yak is installed elsewhere, provide the path at build time:
  - `dotnet build -c Release -f net7.0-windows -p:YakExecutable="C:\\Path\\To\\Yak.exe"`
- To disable Yak packaging for a build: `-p:BuildYakPackage=false`

Yak quickstart (Rhino 8)
------------------------
- Build a release for Rhino 8 Windows and generate a Yak package:
  - `dotnet build -c Release -f net7.0-windows`
- Find the output `.yak` and `manifest.yml` in `bin/Release/net7.0-windows/`.
- Install the package for your user profile:
  - `"C:\\Program Files\\Rhino 8\\System\\Yak.exe" install --source bin/Release/net7.0-windows .`
  - Or copy the built `.gha` directly (see below) if you prefer not to use Yak.

Install into Grasshopper
------------------------
- Copy the built `.gha` file to your Grasshopper Libraries folder:
  - Windows: `%AppData%\Grasshopper\Libraries`
- Alternatively, for Rhino 8, you can package with Yak (the csproj includes a helper target to generate a spec and build in the output directory if Yak is found).

Notes
-----
- Targets: `net48` (Rhino 7), `net7.0`/`net7.0-windows` (Rhino 8). Choose the framework that matches your Rhino.
- The generated Yak manifest pulls metadata (name, version, authors, description, tags) from the project file.

Components
----------
- UR Session
  - Inputs: `ip`, `auto_connect`, `timeout_ms`, `reconnect`
  - Outputs: `session`, `is_connected`, `status`, `last_error`
- UR Read (context menu: Joints, Pose, IO, Modes)
  - Auto listen: enable from context menu to schedule periodic reads without a GH Timer
    - Toggle: "Auto listen (schedule reads)"
    - Interval presets: 20, 50, 100, 200, 500, 1000 ms (context submenu "Auto interval")
  - Joints: list `[q0..q5]` (rad)
  - Pose: list `[x,y,z,rx,ry,rz]` (m, rad)
  - IO: DataTree
    - `{0}`: `din[0..17]` as bools
    - `{1}`: `dout[0..17]` as bools
    - `{2}`: `[ai0, ai1, ao0, ao1]`
  - Modes: DataTree
    - `{0}`: robot mode label+code
    - `{1}`: safety mode label+code
    - `{2}`: program running (bool)
- UR Command (context menu: MoveJ, MoveL, StopJ, StopL, SetDO)
  - MoveJ: inputs `q[6]`, `speed`, `accel`, `async`
  - MoveL: inputs `target:Plane` or `pose[6]`, `speed`, `accel`, `async`
  - StopJ/StopL: input `decel`
  - SetDO: inputs `pin`, `value`

Safety and Testing
------------------
- Test with URSim first (e‑Series ≥ 5.23.0 recommended) before real hardware.
- Follow your organization’s safety procedures.

URSim via Docker (e‑Series)
---------------------------
- Image: `universalrobots/ursim_e-series` (see Docker Hub: [URSim e‑series](https://hub.docker.com/r/universalrobots/ursim_e-series))
- Requirements: Docker Desktop installed and running.
- Pull the image:
  - `docker pull universalrobots/ursim_e-series`
- Run the container (noVNC on 6080, Dashboard 29999, RTDE 30004, plus common ports):
  - `docker run --rm --name ursim -p 6080:6080 -p 29999:29999 -p 30001-30004:30001-30004 universalrobots/ursim_e-series`
- Open the simulator UI in your browser:
  - `http://localhost:6080`
- Connect the Grasshopper `UR Session` component:
  - Set `ip` to `127.0.0.1` (localhost) when URSim is running on your machine via Docker.
  - If URSim runs on another computer, use that host's IP instead of localhost.
  - For a real robot, use the robot's IP address.
  - RTDE port is typically `30004` (handled by the library; just provide IP in this component).
- Notes:
  - For reading state, URSim can be idle. For motion commands, ensure the robot is in "Remote Control" and the program is started/unpaused inside PolyScope.
  - If ports are busy, adjust the `-p` mappings accordingly.
  - Use e‑Series images ≥ 5.23 for best compatibility.

License
-------
MIT

Credits
-------
- Built for use with the `UR.RTDE` NuGet package (native C++ P/Invoke wrapper).
- Underlying C++ library: `ur_rtde` by SDU Robotics. See docs linked above.
- Icons: Phosphor Icons (https://phosphoricons.com), MIT License. Duotone is the primary style.


