# UR.RTDE.Grasshopper

[![Yak Package](https://img.shields.io/badge/yak-UR--RTDE--Grasshopper-blue)](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
[![Version](https://img.shields.io/badge/version-1.6.3.2-blue)](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
[![Rhino](https://img.shields.io/badge/Rhino-7%20%26%208-green)](https://www.rhino3d.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Grasshopper components to control Universal Robots via UR.RTDE (C# wrapper). Supports session management, reads (joints/pose/IO/modes), discrete commands, continuous SpeedJ/ServoJ streaming, and Robotiq gripper control (URCap) via UR.RTDE 1.6.3.9. Tested on Rhino 7 (net48) and Rhino 8 (net8.0/net8.0-windows).

## ✨ New in 1.3.0: Rhino 7 & Rhino 8 Yak Packages

Release 1.3.0 ships dedicated Yak packages for both Rhino 7 (`rh7_0`) and Rhino 8 (`rh8_0`) while keeping the same proven read/command experience:

### UR Read (Event-Driven)
✅ **Non-blocking timer-based polling** - UI stays responsive  
✅ **Efficient caching** - Background timer polls data, UI reads from cache  
✅ **High-frequency updates** - Can handle 20-50ms intervals smoothly  
✅ **No stuttering** - Consistent performance during auto-listen  

### UR Write
✅ **Run button for motion** - MoveJ and MoveL are sent explicitly from the component UI  
✅ **Optional Auto Send** - Right-click to arm automatic sends on new target input  
✅ **Auto Send resets for safety** - Not persisted with the component state  
✅ **Clean & maintainable** - Direct command execution with a simple UI trigger model  

✅ **Fully backward compatible** - existing .gh files work without changes  

> **Technical**: Both components use simple, proven patterns without heavy frameworks: event-driven polling for Read and direct execution with explicit UI triggers for Write.

## Installation

### Via Rhino Package Manager (Default / Recommended)

1. Open Rhino 8 or Rhino 7
2. Open **Tools → Package Manager** (or `Ctrl+Shift+P`)
3. Search for `UR-RTDE-Grasshopper`
4. Click **Install** (the package manager entry is the default way to install UR.RTDE.Grasshopper)

### Via Yak Command Line

Alternatively, you can install via the command line:

```bash
yak install UR-RTDE-Grasshopper
```

### Manual Installation

Copy the built `.gha` file to your Grasshopper Libraries folder:
- **Windows**: `%AppData%\Grasshopper\Libraries`

### UR Simulation Testing (UR Docker)

Run URSim in Docker for local testing (see [Testing with URSim](#testing-with-ursim) below). On **macOS**, use port mapping and connect at `127.0.0.1` — `--network host` is Linux-only.

## Quick Start

1. **Connect to your robot** using the `UR Session` component
   - Set the robot's IP address (default: `127.0.0.1` for URSim)
   - Click "Connect" to establish the RTDE connection

2. **Read robot state** with the `UR Read` component
   - Use the dropdown for Joints, Pose, IO, Modes, Targets, Dynamics, Status, FK (compute), or IK (compute)
   - Enable **Listen** for periodic updates without a Timer

3. **Discrete commands** with the `UR Write` component (UR → RTDE ribbon panel)
   - MoveJ / MoveL (Run button or optional Auto Send), Stop, Set DO/AO, Tool DO, Set TCP, Set Payload, Speed/Servo stop
   - MoveJ/MoveL waypoint lists wait for each segment to complete before advancing

4. **Continuous live control** with the `UR Stream` component (same ribbon panel as **UR Write**)
   - SpeedJ (joint velocities `qd`) or ServoJ (joint positions `q`)
   - Click **Arm stream** on the component before sending; armed state is not saved with the file (safety)
   - Drive joints from a Timer or sliders at 20–500 ms (context menu **Min interval**); not for single “go to pose” moves (use **UR Write**)

**⚠️ Important**: Always test with URSim first before connecting to real hardware!

## Components

### UR Session
Manages the RTDE connection to the Universal Robot.

**Inputs:**
- `ip` - Robot IP address (optional, defaults to `127.0.0.1`)
- `timeout_ms` - Connection timeout in milliseconds (optional, defaults to `2000`)
- `reconnect` - Auto-reconnect on disconnect (optional, defaults to `false`)

**Outputs:**
- `session` - Session object for use with other components
- `is_connected` - Connection status (boolean)
- `status` - Connection status message
- `last_error` - Last error message if any

**Features:**
- Visual connection indicator (green point when connected)
- Connect/Disconnect button on the component

### UR Read (Event-Driven ✨)
Reads robot state data from the robot using **event-driven timer polling** for smooth, non-blocking updates.

**Context Menu Options:**
- **Joints** - Read joint angles `[q0..q5]` (radians)
- **Pose** - Read TCP pose as a Plane (converted from `[x,y,z,rx,ry,rz]` in m, rad)
- **IO** - Read digital/analog IO states
  - `{0}`: Digital inputs `din[0..17]` (bools)
  - `{1}`: Digital outputs `dout[0..17]` (bools)
  - `{2}`: Analog IO `[ai0, ai1, ao0, ao1]`
- **Modes** - Read robot and safety modes
  - `{0}`: Robot mode (label + code)
  - `{1}`: Safety mode (label + code)
  - `{2}`: Program running (bool)
- **Targets** - `{0}` target q, `{1}` target TCP plane
- **Dynamics** - `{0}` qd, `{1}` TCP speed, `{2}` TCP force
- **Status** - `{0}` is steady, `{1}` robot status, `{2}` runtime state
- **FK (compute)** - input `q[6]` → TCP plane (+ raw pose tree)
- **IK (compute)** - target Plane or pose → `{0}` q, `{1}` hasSolution

**Auto Listen Feature:**
- Enable from context menu: "Auto listen (schedule reads)"
- Interval presets: 20, 50, 100, 200, 500, 1000 ms
- **Event-driven architecture** - Background timer polls, UI reads from cache
- **No blocking** - UI stays responsive at all intervals
- Automatically schedules periodic reads without a Grasshopper Timer

**How It Works:**
- When enabled, a background timer polls the robot at the specified interval
- Read data is cached in a thread-safe manner
- Component outputs the cached data without blocking
- Similar to MQTT Subscribe pattern for efficient data streaming

**Performance:**
- Can handle 20ms intervals without stuttering
- Minimal UI impact during polling
- Multiple instances run independently
- Clean start/stop behavior

### UR Write ✨
Sends **discrete** commands using direct execution with an explicit `Run` button for motion. Appears on the **UR → RTDE** ribbon panel (primary command components).

**Context Menu Options:**
- **MoveJ** - Joint space movement
  - `q[6]` - Joint angles in radians (required)
  - `speed` - Speed factor (default: `1.05`)
  - `accel` - Acceleration factor (default: `1.4`)
  - `Run` button - Sends the current joint target once
  - `Auto Send` menu item - Sends only when the target input changes, off by default and not persisted

- **MoveL** - Linear movement
  - `pose[6]` - TCP pose `[x,y,z,rx,ry,rz]` in m, rad (optional)
  - `target` - Plane target (alternative to pose)
  - `speed` - Speed in m/s (default: `0.25`)
  - `accel` - Acceleration in m/s² (default: `1.2`)
  - `Run` button - Sends the current target once
  - `Auto Send` menu item - Sends only when the target input changes, off by default and not persisted

- **Stop** - Stop current movement
  - `decel` - Deceleration factor (default: `2.0`, required)

- **Set DO / Set AO / Tool DO** - Digital and analog outputs
- **Set TCP / Set Payload** - Tool frame and payload mass + CoG
- **Speed Stop / Servo Stop** - Halt an active stream (pairs with **UR Stream**)

**How It Works:**
- MoveJ and MoveL only send when you click the component `Run` button
- `Auto Send` can be enabled from the context menu to send on new target input only
- `Auto Send` is intentionally not saved with the component state
- Stop and SetDO execute directly when solved

**Performance:**
- Minimal overhead for command execution
- Concurrent execution prevention
- Immediate feedback through `OK`, `Message`, `Running`, and `Done`

### UR Stream ✨
Continuous live control via RTDE **SpeedJ** or **ServoJ** (same **UR → RTDE** ribbon panel as **UR Write**). Use for real-time joint streaming from Grasshopper—not for single waypoint moves.

**Ribbon:** `UR` tab → `RTDE` panel, alongside **UR Write** (broadcast icon, same cyan duotone style as the write rocket icon).

**Modes (dropdown on component):**
- **SpeedJ** - stream joint velocities
  - `QD` - `qd[6]` joint velocities (rad/s, required)
  - `A` - acceleration (default `0.5`)
  - `T` - time step `dt` in seconds (default `0.02`)
- **ServoJ** - stream joint positions
  - `Q` - `q[6]` joint positions (rad, required)
  - `V` - speed (default `0.5`)
  - `A` - acceleration (default `0.5`)
  - `T` - time step `dt` in seconds (default `0.02`)
  - `L` - lookahead time in seconds (default `0.1`)
  - `G` - servo gain (default `300`)

**Common inputs / outputs:**
- `Session` - connected UR session (required)
- `OK` - whether the last stream command succeeded
- `Message` - status or error text (includes rate-limit hint when unchanged)
- `Armed` - whether the stream is armed

**UI:**
- **Arm stream** button - must be on before commands are sent; turns off on document load (not persisted)
- Mode dropdown - SpeedJ vs ServoJ (rebuilds inputs when changed)

**Context menu:**
- **Min interval** - rate limit duplicate sends: 20, 50, 100, 200, or 500 ms (default 50 ms)

**How it works:**
1. Connect with **UR Session**
2. Select SpeedJ or ServoJ and wire six joint values (list)
3. Click **Arm stream**
4. Solve repeatedly (Timer recommended at ≥ min interval)
5. Disarm, disconnect, or remove the component to send SpeedStop/ServoStop

**Safety:**
- Armed state is **not** saved in `.gh` files—re-arm after opening a definition
- Do not use for collision-free planning; validate in URSim first
- Pair with **UR Write** → Speed Stop / Servo Stop if you need to halt from another component

### UR Robotiq Gripper
Controls Robotiq grippers (Robotiq URCap required) using the UR.RTDE 1.6.3.9 drivers.

**Backends (menu):**
- **Native** (port `63352`) - direct Robotiq driver with status codes
- **RTDE bridge** - uses RTDE registers and installs the bridge script automatically
- **URScript** (port `30002`) - calls `rq_*` URCap functions

**Actions (menu):**
- **Activate** - optional auto-calibration
- **Open / Close** - speed/force (device units `0-255`), optional wait-for-motion for native
- **Move** - position `0-255` (device units), plus speed/force and optional wait-for-motion

**Common inputs:**
- `install bridge` - only used for RTDE backend (default on)
- `timeout_ms` - command timeout
- `port` - only used for Native/URScript backends

## Performance Tips

### For Auto-Listen (UR Read)
- **Real-time visualization**: 50-100ms
- **Background monitoring**: 200-500ms
- **Logging/recording**: 100-200ms
- Disable **Listen** when not needed to reduce network traffic

### For Commands (UR Write)
- Check the "OK" output to verify command success
- Read the "Message" output for error details
- Use the red `Stop` action to halt motion

### For Streaming (UR Stream)
- Use **ServoJ** when following joint position targets; **SpeedJ** for velocity control
- Match Timer period to **Min interval** (e.g. 50 ms Timer + 50 ms min interval)
- Keep armed only while actively controlling; disarm when done
- Prefer URSim before hardware; streaming can move the arm continuously without MoveJ completion waits

## Troubleshooting

### Component Not Responding
- Check if the session is still connected
- Verify network connectivity to the robot

### Data Seems Delayed
- Auto Send waits for a new target change before sending again
- Check your auto-listen interval setting
- Network latency may affect timing

### Commands Not Executing
- Verify the "OK" output is True
- Read the "Message" output for error details
- Check robot safety status and mode

## Testing with URSim

Before connecting to a real robot, always test with URSim.

### URSim via Docker (e‑Series)

**Requirements:**
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows or macOS)

**Setup:**

1. Start URSim from the **UR.RTDE** repo (same ports as [Multi-Actor-Interface-Library/docker/ursim](https://github.com/lasaths/Multi-Actor-Interface-Library/tree/main/docker/ursim)):

   ```bash
   cd /path/to/UR.RTDE
   docker compose -f docker/ursim/docker-compose.yml up -d
   ```

   Or pull/run manually (must include **30003** for control, not only 30004):

   ```bash
   docker pull universalrobots/ursim_e-series
   docker rm -f ursim 2>/dev/null
   docker run -d --name ursim \
     -p 127.0.0.1:6080:6080 \
     -p 127.0.0.1:5900:5900 \
     -p 127.0.0.1:29999:29999 \
     -p 127.0.0.1:30001:30001 \
     -p 127.0.0.1:30002:30002 \
     -p 127.0.0.1:30003:30003 \
     -p 127.0.0.1:30004:30004 \
     universalrobots/ursim_e-series
   ```

2. Verify ports before connecting Grasshopper:

   ```bash
   nc -zv 127.0.0.1 30003
   nc -zv 127.0.0.1 30004
   ```

3. Open the simulator UI in your browser (use this exact URL, not the bare port):
   - [http://localhost:6080/vnc.html?host=localhost&port=6080](http://localhost:6080/vnc.html?host=localhost&port=6080)
   - Wait until PolyScope finishes booting (1–2 minutes): power on, release brakes — `RTDEControl` fails until the controller is ready even when port 30003 is open

4. Connect from Grasshopper:
   - Set `ip` to `127.0.0.1` (localhost)
   - Click **Connect** on the UR Session component (or use downstream components after a successful connect)
   - **URSim (Docker)**: PolyScope can stay in **Local Control**; the session tries several RTDE control flag combinations
   - For physical robots, use your normal Remote Control workflow

**Useful commands** (from `UR.RTDE` repo root):
```bash
docker compose -f docker/ursim/docker-compose.yml logs -f
docker compose -f docker/ursim/docker-compose.yml stop
docker compose -f docker/ursim/docker-compose.yml down
```

#### macOS (Docker Desktop)

1. Install [Docker Desktop for Mac](https://docs.docker.com/desktop/setup/install/mac-install/) and start it (whale icon in the menu bar should be steady, not “starting”).
2. If you previously used Colima, stop it and use Docker Desktop’s context instead:

   ```bash
   colima stop 2>/dev/null || true
   unset DOCKER_HOST
   docker context use desktop-linux
   ```

3. Run the pull/run steps above. Connect Grasshopper to `127.0.0.1` (not `192.168.56.1` — that address is for Linux `--network host` only).

**Apple Silicon (M1/M2/M3/M4):** The official `ursim_e-series` image is `linux/amd64`. On Mac ARM, the native `URControl` process often fails to start (PolyScope shows **“no controller”**), even with Docker Desktop. Check with:

```bash
docker exec ursim tail -20 /ursim/URControl.log
```

If you see `Mutex ... Error code = 95`, the simulator UI loads but the robot controller did not start. Reliable options:

- Run URSim Docker on **Windows** or **Linux** (amd64), or a remote Linux VM, and connect Grasshopper to that host’s IP
- Use a physical robot or URSim on a Windows machine on the same network

**Important Notes:**
- For reading state, URSim can be idle
- For motion commands, ensure robot is in "Remote Control" and program is started/unpaused in PolyScope
- Use e‑Series images ≥ 5.23.0 for best compatibility
- Adjust port mappings if ports are busy

## Building from Source

### Prerequisites
- Visual Studio 2022 or later
- .NET SDK 7.0 or later
- Rhino 8 (for yak packaging)

### Build Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/lasaths/UR.RTDE.Grasshopper.git
   cd UR.RTDE.Grasshopper
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build:
   ```bash
   dotnet build -c Release
   ```

### Debugging Rhino 8 on macOS (Rider / VS Code / Cursor)

- **Rider**: run configuration `Rhino 8 Mac - Debug` (`.run/Rhino 8 Mac - Debug.run.xml` → `Properties/launchSettings.json`).
- **VS Code / Cursor**: launch config `Rhino 8 Mac - Debug` (`.vscode/launch.json`).
- Default Rhino path: `/Volumes/Storage/00_Applications/Rhino 8.app` — change in `launchSettings.json` and/or `rhino.mac.app` in `.vscode/settings.json`.
- Loads the plugin from `bin/Debug/net8.0` via `RHINO_PACKAGE_DIRS` (not the installed Libraries copy).

### Target Frameworks
- **net48** - For Rhino 7
- **net8.0** - For Rhino 8 (cross-platform)
- **net8.0-windows** - For Rhino 8 (Windows, recommended)

The `.gha` files are output to `bin/Release/<TargetFramework>/`.

### Yak Packaging

Yak packaging runs automatically when Yak is available. The package is built to `bin/Release/net48/`.

**Custom Yak Path:**
```bash
dotnet build -c Release -f net8.0-windows -p:YakExecutable="C:\Path\To\Yak.exe"
```

**Disable Yak Packaging:**
```bash
dotnet build -p:BuildYakPackage=false
```

## Safety

⚠️ **Critical Safety Warning**

- **Always test with URSim first** before connecting to real hardware
- This plugin controls industrial robots that can cause serious injury
- Follow all safety procedures defined by your organization
- Ensure emergency stop procedures are in place
- The authors assume no liability for damages or injuries

This codebase was built with assistance from AI tools and mirrors the upstream `https://github.com/lasaths/UR.RTDE` workflow. It is provided "AS IS", without warranty of any kind, express or implied. Use at your own risk.

## Migration from Previous Versions

Existing `.gh` files remain compatible: component GUIDs and input/output wiring are unchanged. UR Read uses timer-based polling; UR Write uses a Run button and optional Auto Send (not persisted).

## Links

- **NuGet Package**: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/#readme-body-tab)
- **C++ Library Docs**: [SDU Robotics ur_rtde](https://sdurobotics.gitlab.io/ur_rtde/)
- **Yak Package**: [UR-RTDE-Grasshopper on Yak](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
- **GitHub Repository**: [lasaths/UR.RTDE.Grasshopper](https://github.com/lasaths/UR.RTDE.Grasshopper)

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Credits

- Built for use with the `UR.RTDE` NuGet package (native C++ P/Invoke wrapper)
- Underlying C++ library: `ur_rtde` by SDU Robotics
- Icons: [Phosphor Icons](https://phosphoricons.com) (MIT License, Duotone style)
