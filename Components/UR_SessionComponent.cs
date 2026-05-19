using System;
using System.Threading;
using Grasshopper.Kernel;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System.Drawing;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.Geometry;
using Rhino.Display;

namespace UR.RTDE.Grasshopper
{
    public class UR_SessionComponent : GH_Component
    {
        private readonly object _sessionLock = new object();
        private int _connectBusy;
        private System.Threading.Timer _healthTimer;
        private int _healthCheckInFlight;
        private const int HealthCheckIntervalMs = 250;

        internal URSession _session;
        internal string _currentIp = string.Empty;
        internal int _lastTimeoutMs = 2000;
        internal bool AwaitingReconnect { get; private set; }

        public UR_SessionComponent()
          : base("UR Session", "URSession",
            "Create and manage a UR RTDE session (control + receive).",
            "UR", "RTDE")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

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
            lock (_sessionLock)
            {
                if (_session == null || !string.Equals(_currentIp, ip, StringComparison.Ordinal) || reconnect)
                {
                    _session?.Dispose();
                    _session = new URSession(ip);
                    _currentIp = ip ?? string.Empty;
                    AwaitingReconnect = false;
                    createdOrReconnected = true;
                }

                if (_session != null && _session.IsConnected)
                    EnforceOperatorRecoveryIfNeeded();

                bool isConnected = _session?.IsConnected ?? false;
                string status = createdOrReconnected ? "Session created" : "Session reused";
                if (!isConnected)
                {
                    var lastError = _session?.LastError ?? string.Empty;
                    status = string.IsNullOrWhiteSpace(lastError)
                        ? status + " (not connected)"
                        : "Disconnected — " + lastError;
                }

                da.SetData(0, _session != null ? new URSessionGoo(_session) : null);
                da.SetData(1, isConnected);
                da.SetData(2, status);
                da.SetData(3, _session?.LastError ?? string.Empty);

                if (!isConnected && !string.IsNullOrWhiteSpace(_session?.LastError))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _session.LastError);
            }
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
            StopHealthMonitor();
            lock (_sessionLock)
            {
                _session?.Dispose();
                _session = null;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (Locked || Hidden) return;

            bool isConnected = _session?.IsConnected ?? false;

            var origin = Point3d.Origin;
            var size = 6;
            var color = isConnected ? ComponentUiColors.Active : ComponentUiColors.Inactive;

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

        internal bool TryBeginConnectToggle()
        {
            return Interlocked.CompareExchange(ref _connectBusy, 1, 0) == 0;
        }

        internal void EndConnectToggle()
        {
            Interlocked.Exchange(ref _connectBusy, 0);
        }

        internal ConnectToggleResult ToggleConnection()
        {
            lock (_sessionLock)
            {
                var disconnect = _session?.IsConnected ?? false;
                if (disconnect)
                {
                    StopHealthMonitor();
                    AwaitingReconnect = false;
                    _session?.Dispose();
                    _session = null;
                    return ConnectToggleResult.Success();
                }

                var timeoutMs = _lastTimeoutMs;
                var ip = string.IsNullOrWhiteSpace(_currentIp) ? "127.0.0.1" : _currentIp;

                _session ??= new URSession(ip);
                if (_session.Connect(timeoutMs))
                {
                    AwaitingReconnect = false;
                    StartHealthMonitor();
                    return ConnectToggleResult.Success();
                }

                var message = string.IsNullOrWhiteSpace(_session.LastError) ? "Failed to connect" : _session.LastError;
                return ConnectToggleResult.Failure(message);
            }
        }

        private void StartHealthMonitor()
        {
            StopHealthMonitor();
            _healthTimer = new System.Threading.Timer(_ => OnHealthTimerTick(), null, HealthCheckIntervalMs, HealthCheckIntervalMs);
        }

        private void StopHealthMonitor()
        {
            _healthTimer?.Dispose();
            _healthTimer = null;
        }

        private void OnHealthTimerTick()
        {
            if (Interlocked.Exchange(ref _healthCheckInFlight, 1) == 1)
                return;

            try
            {
                string reason = null;
                var shouldDisconnect = false;

                lock (_sessionLock)
                {
                    if (_session != null && _session.IsConnected && _session.CheckOperatorRecoveryRequired(out reason))
                        shouldDisconnect = true;
                }

                if (!shouldDisconnect)
                    return;

                DisconnectForOperatorRecovery(reason);
            }
            finally
            {
                Interlocked.Exchange(ref _healthCheckInFlight, 0);
            }
        }

        private void EnforceOperatorRecoveryIfNeeded()
        {
            if (_session == null || !_session.IsConnected)
                return;

            if (!_session.CheckOperatorRecoveryRequired(out var reason))
                return;

            DisconnectForOperatorRecovery(reason);
        }

        private void DisconnectForOperatorRecovery(string reason)
        {
            lock (_sessionLock)
            {
                if (_session == null || !_session.IsConnected)
                    return;
                _session.ForceDisconnect(reason);
                AwaitingReconnect = true;
            }

            StopHealthMonitor();

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, reason);
                Attributes?.ExpireLayout();
                Attributes?.PerformLayout();
                OnDisplayExpired(true);
                ExpireSolution(true);
            }));
        }

        internal readonly struct ConnectToggleResult
        {
            private ConnectToggleResult(bool ok, string message)
            {
                Ok = ok;
                Message = message;
            }

            public bool Ok { get; }
            public string Message { get; }

            public static ConnectToggleResult Success() => new ConnectToggleResult(true, null);

            public static ConnectToggleResult Failure(string message) => new ConnectToggleResult(false, message);
        }
    }
}

