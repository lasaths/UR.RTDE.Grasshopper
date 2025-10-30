using System;
using Grasshopper.Kernel;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System.Drawing;
using Grasshopper.Kernel.Attributes;
using Rhino.Geometry;
using Rhino.Display;

namespace UR.RTDE.Grasshopper
{
    public class UR_SessionComponent : GH_Component
    {
        internal URSession _session;
        internal string _currentIp = string.Empty;
        internal int _lastTimeoutMs = 2000;

        public UR_SessionComponent()
          : base("UR Session", "URSession",
            "Create and manage a UR RTDE session (control + receive).",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("IP", "I", "Robot IP address. Defaults to 127.0.0.1 (URSim)", GH_ParamAccess.item);
            p.AddIntegerParameter("Timeout (ms)", "T", "Optional connect timeout (ms).", GH_ParamAccess.item, 2000);
            p.AddBooleanParameter("Reconnect", "R", "Force reconnect on this solve.", GH_ParamAccess.item, false);
            p[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
            p.AddBooleanParameter("Connected", "O", "True if session is connected.", GH_ParamAccess.item);
            p.AddTextParameter("Status", "M", "Session status.", GH_ParamAccess.item);
            p.AddTextParameter("Last Error", "E", "Last error message if any.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string ip = "127.0.0.1";
            int timeoutMs = 2000;
            bool reconnect = false;

            da.GetData(0, ref ip);
            da.GetData(1, ref timeoutMs);
            da.GetData(2, ref reconnect);

            _lastTimeoutMs = timeoutMs;

            bool createdOrReconnected = false;

            if (_session == null || !string.Equals(_currentIp, ip, StringComparison.Ordinal) || reconnect)
            {
                _session?.Dispose();
                _session = new URSession(ip);
                _currentIp = ip ?? string.Empty;
                createdOrReconnected = true;
            }

            bool isConnected = _session?.IsConnected ?? false;
            string status = createdOrReconnected ? "Session created" : "Session reused";
            if (!isConnected)
            {
                status += " (not connected)";
            }

            da.SetData(0, _session != null ? new URSessionGoo(_session) : null);
            da.SetData(1, isConnected);
            da.SetData(2, status);
            da.SetData(3, _session?.LastError ?? string.Empty);
        }

        public override void CreateAttributes()
        {
            m_attributes = new UR_SessionAttributes(this);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.plugs-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("e5d931e9-3d07-4925-9f5e-7bfab15dfd91");

        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);
            _session?.Dispose();
            _session = null;
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Locked || Hidden) return;

            bool isConnected = _session?.IsConnected ?? false;

            var origin = Point3d.Origin;
            var size = 6;
            var color = isConnected ? Color.FromArgb(0x10, 0xB9, 0x81) : Color.FromArgb(120, 120, 120);

            args.Display.DrawPoint(origin, PointStyle.RoundSimple, size, color);

            if (isConnected)
            {
                var text = new Text3d($"UR {(_currentIp ?? "")} connected", new Plane(origin + new Vector3d(0, 0, 50), Vector3d.ZAxis), 8);
                args.Display.Draw3dText(text, color);
                text.Dispose();
            }
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                var box = new BoundingBox(new Point3d(-100, -100, -100), new Point3d(100, 100, 100));
                return box;
            }
        }
    }
}


