# UR.RTDE.Grasshopper

[![Yak Package](https://img.shields.io/badge/yak-UR--RTDE--Grasshopper-blue)](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
[![Version](https://img.shields.io/badge/version-1.3.0-blue)](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
[![Rhino](https://img.shields.io/badge/Rhino-7%20%26%208-green)](https://www.rhino3d.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Grasshopper components to control Universal Robots via UR.RTDE (C# wrapper). Supports session management, reads (joints/pose/IO/modes), basic commands, and Robotiq gripper control (URCap) via UR.RTDE 1.2. Tested on Rhino 7 (net48) and Rhino 8 (net8.0/net8.0-windows).

## ✨ New in 1.3.0: Rhino 7 & Rhino 8 Yak Packages

Release 1.3.0 ships dedicated Yak packages for both Rhino 7 (`rh7_0`) and Rhino 8 (`rh8_0`) while keeping the same proven read/command experience:

### UR Read (Event-Driven)
✅ **Non-blocking timer-based polling** - UI stays responsive  
✅ **Efficient caching** - Background timer polls data, UI reads from cache  
✅ **High-frequency updates** - Can handle 20-50ms intervals smoothly  
✅ **No stuttering** - Consistent performance during auto-listen  

### UR Command (Simplified)
✅ **Direct execution** - Simple, straightforward command sending  
✅ **Async option** - Fire-and-forget for non-blocking robot moves  
✅ **Synchronous by default** - Immediate feedback on command completion  
✅ **Clean & maintainable** - No complex async framework overhead  

✅ **Fully backward compatible** - existing .gh files work without changes  

> **Technical**: Both components use simple, proven patterns without heavy frameworks - event-driven polling for Read, direct execution with optional async for Command

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

Run the current URSim Docker for UR E-Series to simulate a robot during UI development:

```bash
docker run --rm -it --network host universalrobots/ursim_e-series
```

Connect to the simulated robot at `192.168.56.1` (default Docker host IP) and ensure RTDE ports 30002-30004 are reachable.

## Quick Start

1. **Connect to your robot** using the `UR Session` component
   - Set the robot's IP address (default: `127.0.0.1` for URSim)
   - Click "Connect" to establish the RTDE connection

2. **Read robot state** with the `UR Read` component
   - Use the context menu to select: Joints, Pose, IO, or Modes
   - Enable "Auto listen" for periodic updates without a Timer
   - **NEW**: Right-click → "Cancel" to stop operations

3. **Send commands** with the `UR Command` component
   - Use context menu: MoveJ, MoveL, StopJ, StopL, or SetDO
   - Configure speed, acceleration, and other parameters
   - **NEW**: Right-click → "Cancel" to abort commands mid-execution

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

### UR Command (Simplified ✨)
Sends commands to the robot using **direct execution** with optional async fire-and-forget.

**Context Menu Options:**
- **MoveJ** - Joint space movement
  - `q[6]` - Joint angles in radians (required)
  - `speed` - Speed factor (default: `1.05`)
  - `accel` - Acceleration factor (default: `1.4`)
  - `async` - Run asynchronously (fire-and-forget, default: `false`)

- **MoveL** - Linear movement
  - `pose[6]` - TCP pose `[x,y,z,rx,ry,rz]` in m, rad (optional)
  - `target` - Plane target (alternative to pose)
  - `speed` - Speed in m/s (default: `0.25`)
  - `accel` - Acceleration in m/s² (default: `1.2`)
  - `async` - Run asynchronously (fire-and-forget, default: `false`)

- **StopJ/StopL** - Stop movement
  - `decel` - Deceleration factor (default: `2.0`, required)

- **SetDO** - Set digital output
  - `pin` - Pin number (required)
  - `value` - Boolean value (required)

**Execution Modes:**
- **Synchronous (default)**: Blocks until command completes, provides immediate feedback
- **Asynchronous (async=true)**: Sends command and returns immediately (fire-and-forget)

**How It Works:**
- Synchronous: Direct method call, waits for completion, returns result
- Asynchronous: Fires `Task.Run` for non-blocking execution
- Simple pattern inspired by MQTT Publish component
- No complex async framework overhead

**Performance:**
- Minimal overhead for command execution
- Clean separation of sync vs async behavior
- Concurrent execution prevention
- Immediate feedback for sync operations

### UR Robotiq Gripper
Controls Robotiq grippers (Robotiq URCap required) using the UR.RTDE 1.2 drivers.

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
- Cancel auto-listen when not needed to reduce network traffic

### For Commands (UR Command)
- Check the "OK" output to verify command success
- Read the "Message" output for error details
- Use cancellation for emergency stops

## Troubleshooting

### Component Not Responding
- Try canceling the current operation (right-click → Cancel)
- Check if the session is still connected
- Verify network connectivity to the robot

### Data Seems Delayed
- This is normal for async operations
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
- Docker Desktop installed and running

**Setup:**

1. Pull the URSim image:
   ```bash
   docker pull universalrobots/ursim_e-series
   ```

2. Run the container:
   ```bash
   docker run --rm --name ursim -p 6080:6080 -p 29999:29999 -p 30001-30004:30001-30004 universalrobots/ursim_e-series
   ```

3. Open the simulator UI in your browser:
   - `http://localhost:6080`

4. Connect from Grasshopper:
   - Set `ip` to `127.0.0.1` (localhost)
   - For URSim on another computer, use that host's IP
   - RTDE port `30004` is handled automatically

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

### Upgrading to 1.3.0 (Async Components)

**Good News**: Fully backward compatible!

- Existing `.gh` files work without modification
- Component GUIDs unchanged
- Input/output structure unchanged
- All serialization compatible

**What's Different**:
- Operations are now non-blocking
- New cancellation feature available
- Better performance with auto-listen

## Links

- **NuGet Package**: [UR.RTDE](https://www.nuget.org/packages/UR.RTDE/#readme-body-tab)
- **C++ Library Docs**: [SDU Robotics ur_rtde](https://sdurobotics.gitlab.io/ur_rtde/)
- **GrasshopperAsyncComponent**: [NuGet](https://www.nuget.org/packages/GrasshopperAsyncComponent) | [GitHub](https://github.com/specklesystems/GrasshopperAsyncComponent)
- **Yak Package**: [UR-RTDE-Grasshopper on Yak](https://yak.rhino3d.com/packages/UR-RTDE-Grasshopper)
- **GitHub Repository**: [lasaths/UR.RTDE.Grasshopper](https://github.com/lasaths/UR.RTDE.Grasshopper)

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Credits

- Built for use with the `UR.RTDE` NuGet package (native C++ P/Invoke wrapper)
- Underlying C++ library: `ur_rtde` by SDU Robotics
- Async framework: `GrasshopperAsyncComponent` by Speckle Systems
- Icons: [Phosphor Icons](https://phosphoricons.com) (MIT License, Duotone style)
