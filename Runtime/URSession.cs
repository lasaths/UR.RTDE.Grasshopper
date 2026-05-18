using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UR.RTDE;

namespace UR.RTDE.Grasshopper
{
    public enum RobotiqBackend
    {
        Native,
        RtdeBridge,
        UrScript
    }

    public enum URAnalogOutputMode
    {
        Voltage,
        Current
    }

    public sealed class URSession : IDisposable
    {
        private const int DefaultRobotiqNativePort = 63352;
        private const int DefaultRobotiqScriptPort = 30002;
        private const ushort ControlFlagsUploadScript = 1;
        private const ushort ControlFlagsDisableRemoteControlCheck = 1 << 7;
        private const int RtdeControlPort = 30003;
        private const int ExternalControlPort = 50002;
        private static int _resolverRegistered;

        private readonly object _lockObj = new object();
        private UR.RTDE.RTDEControl _control;
        private UR.RTDE.RTDEReceive _receive;
        private RTDEIO _io;
        private volatile bool _isConnected;
        private int _streamSendSuppressCount;

        public string Ip { get; }
        public bool IsStreamSendSuppressed => Volatile.Read(ref _streamSendSuppressCount) > 0;
        public bool IsConnected => _isConnected;
        public string LastError { get; private set; }

        static URSession()
        {
            NativeBootstrap.EnsureLoaded();
            RegisterAssemblyResolver();
        }

        public URSession(string ip)
        {
            Ip = ip ?? string.Empty;
        }

        public void PushStreamSendSuppress() => Interlocked.Increment(ref _streamSendSuppressCount);

        public void PopStreamSendSuppress()
        {
            if (Interlocked.Decrement(ref _streamSendSuppressCount) < 0)
                Interlocked.Exchange(ref _streamSendSuppressCount, 0);
        }

        public bool Connect(int timeoutMs = 2000)
        {
            lock (_lockObj)
            {
                try
                {
                    DisposeClientsInternal();
                    _receive = new UR.RTDE.RTDEReceive(Ip);
                    _control = CreateControlWithFallback();
                    _isConnected = true;
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    _isConnected = false;
                    DisposeClientsInternal();
                    return false;
                }
            }
        }

        private RTDEControl CreateControlWithFallback()
        {
            var attempts = new List<(string Label, ushort Flags)>
            {
                ("default (upload external-control script)", ControlFlagsUploadScript),
                ("disable remote-control check", ControlFlagsDisableRemoteControlCheck),
                ("no flags", 0),
                ("upload script + disable remote-control check", (ushort)(ControlFlagsUploadScript | ControlFlagsDisableRemoteControlCheck)),
            };

            Exception? lastEx = null;
            foreach (var (label, flags) in attempts)
            {
                try
                {
                    return new UR.RTDE.RTDEControl(Ip, flags: flags);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            throw new InvalidOperationException(BuildControlConnectionErrorMessage(attempts, lastEx), lastEx);
        }

        private string BuildControlConnectionErrorMessage(
            IReadOnlyList<(string Label, ushort Flags)> attempts,
            Exception? lastEx)
        {
            var sb = new StringBuilder();
            sb.Append("RTDEControl could not connect to ").Append(Ip).Append('.');
            if (lastEx != null)
                sb.Append(' ').Append(lastEx.Message);

            sb.Append(" Tried: ");
            for (var i = 0; i < attempts.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(attempts[i].Label);
            }
            sb.Append('.');

            var portHint = DescribeControlPortReachability(Ip);
            if (!string.IsNullOrWhiteSpace(portHint))
                sb.Append(' ').Append(portHint);

            return sb.ToString();
        }

        private static string DescribeControlPortReachability(string host)
        {
            bool rtdeControlOpen = IsTcpPortOpen(host, RtdeControlPort, 400);
            bool externalControlOpen = IsTcpPortOpen(host, ExternalControlPort, 400);

            if (rtdeControlOpen && externalControlOpen)
                return "Ports 30003 and 50002 are reachable; on URSim, start External Control and use Remote Control if required.";

            if (!rtdeControlOpen)
            {
                return "Port 30003 (RTDE control) is not reachable from this machine. "
                       + "URSim Docker must publish it, e.g. `-p 30001:30001 -p 30003:30003 -p 30004:30004` "
                       + "(receive-only on 30004 is not enough for MoveJ and other control commands).";
            }

            return "Port 30003 is open but 50002 (external control URCap) is not; control may still work with the "
                   + "\"disable remote-control check\" flag on URSim in Local Control.";
        }

        private static bool IsTcpPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(host, port);
                if (!connect.Wait(Math.Max(1, timeoutMs)))
                    return false;
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        public double[] GetActualQ()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetActualQ();
            }
        }

