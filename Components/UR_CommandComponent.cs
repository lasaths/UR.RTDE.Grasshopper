using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;

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
            p.AddNumberParameter("Joints", "Q", "Joint target angles (rad). Used for MoveJ.", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "V", "Motion speed.", GH_ParamAccess.item, 1.05);
            p.AddNumberParameter("Acceleration", "A", "Motion acceleration.", GH_ParamAccess.item, 1.4);
            p.AddBooleanParameter("Async", "X", "Run asynchronously (non-blocking).", GH_ParamAccess.item, false);
            p.AddNumberParameter("Deceleration", "D", "Stop joint deceleration (for Stop).", GH_ParamAccess.item, 2.0);
            p.AddNumberParameter("Pose", "P", "TCP target pose [x,y,z,rx,ry,rz] (m, rad). Used for MoveL.", GH_ParamAccess.list);
            p.AddPlaneParameter("Target", "T", "Target Plane for MoveL (alternative to Pose).", GH_ParamAccess.item);
            p.AddIntegerParameter("Pin", "I", "Digital output pin (SetDO).", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Value", "B", "Digital output value (SetDO).", GH_ParamAccess.item, false);
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
                        da.SetData(1, okMove ? "ok" : "MoveJ failed");
                        break;

                    case URActionKind.MoveL:
                        var pose = new List<double>();
                        da.GetDataList(6, pose);
                        Rhino.Geometry.Plane plane = Rhino.Geometry.Plane.Unset;
                        bool hasPlane = da.GetData(9, ref plane);
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
                        da.SetData(1, okMoveL ? "ok" : "MoveL failed");
                        break;

                    case URActionKind.StopJ:
                        double decel = 2.0;
                        da.GetData(5, ref decel);
                        var okStop = session.StopJ(decel);
                        da.SetData(0, okStop);
                        da.SetData(1, okStop ? "ok" : "Stop failed");
                        break;
                    case URActionKind.StopL:
                        double ldecel = 2.0;
                        da.GetData(5, ref ldecel);
                        var okStopL = session.StopL(ldecel);
                        da.SetData(0, okStopL);
                        da.SetData(1, okStopL ? "ok" : "StopL failed");
                        break;

                    case URActionKind.SetDO:
                        int pin = 0; bool val = false;
                        da.GetData(7, ref pin);
                        da.GetData(8, ref val);
                        var okDo = session.SetStandardDigitalOut(pin, val);
                        da.SetData(0, okDo);
                        da.SetData(1, okDo ? "ok" : "SetDO failed");
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

        protected override System.Drawing.Bitmap Icon => IconProvider.Command;
        public override Guid ComponentGuid => new Guid("2233737c-7ba5-4bf9-9c14-924c5d7077cd");

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Action: MoveJ", (s, e) => { _action = URActionKind.MoveJ; UpdateOptionalFlags(); ExpireSolution(true); }, true, _action == URActionKind.MoveJ);
            Menu_AppendItem(menu, "Action: MoveL", (s, e) => { _action = URActionKind.MoveL; UpdateOptionalFlags(); ExpireSolution(true); }, true, _action == URActionKind.MoveL);
            Menu_AppendItem(menu, "Action: StopJ", (s, e) => { _action = URActionKind.StopJ; UpdateOptionalFlags(); ExpireSolution(true); }, true, _action == URActionKind.StopJ);
            Menu_AppendItem(menu, "Action: StopL", (s, e) => { _action = URActionKind.StopL; UpdateOptionalFlags(); ExpireSolution(true); }, true, _action == URActionKind.StopL);
            Menu_AppendItem(menu, "Action: SetDO", (s, e) => { _action = URActionKind.SetDO; UpdateOptionalFlags(); ExpireSolution(true); }, true, _action == URActionKind.SetDO);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            UpdateOptionalFlags();
        }

        private void UpdateOptionalFlags()
        {
            if (Params == null || Params.Input == null || Params.Input.Count < 10) return;
            for (int i = 1; i < Params.Input.Count; i++)
            {
                Params.Input[i].Optional = true;
            }

            switch (_action)
            {
                case URActionKind.MoveJ:
                    Params.Input[1].Optional = false;
                    break;
                case URActionKind.MoveL:
                    break;
                case URActionKind.StopJ:
                case URActionKind.StopL:
                    Params.Input[5].Optional = false;
                    break;
                case URActionKind.SetDO:
                    Params.Input[8].Optional = false;
                    Params.Input[9].Optional = false;
                    break;
            }
        }
    }
}


