using System;
using Grasshopper.Kernel;

namespace UR.RTDE.Grasshopper
{
    // Session manager component: creates/maintains a UR RTDE session
    public class UR_SessionComponent : GH_Component
    {
        private URSession _session;
        private string _currentIp = string.Empty;

        public UR_SessionComponent()
          : base("UR Session", "URSession",
            "Create and manage a UR RTDE session (control + receive).",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("IP", "I", "Robot IP address.", GH_ParamAccess.item);
            p.AddBooleanParameter("Auto Connect", "X", "Connect automatically when IP changes.", GH_ParamAccess.item, true);
            p.AddIntegerParameter("Timeout (ms)", "T", "Optional connect timeout (ms).", GH_ParamAccess.item, 2000);
            p.AddBooleanParameter("Reconnect", "R", "Force reconnect on this solve.", GH_ParamAccess.item, false);
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
            string ip = string.Empty;
            bool auto = true;
            int timeoutMs = 2000;
            bool reconnect = false;

            if (!da.GetData(0, ref ip)) return;
            da.GetData(1, ref auto);
            da.GetData(2, ref timeoutMs);
            da.GetData(3, ref reconnect);

            bool createdOrReconnected = false;

            if (_session == null || !string.Equals(_currentIp, ip, StringComparison.Ordinal) || reconnect)
            {
                _session?.Dispose();
                _session = new URSession(ip);
                _currentIp = ip ?? string.Empty;
                createdOrReconnected = true;
            }

            string status = createdOrReconnected ? "Session created" : "Session reused";
            if (auto && !string.IsNullOrWhiteSpace(ip))
            {
                if (!_session.IsConnected)
                {
                    if (_session.Connect(timeoutMs))
                        status = "Connected";
                    else
                        status = "Connect failed";
                }
            }

            da.SetData(0, new URSessionGoo(_session));
            da.SetData(1, _session.IsConnected);
            da.SetData(2, status);
            da.SetData(3, _session.LastError ?? string.Empty);
        }

        protected override System.Drawing.Bitmap Icon => IconProvider.Session;

        public override Guid ComponentGuid => new Guid("e5d931e9-3d07-4925-9f5e-7bfab15dfd91");

        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);
            _session?.Dispose();
            _session = null;
        }
    }
}