        public double[] GetActualTCPPose()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return InvokeReceive<double[]>(new[] { "GetActualTCPPose", "GetActualTcpPose", "GetActualToolPose" });
            }
        }

        public bool MoveJ(double[] q, double speed, double acceleration, bool asynchronous)
        {
            if (q == null || q.Length != 6) throw new ArgumentException("q must be length 6", nameof(q));

            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                EndStreamingMotionBeforePathMove();
                return InvokeControlBool("MoveJ", new object[] { q, speed, acceleration, asynchronous });
            }
        }

        public bool StopJ(double deceleration)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlBool("StopJ", new object[] { deceleration, true });
            }
        }

        public bool StopL(double deceleration)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlBool("StopL", new object[] { deceleration, true });
            }
        }

        public bool MoveL(double[] pose, double speed, double acceleration, bool asynchronous)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6", nameof(pose));

            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                EndStreamingMotionBeforePathMove();
                return InvokeControlBool("MoveL", new object[] { pose, speed, acceleration, asynchronous });
            }
        }

        public bool SetStandardDigitalOut(int pin, bool value)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                if (!EnsureIo()) return false;
                try
                {
                    _io.SetStandardDigitalOut(pin, value);
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.InnerException?.Message ?? ex.Message;
                    return false;
                }
            }
        }

        public int GetDigitalInState()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<int>(new[] { "GetDigitalInState", "GetActualDigitalInputBits" });
                }
                catch (MissingMethodException)
                {
                    return BuildDigitalBits("GetStandardDigitalIn");
                }
            }
        }

        public int GetDigitalOutState()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<int>(new[] { "GetDigitalOutState", "GetActualDigitalOutputBits" });
                }
                catch (MissingMethodException)
                {
                    return BuildDigitalBits("GetStandardDigitalOut");
                }
            }
        }

        public double GetStandardAnalogInput0()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<double>(new[] { "GetStandardAnalogInput0", "GetActualStandardAnalogInput0" });
                }
                catch (MissingMethodException)
                {
                    return InvokeReceiveIndexedDouble("GetStandardAnalogInput", 0);
                }
            }
        }

        public double GetStandardAnalogInput1()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<double>(new[] { "GetStandardAnalogInput1", "GetActualStandardAnalogInput1" });
                }
                catch (MissingMethodException)
                {
                    return InvokeReceiveIndexedDouble("GetStandardAnalogInput", 1);
                }
            }
        }

        public double GetStandardAnalogOutput0()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<double>(new[] { "GetStandardAnalogOutput0", "GetActualStandardAnalogOutput0" });
                }
                catch (MissingMethodException)
                {
                    return InvokeReceiveIndexedDouble("GetStandardAnalogOutput", 0);
                }
            }
        }

        public double GetStandardAnalogOutput1()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<double>(new[] { "GetStandardAnalogOutput1", "GetActualStandardAnalogOutput1" });
                }
                catch (MissingMethodException)
                {
                    return InvokeReceiveIndexedDouble("GetStandardAnalogOutput", 1);
                }
            }
        }

        public int GetRobotMode()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return InvokeReceive<int>(new[] { "GetRobotMode" });
            }
        }

        public int GetSafetyMode()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return InvokeReceive<int>(new[] { "GetSafetyMode" });
            }
        }

        public bool IsProgramRunning()
        {
            lock (_lockObj)
            {
                RequireReceive();
                try
                {
                    return InvokeReceive<bool>(new[] { "IsProgramRunning", "GetProgramRunning" });
                }
                catch (MissingMethodException)
                {
                    return InvokeReceive<int>(new[] { "GetRuntimeState" }) != 0;
                }
            }
        }

        public double[] GetTargetQ()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetTargetQ();
            }
        }

        public double[] GetTargetTcpPose()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetTargetTcpPose();
            }
        }

        public double[] GetActualQd()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetActualQd();
            }
        }

        public double[] GetActualTcpSpeed()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetActualTcpSpeed();
            }
        }

        public double[] GetActualTcpForce()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetActualTcpForce();
            }
        }

        public uint GetRobotStatus()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetRobotStatus();
            }
        }

        public int GetRuntimeState()
        {
            lock (_lockObj)
            {
                RequireReceive();
                return _receive.GetRuntimeState();
            }
        }

        public bool IsSteady()
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return _control.IsSteady;
            }
        }

        public bool WaitForMotionComplete(int timeoutMs = 30000)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs));
            while (DateTime.UtcNow < deadline)
            {
                lock (_lockObj)
                {
                    if (_control == null) throw new InvalidOperationException("Not connected");
                    if (_control.IsSteady) return true;
                    if (_control.WaitForNextState()) return true;
                }
                Thread.Sleep(10);
            }

            LastError = $"Motion did not complete within {timeoutMs} ms";
            return false;
        }

        public bool SpeedJ(double[] qd, double acceleration, double time)
        {
            if (qd == null || qd.Length != 6) throw new ArgumentException("qd must be length 6", nameof(qd));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("SpeedJ", new object[] { qd, acceleration, time });
            }
        }

        public bool ServoJ(double[] q, double speed, double acceleration, double time, double lookaheadTime, double gain)
        {
            if (q == null || q.Length != 6) throw new ArgumentException("q must be length 6", nameof(q));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("ServoJ", new object[] { q, speed, acceleration, time, lookaheadTime, gain });
            }
        }

        public bool SpeedStop(double acceleration = 10.0)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("SpeedStop", new object[] { acceleration });
            }
        }

        public bool ServoStop(double acceleration = 0.5)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("ServoStop", new object[] { acceleration });
            }
        }

        public bool SetAnalogOutput(int index, double value, URAnalogOutputMode mode)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                if (!EnsureIo()) return false;
                try
                {
                    if (mode == URAnalogOutputMode.Current)
                        _io.SetAnalogOutputCurrent(index, value);
                    else
                        _io.SetAnalogOutputVoltage(index, value);
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.InnerException?.Message ?? ex.Message;
                    return false;
                }
            }
        }

        public bool SetToolDigitalOut(int pin, bool value)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                if (!EnsureIo()) return false;
                try
                {
                    _io.SetToolDigitalOut(pin, value);
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.InnerException?.Message ?? ex.Message;
                    return false;
                }
            }
        }

        public bool SetTcp(double[] tcpPose)
        {
            if (tcpPose == null || tcpPose.Length != 6) throw new ArgumentException("tcpPose must be length 6", nameof(tcpPose));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("SetTcp", new object[] { tcpPose });
            }
        }

        public bool SetPayload(double mass, double[] centerOfGravity)
        {
            if (centerOfGravity == null || centerOfGravity.Length != 3)
                throw new ArgumentException("centerOfGravity must be length 3", nameof(centerOfGravity));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlVoid("SetPayload", new object[] { mass, centerOfGravity });
            }
        }

        public double[] ForwardKinematics(double[] q)
        {
            if (q == null || q.Length != 6) throw new ArgumentException("q must be length 6", nameof(q));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return _control.GetForwardKinematics(q);
            }
        }

        public bool HasInverseKinematicsSolution(double[] pose)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6", nameof(pose));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return _control.HasInverseKinematicsSolution(pose);
            }
        }

        public double[] InverseKinematics(double[] pose)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6", nameof(pose));
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return _control.GetInverseKinematics(pose);
            }
        }

        public bool RobotiqActivate(RobotiqBackend backend, bool autoCalibrate, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            lock (_lockObj)
            {
                switch (backend)
                {
                    case RobotiqBackend.Native:
                        return RunRobotiqNative(port, verbose, timeoutMs, g =>
                        {
                            g.SetUnit(RobotiqMoveParameter.Position, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Speed, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Force, RobotiqUnit.Device);
                            g.Activate(autoCalibrate);
                            return (true, "Robotiq activated (native)");
                        }, out message);

                    case RobotiqBackend.RtdeBridge:
                        return RunRobotiqRtde(installBridge, timeoutMs, async (g, ct) =>
                        {
                            await g.ActivateAsync(ct);
                            return "Robotiq activated (RTDE bridge)";
                        }, out message);

                    case RobotiqBackend.UrScript:
                        return RunRobotiqScript(port, timeoutMs, async (g, ct) =>
                        {
                            await g.ActivateAsync(ct);
                            return "Robotiq activated (URScript)";
                        }, out message);

                    default:
                        message = "Unsupported backend";
                        LastError = message;
                        return false;
                }
            }
        }

        public bool RobotiqOpen(RobotiqBackend backend, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);

            lock (_lockObj)
            {
                switch (backend)
                {
                    case RobotiqBackend.Native:
                        return RunRobotiqNative(port, verbose, timeoutMs, g =>
                        {
                            g.SetUnit(RobotiqMoveParameter.Position, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Speed, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Force, RobotiqUnit.Device);
                            g.SetSpeed(s);
                            g.SetForce(f);
                            var status = g.Open(s, f, waitForMotion ? RobotiqMoveMode.WaitFinished : RobotiqMoveMode.StartMove);
                            var fault = g.FaultStatus();
                            return NativeResult(status, fault, "open");
                        }, out message);

                    case RobotiqBackend.RtdeBridge:
                        return RunRobotiqRtde(installBridge, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.OpenAsync(ct);
                            return "Open sent (RTDE bridge)";
                        }, out message);

                    case RobotiqBackend.UrScript:
                        return RunRobotiqScript(port, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.OpenAsync(ct);
                            return "Open sent (URScript)";
                        }, out message);

                    default:
                        message = "Unsupported backend";
                        LastError = message;
                        return false;
                }
            }
        }

        public bool RobotiqClose(RobotiqBackend backend, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);

            lock (_lockObj)
            {
                switch (backend)
                {
                    case RobotiqBackend.Native:
                        return RunRobotiqNative(port, verbose, timeoutMs, g =>
                        {
                            g.SetUnit(RobotiqMoveParameter.Position, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Speed, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Force, RobotiqUnit.Device);
                            g.SetSpeed(s);
                            g.SetForce(f);
                            var status = g.Close(s, f, waitForMotion ? RobotiqMoveMode.WaitFinished : RobotiqMoveMode.StartMove);
                            var fault = g.FaultStatus();
                            return NativeResult(status, fault, "close");
                        }, out message);

                    case RobotiqBackend.RtdeBridge:
                        return RunRobotiqRtde(installBridge, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.CloseAsync(ct);
                            return "Close sent (RTDE bridge)";
                        }, out message);

                    case RobotiqBackend.UrScript:
                        return RunRobotiqScript(port, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.CloseAsync(ct);
                            return "Close sent (URScript)";
                        }, out message);

                    default:
                        message = "Unsupported backend";
                        LastError = message;
                        return false;
                }
            }
        }

        public bool RobotiqMove(RobotiqBackend backend, double position, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var p = ClampToDevice(position);
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);

            lock (_lockObj)
            {
                switch (backend)
                {
                    case RobotiqBackend.Native:
                        return RunRobotiqNative(port, verbose, timeoutMs, g =>
                        {
                            g.SetUnit(RobotiqMoveParameter.Position, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Speed, RobotiqUnit.Device);
                            g.SetUnit(RobotiqMoveParameter.Force, RobotiqUnit.Device);
                            g.SetSpeed(s);
                            g.SetForce(f);
                            var status = g.Move(p, s, f, waitForMotion ? RobotiqMoveMode.WaitFinished : RobotiqMoveMode.StartMove);
                            var fault = g.FaultStatus();
                            return NativeResult(status, fault, $"move to {p:0}");
                        }, out message);

                    case RobotiqBackend.RtdeBridge:
                        return RunRobotiqRtde(installBridge, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.MoveAsync((byte)p, ct);
                            return $"Move {p:0} sent (RTDE bridge)";
                        }, out message);

                    case RobotiqBackend.UrScript:
                        return RunRobotiqScript(port, timeoutMs, async (g, ct) =>
                        {
                            await g.SetSpeedAsync((byte)s, ct);
                            await g.SetForceAsync((byte)f, ct);
                            await g.MoveAsync((byte)p, ct);
                            return $"Move {p:0} sent (URScript)";
                        }, out message);

                    default:
                        message = "Unsupported backend";
                        LastError = message;
                        return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_lockObj)
            {
                DisposeClientsInternal();
            }
            GC.SuppressFinalize(this);
        }

        private void DisposeClientsInternal()
        {
            // Must be called within lock. Mark disconnected before tearing down native clients
            // so timer/read threads fail fast instead of racing into freed RTDE handles.
            _isConnected = false;
            var receive = _receive;
            var control = _control;
            var io = _io;
            _receive = null;
            _control = null;
            _io = null;
            try { receive?.Dispose(); } catch { }
            try { control?.Dispose(); } catch { }
            try { io?.Dispose(); } catch { }
        }

        private RTDEReceive RequireReceive()
        {
            if (!_isConnected || _receive == null)
                throw new InvalidOperationException("Not connected");
            return _receive;
        }

        private T InvokeReceive<T>(string[] methodNames)
        {
            Exception last = null;
            foreach (var name in methodNames)
            {
                try
                {
                    var mi = _receive.GetType().GetMethod(name);
                    if (mi == null) continue;
                    var result = mi.Invoke(_receive, Array.Empty<object>());
                    return (T)result;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            throw new MissingMethodException($"None of the methods found on RTDEReceive: {string.Join(", ", methodNames)}", last);
        }

        private bool EnsureIo()
        {
            if (_control == null)
            {
                LastError = "Not connected";
                return false;
            }

            if (_io != null) return true;

            try
            {
                _io = new RTDEIO(Ip, false);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private int BuildDigitalBits(string methodName)
        {
            var mi = _receive.GetType().GetMethod(methodName, new[] { typeof(int) });
            if (mi == null)
                throw new MissingMethodException($"Method not found on RTDEReceive: {methodName}(int)");

            int bits = 0;
            // UR standard digital IO is usually exposed as 8 channels in this API shape.
            for (int i = 0; i < 8; i++)
            {
                var state = mi.Invoke(_receive, new object[] { i });
                if (state is bool b && b)
                    bits |= (1 << i);
            }

            return bits;
        }

        private double InvokeReceiveIndexedDouble(string methodName, int index)
        {
            var mi = _receive.GetType().GetMethod(methodName, new[] { typeof(int) });
            if (mi == null)
                throw new MissingMethodException($"Method not found on RTDEReceive: {methodName}(int)");

            var value = mi.Invoke(_receive, new object[] { index });
            return Convert.ToDouble(value);
        }

        private static bool IsVagueControlError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return true;
            if (!message.StartsWith("Command failed", StringComparison.OrdinalIgnoreCase))
                return false;
            var detail = message.Length > "Command failed:".Length
                ? message.Substring("Command failed:".Length).Trim()
                : string.Empty;
            return detail.Length == 0 || detail.Equals("Unknown error", StringComparison.OrdinalIgnoreCase);
        }

        private string DescribeRobotStateForControlFailure()
        {
            if (!_isConnected || _receive == null)
                return "session not connected";

            try
            {
                var parts = new List<string>();
                var robotMode = GetRobotMode();
                parts.Add($"robot mode {robotMode} ({DescribeRobotMode(robotMode)})");
                var safety = GetSafetyMode();
                parts.Add($"safety {safety} ({DescribeSafetyMode(safety)})");
                if (IsProgramRunning())
                    parts.Add("PolyScope program is playing — stop it before RTDE MoveJ/MoveL");
                if (safety != 1)
                    parts.Add("clear protective/safeguard stop on the teach pendant");
                if (robotMode is 3 or 4)
                    parts.Add("power on the robot and release brakes");
                if (robotMode is 0 or 2)
                    parts.Add("wait until the robot finishes booting");
                parts.Add("enable Remote Control on the pendant, or run External Control URCap (port 50002) on URSim");
                parts.Add("turn UR Stream off (Stop) — ServoJ blocks MoveJ until streaming ends");
                return string.Join("; ", parts);
            }
            catch (Exception ex)
            {
                return "could not read robot state: " + ex.Message;
            }
        }

        private static string DescribeRobotMode(int mode) => mode switch
        {
            0 => "Disconnected",
            1 => "ConfirmSafety",
            2 => "Booting",
            3 => "PowerOff",
            4 => "PowerOn",
            5 => "Idle",
            6 => "Backdrive",
            7 => "Running",
            8 => "UpdatingFirmware",
            _ => "Unknown"
        };

        private static string DescribeSafetyMode(int mode) => mode switch
        {
            1 => "Normal",
            2 => "Reduced",
            3 => "ProtectiveStop",
            4 => "Recovery",
            5 => "SafeguardStop",
            6 => "SystemEmergencyStop",
            7 => "RobotEmergencyStop",
            8 => "Violation",
            9 => "Fault",
            10 => "AutomaticModeSafeguardStop",
            11 => "ThreePositionEnablingStop",
            _ => "Unknown"
        };

        private string FormatControlException(string methodName, Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (!IsVagueControlError(msg))
                return msg;
            return $"{methodName} rejected by RTDE control (robot returned no detail). {DescribeRobotStateForControlFailure()}";
        }

        /// <summary>
        /// ServoJ/SpeedJ keep the RTDE script in streaming mode; path moves are rejected until stopped.
        /// </summary>
        private void EndStreamingMotionBeforePathMove()
        {
            if (_control == null) return;
            var previousError = LastError;
            TryInvokeControlVoid("ServoStop", new object[] { 0.5 });
            TryInvokeControlVoid("SpeedStop", new object[] { 10.0 });
            LastError = previousError;
        }

        private void TryInvokeControlVoid(string methodName, object[] args)
        {
            try
            {
                var mi = _control.GetType().GetMethod(methodName);
                mi?.Invoke(_control, args);
            }
            catch
            {
                // Streaming may not be active.
            }
        }

        private bool InvokeControlBool(string methodName, object[] args)
        {
            try
            {
                var mi = _control.GetType().GetMethod(methodName);
                if (mi == null) 
                {
                    LastError = $"Method not found: {methodName}";
                    return false;
                }
                var result = mi.Invoke(_control, args);
                LastError = null;
                if (mi.ReturnType == typeof(bool))
                    return result is bool b && b;
                return true; // treat void as success if no exception thrown
            }
            catch (Exception ex)
            {
                LastError = FormatControlException(methodName, ex);
                return false;
            }
        }

        private bool InvokeControlVoid(string methodName, object[] args)
        {
            try
            {
                var mi = _control.GetType().GetMethod(methodName);
                if (mi == null)
                {
                    LastError = $"Method not found: {methodName}";
                    return false;
                }
                mi.Invoke(_control, args);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = FormatControlException(methodName, ex);
                return false;
            }
        }

        private bool RunRobotiqNative(int port, bool verbose, int timeoutMs, Func<RobotiqGripperNative, (bool Ok, string Message)> action, out string message)
        {
            var effectivePort = port > 0 ? port : DefaultRobotiqNativePort;
            try
            {
                using var g = new RobotiqGripperNative(Ip, effectivePort, verbose);
                g.Connect((uint)Math.Max(1, timeoutMs));
                var result = action(g);
                LastError = result.Ok ? null : result.Message;
                message = result.Message;
                return result.Ok;
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException?.Message ?? ex.Message;
                message = LastError;
                return false;
            }
        }

        private bool RunRobotiqRtde(bool installBridge, int timeoutMs, Func<RobotiqGripperRtde, CancellationToken, System.Threading.Tasks.Task<string>> action, out string message)
        {
            if (_control == null || _receive == null) throw new InvalidOperationException("Not connected");
            using var cts = new CancellationTokenSource(Math.Max(1, timeoutMs));
            try
            {
                using var io = new RTDEIO(Ip, false);
                var gripper = new RobotiqGripperRtde(_control, _receive, io);
                if (installBridge)
                    gripper.InstallBridgeAsync(cts.Token).GetAwaiter().GetResult();
                message = action(gripper, cts.Token).GetAwaiter().GetResult();
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException?.Message ?? ex.Message;
                message = LastError;
                return false;
            }
        }

        private bool RunRobotiqScript(int port, int timeoutMs, Func<RobotiqGripper, CancellationToken, System.Threading.Tasks.Task<string>> action, out string message)
        {
            var effectivePort = port > 0 ? port : DefaultRobotiqScriptPort;
            using var cts = new CancellationTokenSource(Math.Max(1, timeoutMs));
            try
            {
                using var gripper = new RobotiqGripper(Ip, effectivePort);
                gripper.ConnectAsync(timeoutMs, cts.Token).GetAwaiter().GetResult();
                message = action(gripper, cts.Token).GetAwaiter().GetResult();
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException?.Message ?? ex.Message;
                message = LastError;
                return false;
            }
        }

        private static (bool Ok, string Message) NativeResult(RobotiqObjectStatus status, RobotiqFaultCode fault, string verb)
        {
            if (fault != RobotiqFaultCode.NoFault)
                return (false, $"Robotiq fault {fault}");
            var statusLabel = status switch
            {
                RobotiqObjectStatus.Moving => "moving",
                RobotiqObjectStatus.StoppedOuterObject => "stopped on outer object",
                RobotiqObjectStatus.StoppedInnerObject => "stopped on inner object",
                RobotiqObjectStatus.AtDestination => "at destination",
                _ => status.ToString()
            };
            return (true, $"Robotiq {verb}: {statusLabel}");
        }

        private static float ClampToDevice(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return (float)Math.Max(0, Math.Min(255, value));
        }

        private static void RegisterAssemblyResolver()
        {
            if (Interlocked.Exchange(ref _resolverRegistered, 1) != 0)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveSiblingAssembly;
        }

        private static Assembly ResolveSiblingAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrWhiteSpace(requestedName))
                    return null;

                var assemblyDir = Path.GetDirectoryName(typeof(URSession).Assembly.Location);
                if (string.IsNullOrWhiteSpace(assemblyDir))
                    return null;

                var candidatePath = Path.Combine(assemblyDir, requestedName + ".dll");
                if (!File.Exists(candidatePath))
                    return null;

                return Assembly.LoadFrom(candidatePath);
            }
            catch
            {
                return null;
            }
        }

    }
}
