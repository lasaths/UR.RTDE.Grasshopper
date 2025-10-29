using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace UR.RTDE.Grasshopper
{
    public enum URActionKind { MoveJ, MoveL, StopJ, StopL, SetDO }

    public class UR_CommandComponent : GH_Component
    {
        private URActionKind _action = URActionKind.MoveJ;

        public UR_CommandComponent()
          : base("UR Command", "URCmd",
            "Execute commands on the robot via RTDE (select action in the menu).",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if command succeeded.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            UpdateOptionalFlags();
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo)) return;
            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                return;
            }

            try
            {
                switch (_action)
                {
                    case URActionKind.MoveJ:
                        var q = new List<double>();
                        da.GetDataList(1, q);
                        double speed = 1.05, accel = 1.4; bool async = false;
                        da.GetData(2, ref speed);
                        da.GetData(3, ref accel);
                        da.GetData(4, ref async);
                        if (q == null || q.Count != 6)
                            throw new ArgumentException("q must be a list of 6 joint angles (rad)");
                        var okMove = session.MoveJ(q.ToArray(), speed, accel, async);
                        da.SetData(0, okMove);
                        da.SetData(1, okMove ? "ok" : $"MoveJ failed: {session.LastError ?? "Unknown error"}");
                        break;

                    case URActionKind.MoveL:
                        var pose = new List<double>();
                        da.GetDataList(6, pose);
                        Rhino.Geometry.Plane plane = Rhino.Geometry.Plane.Unset;
                        bool hasPlane = da.GetData(7, ref plane);
                        double lSpeed = 0.25, lAccel = 1.2; bool lAsync = false;
                        da.GetData(2, ref lSpeed);
                        da.GetData(3, ref lAccel);
                        da.GetData(4, ref lAsync);
                        double[] p6;
                        if (hasPlane && plane.IsValid)
                        {
                            p6 = PoseUtils.PlaneToPose(plane);
                        }
                        else
                        {
                            if (pose == null || pose.Count != 6)
                                throw new ArgumentException("Provide target Plane or pose list [x,y,z,rx,ry,rz]");
                            p6 = pose.ToArray();
                        }
                        var okMoveL = session.MoveL(p6, lSpeed, lAccel, lAsync);
                        da.SetData(0, okMoveL);
                        da.SetData(1, okMoveL ? "ok" : $"MoveL failed: {session.LastError ?? "Unknown error"}");
                        break;

                    case URActionKind.StopJ:
                        double decel = 2.0;
                        da.GetData(5, ref decel);
                        var okStop = session.StopJ(decel);
                        da.SetData(0, okStop);
                        da.SetData(1, okStop ? "ok" : $"Stop failed: {session.LastError ?? "Unknown error"}");
                        break;
                    case URActionKind.StopL:
                        double ldecel = 2.0;
                        da.GetData(5, ref ldecel);
                        var okStopL = session.StopL(ldecel);
                        da.SetData(0, okStopL);
                        da.SetData(1, okStopL ? "ok" : $"StopL failed: {session.LastError ?? "Unknown error"}");
                        break;

                    case URActionKind.SetDO:
                        int pin = 0; bool val = false;
                        da.GetData(8, ref pin);
                        da.GetData(9, ref val);
                        var okDo = session.SetStandardDigitalOut(pin, val);
                        da.SetData(0, okDo);
                        da.SetData(1, okDo ? "ok" : $"SetDO failed: {session.LastError ?? "Unknown error"}");
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

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.play-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }
        public override Guid ComponentGuid => new Guid("2233737c-7ba5-4bf9-9c14-924c5d7077cd");

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Action: MoveJ", (s, e) => { _action = URActionKind.MoveJ; RebuildInputsForAction(); }, true, _action == URActionKind.MoveJ);
            Menu_AppendItem(menu, "Action: MoveL", (s, e) => { _action = URActionKind.MoveL; RebuildInputsForAction(); }, true, _action == URActionKind.MoveL);
            Menu_AppendItem(menu, "Action: StopJ", (s, e) => { _action = URActionKind.StopJ; RebuildInputsForAction(); }, true, _action == URActionKind.StopJ);
            Menu_AppendItem(menu, "Action: StopL", (s, e) => { _action = URActionKind.StopL; RebuildInputsForAction(); }, true, _action == URActionKind.StopL);
            Menu_AppendItem(menu, "Action: SetDO", (s, e) => { _action = URActionKind.SetDO; RebuildInputsForAction(); }, true, _action == URActionKind.SetDO);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            RebuildInputsForAction();
        }

        private void UpdateOptionalFlags()
        {
        }

        private void RebuildInputsForAction()
        {
            if (Params == null) return;

            while (Params.Input.Count > 1)
            {
                var toRemove = Params.Input[1];
                Params.UnregisterInputParameter(toRemove, true);
            }

            Param_Number Num(string name, string nick, string desc, GH_ParamAccess access, double? def = null, bool optional = true)
            {
                var p = new Param_Number { Name = name, NickName = nick, Description = desc, Access = access, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }
            Param_Boolean Bool(string name, string nick, string desc, bool? def = null, bool optional = true)
            {
                var p = new Param_Boolean { Name = name, NickName = nick, Description = desc, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }
            Param_Integer Int(string name, string nick, string desc, int? def = null, bool optional = true)
            {
                var p = new Param_Integer { Name = name, NickName = nick, Description = desc, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }

            switch (_action)
            {
                case URActionKind.MoveJ:
                    Params.RegisterInputParam(Num("Joints", "Q", "Joint target angles (rad)", GH_ParamAccess.list, null, false));
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed", GH_ParamAccess.item, 1.05));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration", GH_ParamAccess.item, 1.4));
                    Params.RegisterInputParam(Bool("Async", "X", "Run asynchronously (non-blocking)", false));
                    break;

                case URActionKind.MoveL:
                    var pose = Num("Pose", "P", "TCP pose [x,y,z,rx,ry,rz] (m,rad)", GH_ParamAccess.list);
                    pose.Optional = true;
                    Params.RegisterInputParam(pose);
                    Params.RegisterInputParam(new Param_Plane { Name = "Target", NickName = "T", Description = "Target Plane (alternative to Pose)", Optional = true });
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed", GH_ParamAccess.item, 0.25));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration", GH_ParamAccess.item, 1.2));
                    Params.RegisterInputParam(Bool("Async", "X", "Run asynchronously (non-blocking)", false));
                    break;

                case URActionKind.StopJ:
                case URActionKind.StopL:
                    Params.RegisterInputParam(Num("Deceleration", "D", "Stop deceleration", GH_ParamAccess.item, 2.0, false));
                    break;

                case URActionKind.SetDO:
                    Params.RegisterInputParam(Int("Pin", "I", "Digital output pin", 0, false));
                    Params.RegisterInputParam(Bool("Value", "B", "Digital output value", false, false));
                    break;
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }
    }
}


