using System;
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

    public sealed class URSession : IDisposable
    {
        private const int DefaultRobotiqNativePort = 63352;
        private const int DefaultRobotiqScriptPort = 30002;

        private readonly object _lockObj = new object();
        private UR.RTDE.RTDEControl _control;
        private UR.RTDE.RTDEReceive _receive;
        private RTDEIO _io;
        private volatile bool _isConnected;

        public string Ip { get; }
        public bool IsConnected => _isConnected;
        public string LastError { get; private set; }

        public URSession(string ip)
        {
            Ip = ip ?? string.Empty;
        }

        public bool Connect(int timeoutMs = 2000)
        {
            lock (_lockObj)
            {
                try
                {
                    DisposeClientsInternal();
                    _control = new UR.RTDE.RTDEControl(Ip);
                    _receive = new UR.RTDE.RTDEReceive(Ip);
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

        public double[] GetActualQ()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return _receive.GetActualQ();
        }

        public double[] GetActualTCPPose()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<double[]>(new[] { "GetActualTCPPose", "GetActualTcpPose", "GetActualToolPose" });
        }

        public bool MoveJ(double[] q, double speed, double acceleration, bool asynchronous)
        {
            if (q == null || q.Length != 6) throw new ArgumentException("q must be length 6", nameof(q));
            
            bool result;
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                result = InvokeControlBool("MoveJ", new object[] { q, speed, acceleration, asynchronous });
            }
            
            // If synchronous, wait for the move to complete (outside lock so reads can continue)
            if (!asynchronous && result)
            {
                WaitForMoveComplete();
            }
            
            return result;
        }

        public bool StopJ(double deceleration)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlBool("StopJ", new object[] { deceleration });
            }
        }

        public bool StopL(double deceleration)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                return InvokeControlBool("StopL", new object[] { deceleration });
            }
        }

        public bool MoveL(double[] pose, double speed, double acceleration, bool asynchronous)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6", nameof(pose));
            
            bool result;
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                result = InvokeControlBool("MoveL", new object[] { pose, speed, acceleration, asynchronous });
            }
            
            // If synchronous, wait for the move to complete (outside lock so reads can continue)
            if (!asynchronous && result)
            {
                WaitForMoveComplete();
            }
            
            return result;
        }

        public bool SetStandardDigitalOut(int pin, bool value)
        {
            lock (_lockObj)
            {
                if (_control == null) throw new InvalidOperationException("Not connected");
                if (_io == null)
                {
                    try { _io = new RTDEIO(Ip, false); }
                    catch (Exception ex)
                    {
                        LastError = ex.InnerException?.Message ?? ex.Message;
                        return false;
                    }
                }

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
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<int>(new[] { "GetDigitalInState", "GetActualDigitalInputBits" });
        }

        public int GetDigitalOutState()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<int>(new[] { "GetDigitalOutState", "GetActualDigitalOutputBits" });
        }

        public double GetStandardAnalogInput0()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<double>(new[] { "GetStandardAnalogInput0", "GetActualStandardAnalogInput0" });
        }

        public double GetStandardAnalogInput1()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<double>(new[] { "GetStandardAnalogInput1", "GetActualStandardAnalogInput1" });
        }

        public double GetStandardAnalogOutput0()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<double>(new[] { "GetStandardAnalogOutput0", "GetActualStandardAnalogOutput0" });
        }

        public double GetStandardAnalogOutput1()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<double>(new[] { "GetStandardAnalogOutput1", "GetActualStandardAnalogOutput1" });
        }

        public int GetRobotMode()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<int>(new[] { "GetRobotMode" });
        }

        public int GetSafetyMode()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<int>(new[] { "GetSafetyMode" });
        }

        public bool IsProgramRunning()
        {
            if (_receive == null) throw new InvalidOperationException("Not connected");
            return InvokeReceive<bool>(new[] { "IsProgramRunning" });
        }

        public bool RobotiqActivate(RobotiqBackend backend, bool autoCalibrate, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
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

        public bool RobotiqOpen(RobotiqBackend backend, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);
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

        public bool RobotiqClose(RobotiqBackend backend, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);
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

        public bool RobotiqMove(RobotiqBackend backend, double position, double speed, double force, bool waitForMotion, int timeoutMs, bool installBridge, bool verbose, int port, out string message)
        {
            var p = ClampToDevice(position);
            var s = ClampToDevice(speed);
            var f = ClampToDevice(force);
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
            // Must be called within lock
            try { _receive?.Dispose(); } catch { }
            try { _control?.Dispose(); } catch { }
            try { _io?.Dispose(); } catch { }
            _receive = null;
            _control = null;
            _io = null;
            _isConnected = false;
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
                if (mi.ReturnType == typeof(bool))
                    return result is bool b && b;
                return true; // treat void as success if no exception thrown
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException?.Message ?? ex.Message;
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

        /// <summary>
        /// Waits for the robot to complete its current move.
        /// Called outside the lock so reads can continue during the wait.
        /// </summary>
        private void WaitForMoveComplete()
        {
            if (_receive == null) return;
            
            // First, wait for the robot to start moving (velocity > threshold)
            // This prevents detecting "stopped" before the move even begins
            Thread.Sleep(100); // Give time for the move command to be processed
            
            bool moveStarted = false;
            int startTimeout = 200; // 2 seconds to detect move start (10ms * 200)
            
            for (int i = 0; i < startTimeout && !moveStarted; i++)
            {
                try
                {
                    var velocities = InvokeReceive<double[]>(new[] { "GetActualQd" });
                    if (velocities != null)
                    {
                        foreach (var v in velocities)
                        {
                            if (Math.Abs(v) > 0.01) // Robot is moving
                            {
                                moveStarted = true;
                                break;
                            }
                        }
                    }
                }
                catch { }
                
                if (!moveStarted)
                    Thread.Sleep(10);
            }
            
            // If move never started, maybe target was already reached or command failed
            if (!moveStarted)
                return;
            
            // Now wait for the robot to stop (all velocities near zero)
            int maxAttempts = 6000; // 60 seconds max (10ms * 6000)
            int stoppedCount = 0; // Require multiple consecutive "stopped" readings
            const int requiredStoppedReadings = 5;
            
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var velocities = InvokeReceive<double[]>(new[] { "GetActualQd" });
                    if (velocities != null)
                    {
                        bool allStopped = true;
                        foreach (var v in velocities)
                        {
                            if (Math.Abs(v) > 0.001) // Threshold for "stopped"
                            {
                                allStopped = false;
                                break;
                            }
                        }
                        
                        if (allStopped)
                        {
                            stoppedCount++;
                            if (stoppedCount >= requiredStoppedReadings)
                                return; // Confirmed stopped
                        }
                        else
                        {
                            stoppedCount = 0; // Reset counter if still moving
                        }
                    }
                }
                catch
                {
                    // If we can't read velocities, just continue waiting
                }
                
                Thread.Sleep(10);
            }
        }
    }
}
