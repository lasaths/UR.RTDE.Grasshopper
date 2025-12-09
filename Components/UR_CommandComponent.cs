using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using GrasshopperAsyncComponent;

namespace UR.RTDE.Grasshopper
{
    public enum URActionKind { MoveJ, MoveL, StopJ, StopL, SetDO }

    internal class TargetData
    {
        public double[] Joints { get; set; }
        public double[] Pose { get; set; }
        public double Speed { get; set; }
        public double Acceleration { get; set; }
        public bool Async { get; set; }
        public GH_Path Path { get; set; }
    }

    public class UR_CommandComponent : GH_AsyncComponent
    {
        private URActionKind _action = URActionKind.MoveJ;
        private bool _sequential = false;
        private bool _stopOnError = true;

        public UR_CommandComponent()
          : base("UR Command", "URCmd",
            "Execute commands on the robot via RTDE (select action in the menu).",
            "UR", "RTDE")
        {
            BaseWorker = new URCommandWorker(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if command succeeded. Tree output in sequential mode.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error. Tree output in sequential mode.", GH_ParamAccess.item);
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

            // Read sequential mode flag and stop-on-error
            bool sequential = false;
            bool stopOnError = true;
            
            for (int i = 0; i < Params.Input.Count; i++)
            {
                var param = Params.Input[i];
                if (param != null)
                {
                    if (param.Name == "Sequential")
                        da.GetData(i, ref sequential);
                    else if (param.Name == "Stop on Error")
                        da.GetData(i, ref stopOnError);
                }
            }
            
            _sequential = sequential;
            _stopOnError = stopOnError;
            
            // Adjust sequential flag based on action type
            if (sequential && _action != URActionKind.MoveJ && _action != URActionKind.MoveL)
            {
                sequential = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Sequential mode only supported for MoveJ and MoveL");
            }

            try
            {
                if (sequential)
                {
                    // Prepare data for async worker
                    var worker = BaseWorker as URCommandWorker;
                    if (worker != null)
                    {
                        worker.PrepareWork(da, session, _action, _stopOnError);
                    }
                    // The worker will execute asynchronously
                }
                else
                {
                    // Execute single command synchronously (fast, no need for async)
                    ExecuteSingle(da, session);
                }
            }
            catch (Exception ex)
            {
                da.SetData(0, false);
                da.SetData(1, ex.Message);
            }
        }

        private void ExecuteSingle(IGH_DataAccess da, URSession session)
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

        private List<TargetData> ExtractMoveJTargets(IGH_DataAccess da)
        {
            var targets = new List<TargetData>();
            var jointsTree = new GH_Structure<GH_Number>();
            
            // Try to get as tree first, then fall back to list
            if (da.GetDataTree(1, out jointsTree))
            {
                // Extract from tree
                foreach (var path in jointsTree.Paths)
                {
                    var branch = jointsTree.get_Branch(path);
                    var numbers = branch?.OfType<GH_Number>().ToList();
                    if (numbers != null && numbers.Count == 6)
                    {
                        var joints = numbers.Select(x => x.Value).ToArray();
                        var speed = GetValueFromTree<double>(da, 2, path, 1.05);
                        var accel = GetValueFromTree<double>(da, 3, path, 1.4);
                        var async = GetValueFromTree<bool>(da, 4, path, false);
                        
                        targets.Add(new TargetData
                        {
                            Joints = joints,
                            Speed = speed,
                            Acceleration = accel,
                            Async = async,
                            Path = path
                        });
                    }
                }
            }
            else
            {
                // Fall back to list extraction
                var jointsList = new List<double>();
                if (da.GetDataList(1, jointsList) && jointsList.Count == 6)
                {
                    double speed = 1.05, accel = 1.4;
                    bool async = false;
                    da.GetData(2, ref speed);
                    da.GetData(3, ref accel);
                    da.GetData(4, ref async);
                    
                    targets.Add(new TargetData
                    {
                        Joints = jointsList.ToArray(),
                        Speed = speed,
                        Acceleration = accel,
                        Async = async,
                        Path = new GH_Path(0)
                    });
                }
            }

            return targets;
        }

        private List<TargetData> ExtractMoveLTargets(IGH_DataAccess da)
        {
            var targets = new List<TargetData>();
            
            // Try planes first (as tree)
            var planesTree = new GH_Structure<GH_Plane>();
            bool hasPlanes = da.GetDataTree(7, out planesTree);
            
            if (hasPlanes && planesTree.PathCount > 0)
            {
                // Extract from plane tree
                foreach (var path in planesTree.Paths)
                {
                    var branch = planesTree.get_Branch(path)?.OfType<GH_Plane>().ToList();
                    if (branch != null)
                    {
                        foreach (var planeGoo in branch)
                        {
                            var plane = planeGoo.Value;
                            if (plane.IsValid)
                            {
                                var pose = PoseUtils.PlaneToPose(plane);
                                var speed = GetValueFromTree<double>(da, 2, path, 0.25);
                                var accel = GetValueFromTree<double>(da, 3, path, 1.2);
                                var async = GetValueFromTree<bool>(da, 4, path, false);
                                
                                targets.Add(new TargetData
                                {
                                    Pose = pose,
                                    Speed = speed,
                                    Acceleration = accel,
                                    Async = async,
                                    Path = path
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                // Try pose tree
                var poseTree = new GH_Structure<GH_Number>();
                if (da.GetDataTree(6, out poseTree))
                {
                    foreach (var path in poseTree.Paths)
                    {
                        var branch = poseTree.get_Branch(path)?.OfType<GH_Number>().ToList();
                        if (branch != null && branch.Count == 6)
                        {
                            var pose = branch.Select(x => x.Value).ToArray();
                            var speed = GetValueFromTree<double>(da, 2, path, 0.25);
                            var accel = GetValueFromTree<double>(da, 3, path, 1.2);
                            var async = GetValueFromTree<bool>(da, 4, path, false);
                            
                            targets.Add(new TargetData
                            {
                                Pose = pose,
                                Speed = speed,
                                Acceleration = accel,
                                Async = async,
                                Path = path
                            });
                        }
                    }
                }
                else
                {
                    // Fall back to single plane or pose
                    Rhino.Geometry.Plane plane = Rhino.Geometry.Plane.Unset;
                    bool hasPlane = da.GetData(7, ref plane);
                    
                    if (hasPlane && plane.IsValid)
                    {
                        var pose = PoseUtils.PlaneToPose(plane);
                        double speed = 0.25, accel = 1.2;
                        bool async = false;
                        da.GetData(2, ref speed);
                        da.GetData(3, ref accel);
                        da.GetData(4, ref async);
                        
                        targets.Add(new TargetData
                        {
                            Pose = pose,
                            Speed = speed,
                            Acceleration = accel,
                            Async = async,
                            Path = new GH_Path(0)
                        });
                    }
                    else
                    {
                        var poseList = new List<double>();
                        if (da.GetDataList(6, poseList) && poseList.Count == 6)
                        {
                            double speed = 0.25, accel = 1.2;
                            bool async = false;
                            da.GetData(2, ref speed);
                            da.GetData(3, ref accel);
                            da.GetData(4, ref async);
                            
                            targets.Add(new TargetData
                            {
                                Pose = poseList.ToArray(),
                                Speed = speed,
                                Acceleration = accel,
                                Async = async,
                                Path = new GH_Path(0)
                            });
                        }
                    }
                }
            }

            return targets;
        }

        private T GetValueFromTree<T>(IGH_DataAccess da, int paramIndex, GH_Path path, T defaultValue)
        {
            if (paramIndex >= Params.Input.Count) return defaultValue;
            
            var param = Params.Input[paramIndex];
            if (param == null) return defaultValue;

            try
            {
                var tree = new GH_Structure<IGH_Goo>();
                if (da.GetDataTree(paramIndex, out tree) && tree.PathCount > 0)
                {
                    // Try to get value from the same path
                    var branch = tree.get_Branch(path);
                    if (branch != null && branch.Count > 0)
                    {
                        var value = branch[0];
                        if (value is GH_Number num && typeof(T) == typeof(double))
                            return (T)(object)num.Value;
                        if (value is GH_Boolean b && typeof(T) == typeof(bool))
                            return (T)(object)b.Value;
                    }
                    
                    // If path doesn't exist, try first available path
                    if (tree.Paths.Count > 0)
                    {
                        var firstPath = tree.Paths[0];
                        branch = tree.get_Branch(firstPath);
                        if (branch != null && branch.Count > 0)
                        {
                            var value = branch[0];
                            if (value is GH_Number num && typeof(T) == typeof(double))
                                return (T)(object)num.Value;
                            if (value is GH_Boolean b && typeof(T) == typeof(bool))
                                return (T)(object)b.Value;
                        }
                    }
                }
                
                // Fall back to single value
                if (typeof(T) == typeof(double))
                {
                    double val = (double)(object)defaultValue;
                    if (da.GetData(paramIndex, ref val))
                        return (T)(object)val;
                }
                if (typeof(T) == typeof(bool))
                {
                    bool val = (bool)(object)defaultValue;
                    if (da.GetData(paramIndex, ref val))
                        return (T)(object)val;
                }
            }
            catch { }

            return defaultValue;
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.rocket-launch-duotone.png";
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
                    Params.RegisterInputParam(Num("Joints", "Q", "Joint target angles (rad). Tree/list for sequential mode.", GH_ParamAccess.tree, null, false));
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed. Tree/list for sequential mode.", GH_ParamAccess.tree, 1.05));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration. Tree/list for sequential mode.", GH_ParamAccess.tree, 1.4));
                    Params.RegisterInputParam(Bool("Async", "X", "Run asynchronously (non-blocking). Tree/list for sequential mode.", false));
                    Params.RegisterInputParam(Bool("Sequential", "Seq", "Execute sequentially (blocks UI, max 20 commands). Only for MoveJ/MoveL.", false));
                    Params.RegisterInputParam(Bool("Stop on Error", "Stop", "Stop sequence on first error (sequential mode only)", true));
                    break;

                case URActionKind.MoveL:
                    var pose = Num("Pose", "P", "TCP pose [x,y,z,rx,ry,rz] (m,rad). Tree/list for sequential mode.", GH_ParamAccess.tree);
                    pose.Optional = true;
                    Params.RegisterInputParam(pose);
                    Params.RegisterInputParam(new Param_Plane { Name = "Target", NickName = "T", Description = "Target Plane (alternative to Pose). Tree/list for sequential mode.", Optional = true });
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed. Tree/list for sequential mode.", GH_ParamAccess.tree, 0.25));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration. Tree/list for sequential mode.", GH_ParamAccess.tree, 1.2));
                    Params.RegisterInputParam(Bool("Async", "X", "Run asynchronously (non-blocking). Tree/list for sequential mode.", false));
                    Params.RegisterInputParam(Bool("Sequential", "Seq", "Execute sequentially (blocks UI, max 20 commands). Only for MoveJ/MoveL.", false));
                    Params.RegisterInputParam(Bool("Stop on Error", "Stop", "Stop sequence on first error (sequential mode only)", true));
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

    // Worker class for async sequential execution
    public class URCommandWorker : WorkerInstance
    {
        private IGH_DataAccess _da;
        private URSession _session;
        private URActionKind _action;
        private bool _stopOnError;
        private List<TargetData> _targets;
        private GH_Structure<GH_Boolean> _results;
        private GH_Structure<GH_String> _messages;

        public URCommandWorker(GH_Component parent) : base(parent)
        {
        }

        public void PrepareWork(IGH_DataAccess da, URSession session, URActionKind action, bool stopOnError)
        {
            _da = da;
            _session = session;
            _action = action;
            _stopOnError = stopOnError;
        }

        public override void DoWork(Action<string, double> reportProgress, Action done)
        {
            if (_session == null || !_session.IsConnected)
            {
                _results = new GH_Structure<GH_Boolean>();
                _messages = new GH_Structure<GH_String>();
                _results.Append(new GH_Boolean(false), new GH_Path(0));
                _messages.Append(new GH_String("Session not connected"), new GH_Path(0));
                done();
                return;
            }

            const int MAX_COMMANDS = 20;
            _results = new GH_Structure<GH_Boolean>();
            _messages = new GH_Structure<GH_String>();

            try
            {
                // Extract targets based on action
                if (_action == URActionKind.MoveJ)
                {
                    _targets = ExtractMoveJTargets(_da);
                }
                else if (_action == URActionKind.MoveL)
                {
                    _targets = ExtractMoveLTargets(_da);
                }
                else
                {
                    _results.Append(new GH_Boolean(false), new GH_Path(0));
                    _messages.Append(new GH_String("Sequential mode only supported for MoveJ and MoveL"), new GH_Path(0));
                    done();
                    return;
                }

                if (_targets == null || _targets.Count == 0)
                {
                    _results.Append(new GH_Boolean(false), new GH_Path(0));
                    _messages.Append(new GH_String("No valid targets found"), new GH_Path(0));
                    done();
                    return;
                }

                if (_targets.Count > MAX_COMMANDS)
                {
                    _targets = _targets.Take(MAX_COMMANDS).ToList();
                }

                // Execute sequentially
                for (int i = 0; i < _targets.Count; i++)
                {
                    // Check for cancellation via parent component (best-effort via reflection)
                    if (Parent is GH_AsyncComponent asyncComp)
                    {
                        try
                        {
                            var tokenProp = asyncComp.GetType().GetProperty("CancellationToken");
                            var token = tokenProp?.GetValue(asyncComp);
                            var isCancelledProp = token?.GetType().GetProperty("IsCancellationRequested");
                            if (isCancelledProp != null && isCancelledProp.GetValue(token) is bool cancelled && cancelled)
                                break;
                        }
                        catch { }
                    }

                    var target = _targets[i];
                    bool success = false;
                    string message = "";

                    try
                    {
                        reportProgress($"Executing {i + 1}/{_targets.Count}", (double)i / _targets.Count);

                        if (_action == URActionKind.MoveJ)
                        {
                            success = _session.MoveJ(
                                target.Joints,
                                target.Speed,
                                target.Acceleration,
                                target.Async);
                            message = success
                                ? $"MoveJ {i + 1}/{_targets.Count}: OK"
                                : $"MoveJ {i + 1}/{_targets.Count}: Failed - {_session.LastError ?? "Unknown error"}";
                        }
                        else if (_action == URActionKind.MoveL)
                        {
                            success = _session.MoveL(
                                target.Pose,
                                target.Speed,
                                target.Acceleration,
                                target.Async);
                            message = success
                                ? $"MoveL {i + 1}/{_targets.Count}: OK"
                                : $"MoveL {i + 1}/{_targets.Count}: Failed - {_session.LastError ?? "Unknown error"}";
                        }
                    }
                    catch (Exception ex)
                    {
                        message = $"{_action} {i + 1}/{_targets.Count}: Exception - {ex.Message}";
                    }

                    _results.Append(new GH_Boolean(success), target.Path);
                    _messages.Append(new GH_String(message), target.Path);

                    if (!success && _stopOnError)
                    {
                        // Add remaining targets as failed
                        for (int j = i + 1; j < _targets.Count; j++)
                        {
                            _results.Append(new GH_Boolean(false), _targets[j].Path);
                            _messages.Append(new GH_String($"Skipped due to error at index {i + 1}"), _targets[j].Path);
                        }
                        break;
                    }
                }

                reportProgress("Complete", 1.0);
            }
            catch (Exception ex)
            {
                _results.Append(new GH_Boolean(false), new GH_Path(0));
                _messages.Append(new GH_String(ex.Message), new GH_Path(0));
            }

            done();
        }

        public override WorkerInstance Duplicate()
        {
            return new URCommandWorker(Parent);
        }

        public override void SetData(IGH_DataAccess da)
        {
            if (_results != null && _messages != null)
            {
                da.SetDataTree(0, _results);
                da.SetDataTree(1, _messages);
            }
        }

        public override void GetData(IGH_DataAccess da, GH_ComponentParamServer paramServer)
        {
            // Data is set via SetData, this method may not be needed
        }

        // Helper methods (same as in component)
        private List<TargetData> ExtractMoveJTargets(IGH_DataAccess da)
        {
            var targets = new List<TargetData>();
            var jointsTree = new GH_Structure<GH_Number>();
            
            if (da.GetDataTree(1, out jointsTree))
            {
                foreach (var path in jointsTree.Paths)
                {
                    var branch = jointsTree.get_Branch(path)?.OfType<GH_Number>().ToList();
                    if (branch != null && branch.Count == 6)
                    {
                        var joints = branch.Select(x => x.Value).ToArray();
                        var speed = GetValueFromTree<double>(da, 2, path, 1.05);
                        var accel = GetValueFromTree<double>(da, 3, path, 1.4);
                        var async = GetValueFromTree<bool>(da, 4, path, false);
                        
                        targets.Add(new TargetData
                        {
                            Joints = joints,
                            Speed = speed,
                            Acceleration = accel,
                            Async = async,
                            Path = path
                        });
                    }
                }
            }
            else
            {
                var jointsList = new List<double>();
                if (da.GetDataList(1, jointsList) && jointsList.Count == 6)
                {
                    double speed = 1.05, accel = 1.4;
                    bool async = false;
                    da.GetData(2, ref speed);
                    da.GetData(3, ref accel);
                    da.GetData(4, ref async);
                    
                    targets.Add(new TargetData
                    {
                        Joints = jointsList.ToArray(),
                        Speed = speed,
                        Acceleration = accel,
                        Async = async,
                        Path = new GH_Path(0)
                    });
                }
            }

            return targets;
        }

        private List<TargetData> ExtractMoveLTargets(IGH_DataAccess da)
        {
            var targets = new List<TargetData>();
            
            var planesTree = new GH_Structure<GH_Plane>();
            bool hasPlanes = da.GetDataTree(7, out planesTree);
            
            if (hasPlanes && planesTree.PathCount > 0)
            {
                foreach (var path in planesTree.Paths)
                {
                    var branch = planesTree.get_Branch(path)?.OfType<GH_Plane>().ToList();
                    if (branch != null)
                    {
                        foreach (var planeGoo in branch)
                        {
                            var plane = planeGoo.Value;
                            if (plane.IsValid)
                            {
                                var pose = PoseUtils.PlaneToPose(plane);
                                var speed = GetValueFromTree<double>(da, 2, path, 0.25);
                                var accel = GetValueFromTree<double>(da, 3, path, 1.2);
                                var async = GetValueFromTree<bool>(da, 4, path, false);
                                
                                targets.Add(new TargetData
                                {
                                    Pose = pose,
                                    Speed = speed,
                                    Acceleration = accel,
                                    Async = async,
                                    Path = path
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                var poseTree = new GH_Structure<GH_Number>();
                if (da.GetDataTree(6, out poseTree))
                {
                    foreach (var path in poseTree.Paths)
                    {
                        var branch = poseTree.get_Branch(path)?.OfType<GH_Number>().ToList();
                        if (branch != null && branch.Count == 6)
                        {
                            var pose = branch.Select(x => x.Value).ToArray();
                            var speed = GetValueFromTree<double>(da, 2, path, 0.25);
                            var accel = GetValueFromTree<double>(da, 3, path, 1.2);
                            var async = GetValueFromTree<bool>(da, 4, path, false);
                            
                            targets.Add(new TargetData
                            {
                                Pose = pose,
                                Speed = speed,
                                Acceleration = accel,
                                Async = async,
                                Path = path
                            });
                        }
                    }
                }
                else
                {
                    Rhino.Geometry.Plane plane = Rhino.Geometry.Plane.Unset;
                    bool hasPlane = da.GetData(7, ref plane);
                    
                    if (hasPlane && plane.IsValid)
                    {
                        var pose = PoseUtils.PlaneToPose(plane);
                        double speed = 0.25, accel = 1.2;
                        bool async = false;
                        da.GetData(2, ref speed);
                        da.GetData(3, ref accel);
                        da.GetData(4, ref async);
                        
                        targets.Add(new TargetData
                        {
                            Pose = pose,
                            Speed = speed,
                            Acceleration = accel,
                            Async = async,
                            Path = new GH_Path(0)
                        });
                    }
                    else
                    {
                        var poseList = new List<double>();
                        if (da.GetDataList(6, poseList) && poseList.Count == 6)
                        {
                            double speed = 0.25, accel = 1.2;
                            bool async = false;
                            da.GetData(2, ref speed);
                            da.GetData(3, ref accel);
                            da.GetData(4, ref async);
                            
                            targets.Add(new TargetData
                            {
                                Pose = poseList.ToArray(),
                                Speed = speed,
                                Acceleration = accel,
                                Async = async,
                                Path = new GH_Path(0)
                            });
                        }
                    }
                }
            }

            return targets;
        }

        private T GetValueFromTree<T>(IGH_DataAccess da, int paramIndex, GH_Path path, T defaultValue)
        {
            if (paramIndex >= Parent.Params.Input.Count) return defaultValue;
            
            var param = Parent.Params.Input[paramIndex];
            if (param == null) return defaultValue;

            try
            {
                var tree = new GH_Structure<IGH_Goo>();
                if (da.GetDataTree(paramIndex, out tree) && tree.PathCount > 0)
                {
                    var branch = tree.get_Branch(path);
                    if (branch != null && branch.Count > 0)
                    {
                        var value = branch[0];
                        if (value is GH_Number num && typeof(T) == typeof(double))
                            return (T)(object)num.Value;
                        if (value is GH_Boolean b && typeof(T) == typeof(bool))
                            return (T)(object)b.Value;
                    }
                    
                    if (tree.Paths.Count > 0)
                    {
                        var firstPath = tree.Paths[0];
                        branch = tree.get_Branch(firstPath);
                        if (branch != null && branch.Count > 0)
                        {
                            var value = branch[0];
                            if (value is GH_Number num && typeof(T) == typeof(double))
                                return (T)(object)num.Value;
                            if (value is GH_Boolean b && typeof(T) == typeof(bool))
                                return (T)(object)b.Value;
                        }
                    }
                }
                
                if (typeof(T) == typeof(double))
                {
                    double val = (double)(object)defaultValue;
                    if (da.GetData(paramIndex, ref val))
                        return (T)(object)val;
                }
                if (typeof(T) == typeof(bool))
                {
                    bool val = (bool)(object)defaultValue;
                    if (da.GetData(paramIndex, ref val))
                        return (T)(object)val;
                }
            }
            catch { }

            return defaultValue;
        }
    }
}
