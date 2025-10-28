using System;

namespace UR.RTDE.Grasshopper
{
    // Lightweight session wrapper that owns RTDEControl and RTDEReceive
    public sealed class URSession : IDisposable
    {
        private readonly object _lockObj = new object();
        private UR.RTDE.RTDEControl _control;
        private UR.RTDE.RTDEReceive _receive;
        private object _io; // Optional RTDEIO instance (late-bound via reflection)

        public string Ip { get; }
        public bool IsConnected { get; private set; }
        public string LastError { get; private set; }

        public URSession(string ip)
        {
            Ip = ip ?? string.Empty;
        }

        public bool Connect(int timeoutMs = 2000)
        {
            try
            {
                DisposeClients();
                // Construct control/receive clients. Default options are used to keep it simple.
                _control = new UR.RTDE.RTDEControl(Ip);
                _receive = new UR.RTDE.RTDEReceive(Ip);
                IsConnected = true;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsConnected = false;
                DisposeClients();
                return false;
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
            // Try several known method names across versions
            return InvokeReceive<double[]>(new[] { "GetActualTCPPose", "GetActualTcpPose", "GetActualToolPose" });
        }

        public bool MoveJ(double[] q, double speed, double acceleration, bool asynchronous)
        {
            if (q == null || q.Length != 6) throw new ArgumentException("q must be length 6", nameof(q));
            if (_control == null) throw new InvalidOperationException("Not connected");
            lock (_lockObj)
            {
                return InvokeControlBool("MoveJ", new object[] { q, speed, acceleration, asynchronous });
            }
        }

        public bool StopJ(double deceleration)
        {
            if (_control == null) throw new InvalidOperationException("Not connected");
            lock (_lockObj)
            {
                return InvokeControlBool("StopJ", new object[] { deceleration });
            }
        }

        public bool StopL(double deceleration)
        {
            if (_control == null) throw new InvalidOperationException("Not connected");
            lock (_lockObj)
            {
                return InvokeControlBool("StopL", new object[] { deceleration });
            }
        }

        public bool MoveL(double[] pose, double speed, double acceleration, bool asynchronous)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6", nameof(pose));
            if (_control == null) throw new InvalidOperationException("Not connected");
            lock (_lockObj)
            {
                return InvokeControlBool("MoveL", new object[] { pose, speed, acceleration, asynchronous });
            }
        }

        public bool SetStandardDigitalOut(int pin, bool value)
        {
            if (_control == null) throw new InvalidOperationException("Not connected");
            lock (_lockObj)
            {
                // Prefer RTDEIO if available; otherwise attempt on control
                // Lazy create RTDEIO via reflection to avoid hard dependency
                if (_io == null)
                {
                    var ioType = Type.GetType("UR.RTDE.RTDEIO, UR.RTDE");
                    if (ioType != null)
                    {
                        try { _io = Activator.CreateInstance(ioType, Ip); }
                        catch { _io = null; }
                    }
                }
                if (_io != null)
                {
                    try
                    {
                        var mi = _io.GetType().GetMethod("SetStandardDigitalOut");
                        mi?.Invoke(_io, new object[] { pin, value });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.InnerException?.Message ?? ex.Message;
                        return false;
                    }
                }
                return InvokeControlBool("SetStandardDigitalOut", new object[] { pin, value });
            }
        }

        // IO reads
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

        // Modes / status
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

        public void Dispose()
        {
            DisposeClients();
            GC.SuppressFinalize(this);
        }

        private void DisposeClients()
        {
            lock (_lockObj)
            {
                try { _receive?.Dispose(); } catch { }
                try { _control?.Dispose(); } catch { }
                _receive = null;
                _control = null;
                IsConnected = false;
            }
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
                if (mi == null) throw new MissingMethodException($"Method not found: {methodName}");
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
    }
}
