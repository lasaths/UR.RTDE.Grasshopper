using System;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

namespace UR.RTDE.Grasshopper
{
    public enum URReadKind { Joints, Pose, IO, Modes }

    public class UR_ReadComponent : GH_Component
    {
        private URReadKind _kind = URReadKind.Joints;
        private bool _autoListen = false;
        private int _autoIntervalMs = 100;

        public UR_ReadComponent()
          : base("UR Read", "URRead",
            "Read values from the robot via RTDE (select what to read in the menu).",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Data", "D", "Read result. Joints: list of 6 numbers. Pose: Plane. IO and Modes: tree with sub-paths.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo)) return;

            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, null);
                da.SetData(1, "Session not connected");
                return;
            }

            try
            {
                switch (_kind)
                {
                    case URReadKind.Joints:
                        var q = session.GetActualQ();
                        var qTree = new GH_Structure<IGH_Goo>();
                        var qPath = new GH_Path(0);
                        for (int i = 0; i < 6 && i < q.Length; i++)
                            qTree.Append(new GH_Number(q[i]), qPath);
                        da.SetDataTree(0, qTree);
                        da.SetData(1, "ok");
                        break;
                    case URReadKind.Pose:
                        var p6 = session.GetActualTCPPose();
                        var plane = PoseUtils.PoseToPlane(p6);
                        da.SetData(0, plane);
                        da.SetData(1, "ok");
                        break;
                    case URReadKind.IO:
                        var din = session.GetDigitalInState();
                        var dout = session.GetDigitalOutState();
                        var ai0 = session.GetStandardAnalogInput0();
                        var ai1 = session.GetStandardAnalogInput1();
                        var ao0 = session.GetStandardAnalogOutput0();
                        var ao1 = session.GetStandardAnalogOutput1();

                        var ioTree = new GH_Structure<IGH_Goo>();
                        var pDin = new GH_Path(0);
                        var pDout = new GH_Path(1);
                        var pAnalog = new GH_Path(2);
                        for (int i = 0; i < 18; i++)
                        {
                            bool dinBit = ((din >> i) & 1) == 1;
                            bool doutBit = ((dout >> i) & 1) == 1;
                            ioTree.Append(new GH_Boolean(dinBit), pDin);
                            ioTree.Append(new GH_Boolean(doutBit), pDout);
                        }
                        ioTree.Append(new GH_Number(ai0), pAnalog);
                        ioTree.Append(new GH_Number(ai1), pAnalog);
                        ioTree.Append(new GH_Number(ao0), pAnalog);
                        ioTree.Append(new GH_Number(ao1), pAnalog);
                        da.SetDataTree(0, ioTree);
                        da.SetData(1, "ok");
                        break;
                    case URReadKind.Modes:
                        var rmode = session.GetRobotMode();
                        var smode = session.GetSafetyMode();
                        var running = session.IsProgramRunning();

                        var modeTree = new GH_Structure<IGH_Goo>();
                        modeTree.Append(new GH_String($"{MapRobotMode(rmode)} ({rmode})"), new GH_Path(0));
                        modeTree.Append(new GH_String($"{MapSafetyMode(smode)} ({smode})"), new GH_Path(1));
                        modeTree.Append(new GH_Boolean(running), new GH_Path(2));
                        da.SetDataTree(0, modeTree);
                        da.SetData(1, "ok");
                        break;
                    default:
                        da.SetData(0, null);
                        da.SetData(1, "Not implemented");
                        break;
                }
            }
            catch (Exception ex)
            {
                da.SetData(0, null);
                da.SetData(1, ex.Message);
            }

            if (_autoListen)
            {
                var doc = OnPingDocument();
                if (doc != null)
                    doc.ScheduleSolution(_autoIntervalMs, d => ExpireSolution(true));
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.binoculars-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }
        public override Guid ComponentGuid => new Guid("5db18069-6306-4b80-957b-f189fc71f8cf");

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Read: Joints", (s, e) => { _kind = URReadKind.Joints; ExpireSolution(true); }, true, _kind == URReadKind.Joints);
            Menu_AppendItem(menu, "Read: Pose", (s, e) => { _kind = URReadKind.Pose; ExpireSolution(true); }, true, _kind == URReadKind.Pose);
            Menu_AppendItem(menu, "Read: IO", (s, e) => { _kind = URReadKind.IO; ExpireSolution(true); }, true, _kind == URReadKind.IO);
            Menu_AppendItem(menu, "Read: Modes", (s, e) => { _kind = URReadKind.Modes; ExpireSolution(true); }, true, _kind == URReadKind.Modes);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Auto listen (schedule reads)", (s, e) =>
            {
                _autoListen = !_autoListen;
                ExpireSolution(true);
            }, true, _autoListen);

            var intervalRoot = Menu_AppendItem(menu, "Auto interval");
            void addInterval(string label, int ms)
            {
                Menu_AppendItem(intervalRoot.DropDown, label, (s, e) => { _autoIntervalMs = ms; if (_autoListen) ExpireSolution(true); }, true, _autoIntervalMs == ms);
            }
            addInterval("20 ms", 20);
            addInterval("50 ms", 50);
            addInterval("100 ms", 100);
            addInterval("200 ms", 200);
            addInterval("500 ms", 500);
            addInterval("1000 ms", 1000);
        }

        private static string MapRobotMode(int mode)
        {
            switch (mode)
            {
                case 0: return "Disconnected";
                case 1: return "ConfirmSafety";
                case 2: return "Booting";
                case 3: return "PowerOff";
                case 4: return "PowerOn";
                case 5: return "Idle";
                case 6: return "Backdrive";
                case 7: return "Running";
                case 8: return "UpdatingFirmware";
                default: return "Unknown";
            }
        }

        private static string MapSafetyMode(int mode)
        {
            switch (mode)
            {
                case 1: return "Normal";
                case 2: return "Reduced";
                case 3: return "ProtectiveStop";
                case 4: return "Recovery";
                case 5: return "SafeguardStop";
                case 6: return "SystemEmergencyStop";
                case 7: return "RobotEmergencyStop";
                case 8: return "Violation";
                case 9: return "Fault";
                case 10: return "AutomaticModeSafeguardStop";
                case 11: return "ThreePositionEnablingStop";
                default: return "Unknown";
            }
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("kind", (int)_kind);
            writer.SetBoolean("auto", _autoListen);
            writer.SetInt32("interval", _autoIntervalMs);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("kind")) _kind = (URReadKind)reader.GetInt32("kind");
            if (reader.ItemExists("auto")) _autoListen = reader.GetBoolean("auto");
            if (reader.ItemExists("interval")) _autoIntervalMs = reader.GetInt32("interval");
            return base.Read(reader);
        }
    }
}


