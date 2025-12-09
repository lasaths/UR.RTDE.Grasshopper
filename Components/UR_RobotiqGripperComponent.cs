using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace UR.RTDE.Grasshopper
{
    public enum RobotiqActionKind { Activate, Open, Close, Move, SetSpeed, SetForce }

    public class UR_RobotiqGripperComponent : GH_Component
    {
        private readonly Dictionary<string, UR.RTDE.RobotiqGripper> _gripperCache = new Dictionary<string, UR.RTDE.RobotiqGripper>();

        public UR_RobotiqGripperComponent()
          : base("UR Robotiq Gripper", "URGripper",
            "Control Robotiq gripper via URCap URScript functions. Requires Robotiq URCap installed on the controller.",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
            p.AddIntegerParameter("Command", "C", "Gripper command (0=Activate, 1=Open, 2=Close, 3=Move, 4=SetSpeed, 5=SetForce)", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if command succeeded.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo)) return;
            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                return;
            }

            int commandIndex = 1; // Default to "Open"
            if (!da.GetData(1, ref commandIndex))
            {
                da.SetData(0, false);
                da.SetData(1, "Command required");
                return;
            }

            if (commandIndex < 0 || commandIndex > 5)
            {
                da.SetData(0, false);
                da.SetData(1, $"Invalid command index: {commandIndex}. Must be 0-5.");
                return;
            }

            var action = (RobotiqActionKind)commandIndex;

            try
            {
                var gripper = GetOrCreateGripper(session.Ip);
                if (gripper == null)
                {
                    da.SetData(0, false);
                    da.SetData(1, "Failed to create RobotiqGripper instance");
                    return;
                }

                // Ensure connected
                if (!gripper.IsConnected)
                {
                    gripper.ConnectAsync(3000, CancellationToken.None).GetAwaiter().GetResult();
                }

                switch (action)
                {
                    case RobotiqActionKind.Activate:
                        gripper.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, "Activated");
                        break;

                    case RobotiqActionKind.Open:
                        gripper.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, "Opened");
                        break;

                    case RobotiqActionKind.Close:
                        gripper.CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, "Closed");
                        break;

                    case RobotiqActionKind.Move:
                        int position = 128;
                        if (!da.GetData(2, ref position))
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Position (0-255) required");
                            return;
                        }
                        if (position < 0 || position > 255)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Position must be 0-255");
                            return;
                        }
                        gripper.MoveAsync((byte)position, CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, $"Moved to position {position}");
                        break;

                    case RobotiqActionKind.SetSpeed:
                        int speed = 128;
                        if (!da.GetData(2, ref speed))
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Speed (0-255) required");
                            return;
                        }
                        if (speed < 0 || speed > 255)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Speed must be 0-255");
                            return;
                        }
                        gripper.SetSpeedAsync((byte)speed, CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, $"Speed set to {speed}");
                        break;

                    case RobotiqActionKind.SetForce:
                        int force = 128;
                        if (!da.GetData(2, ref force))
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Force (0-255) required");
                            return;
                        }
                        if (force < 0 || force > 255)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Force must be 0-255");
                            return;
                        }
                        gripper.SetForceAsync((byte)force, CancellationToken.None).GetAwaiter().GetResult();
                        da.SetData(0, true);
                        da.SetData(1, $"Force set to {force}");
                        break;

                    default:
                        da.SetData(0, false);
                        da.SetData(1, "Not implemented");
                        break;
                }
            }
            catch (Exception ex)
            {
                da.SetData(0, false);
                da.SetData(1, ex.Message);
            }
        }

        private UR.RTDE.RobotiqGripper GetOrCreateGripper(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return null;

            lock (_gripperCache)
            {
                if (!_gripperCache.TryGetValue(ip, out var gripper))
                {
                    try
                    {
                        gripper = new UR.RTDE.RobotiqGripper(ip, 30002);
                        _gripperCache[ip] = gripper;
                    }
                    catch
                    {
                        return null;
                    }
                }
                return gripper;
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.robot-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("177b4f5e-115c-4687-a9db-c67951d0dee0");

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            RebuildInputsForCommand();
        }

        protected override void OnParameterChanged(GH_ParamServer sender)
        {
            base.OnParameterChanged(sender);
            if (sender == Params.Input[1]) // Command parameter changed
            {
                RebuildInputsForCommand();
            }
        }

        private void RebuildInputsForCommand()
        {
            if (Params == null) return;

            // Keep Session (0) and Command (1), remove any additional inputs
            while (Params.Input.Count > 2)
            {
                var toRemove = Params.Input[2];
                Params.UnregisterInputParameter(toRemove, true);
            }

            // Get current command value if available
            int commandIndex = 1; // Default
            if (Params.Input.Count > 1)
            {
                var cmdParam = Params.Input[1] as Param_Integer;
                if (cmdParam != null && cmdParam.VolatileDataCount > 0)
                {
                    try
                    {
                        var value = cmdParam.VolatileData.get_Branch(0)[0];
                        if (value is Grasshopper.Kernel.Types.GH_Integer ghInt)
                            commandIndex = ghInt.Value;
                    }
                    catch { }
                }
            }

            if (commandIndex < 0 || commandIndex > 5) commandIndex = 1;

            var action = (RobotiqActionKind)commandIndex;

            Param_Integer Int(string name, string nick, string desc, int? def = null, bool optional = false)
            {
                var p = new Param_Integer { Name = name, NickName = nick, Description = desc, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }

            switch (action)
            {
                case RobotiqActionKind.Move:
                    Params.RegisterInputParam(Int("Position", "P", "Gripper position (0=open, 255=closed)", 128, false));
                    break;

                case RobotiqActionKind.SetSpeed:
                    Params.RegisterInputParam(Int("Speed", "V", "Gripper speed (0-255)", 128, false));
                    break;

                case RobotiqActionKind.SetForce:
                    Params.RegisterInputParam(Int("Force", "F", "Gripper force (0-255)", 128, false));
                    break;

                case RobotiqActionKind.Activate:
                case RobotiqActionKind.Open:
                case RobotiqActionKind.Close:
                    // No additional inputs needed
                    break;
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo)) return;
            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                return;
            }

            int commandIndex = 1; // Default to "Open"
            if (!da.GetData(1, ref commandIndex))
            {
                da.SetData(0, false);
                da.SetData(1, "Command required");
                return;
            }

            if (commandIndex < 0 || commandIndex > 5)
            {
                da.SetData(0, false);
                da.SetData(1, $"Invalid command index: {commandIndex}. Must be 0-5.");
                return;
            }

            var action = (RobotiqActionKind)commandIndex;

            try
            {
                var gripper = GetOrCreateGripper(session.Ip);
                if (gripper == null)
                {
                    da.SetData(0, false);
                    da.SetData(1, "Failed to create RobotiqGripper instance");
                    return;
                }

                // Ensure connected
                if (!gripper.IsConnected)
                {
                    gripper.ConnectAsync(3000, CancellationToken.None).GetAwaiter().GetResult();
                }

                switch (action)

        public override void RemovedFromDocument(GH_Document document)
        {
            // Clean up cached grippers
            lock (_gripperCache)
            {
                foreach (var gripper in _gripperCache.Values)
                {
                    try { gripper.Dispose(); } catch { }
                }
                _gripperCache.Clear();
            }
            base.RemovedFromDocument(document);
        }
    }
}

