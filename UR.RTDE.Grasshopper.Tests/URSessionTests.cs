using NUnit.Framework;
using UR.RTDE.Grasshopper;
using System;
using System.Diagnostics;
using System.Threading;

namespace UR.RTDE.Grasshopper.Tests
{
    [TestFixture]
    public class URSessionTests
    {
        private const string TestIp = "127.0.0.1";
        private URSession _session;

        [SetUp]
        public void Setup()
        {
            _session = new URSession(TestIp);
        }

        [TearDown]
        public void TearDown()
        {
            _session?.Dispose();
        }

        [Test]
        public void TestSessionCreation()
        {
            Assert.That(_session, Is.Not.Null);
            Assert.That(_session.Ip, Is.EqualTo(TestIp));
            Assert.That(_session.IsConnected, Is.False);
        }

        [Test]
        [Category("Integration")]
        public void TestConnectSetsConnectionStateConsistently()
        {
            bool connected = _session.Connect();

            Assert.That(_session.IsConnected, Is.EqualTo(connected));
            if (connected)
                Assert.That(_session.LastError, Is.Null.Or.Empty);
            else
                Assert.That(_session.LastError, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TestGetActualQWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetActualQ());
        }

        [Test]
        public void TestMoveJWithoutConnection()
        {
            double[] q = { 0, 0, 0, 0, 0, 0 };
            Assert.Throws<InvalidOperationException>(() => _session.MoveJ(q, 1.0, 1.0, false));
        }

        [Test]
        [Category("Integration")]
        public void TestMoveJWithInvalidInput()
        {
            bool connected = _session.Connect();
            if (!connected) return;
            
            Assert.Throws<ArgumentException>(() => _session.MoveJ(new[] { 1.0 }, 1.0, 1.0, false));
            Assert.Throws<ArgumentException>(() => _session.MoveJ(null, 1.0, 1.0, false));
        }

        [Test]
        public void TestMoveLWithoutConnection()
        {
            double[] pose = { 0.3, 0.0, 0.3, 0.0, 0.0, 0.0 };
            Assert.Throws<InvalidOperationException>(() => _session.MoveL(pose, 0.25, 1.2, false));
        }

        [Test]
        [Category("Integration")]
        public void TestMoveLWithInvalidInput()
        {
            bool connected = _session.Connect();
            if (!connected) return;
            
            Assert.Throws<ArgumentException>(() => _session.MoveL(new[] { 1.0 }, 0.25, 1.2, false));
            Assert.Throws<ArgumentException>(() => _session.MoveL(null, 0.25, 1.2, false));
        }

        [Test]
        public void TestStopJWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.StopJ(2.0));
        }

        [Test]
        public void TestStopLWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.StopL(2.0));
        }

        [Test]
        public void TestSetDOWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.SetStandardDigitalOut(0, true));
        }

        [Test]
        public void TestGetActualTCPPoseWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetActualTCPPose());
        }

        [Test]
        public void TestGetDigitalInStateWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetDigitalInState());
        }

        [Test]
        public void TestGetDigitalOutStateWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetDigitalOutState());
        }

        [Test]
        public void TestGetRobotModeWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetRobotMode());
        }

        [Test]
        public void TestGetSafetyModeWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetSafetyMode());
        }

        [Test]
        public void TestIsProgramRunningWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.IsProgramRunning());
        }

        [Test]
        public void TestStreamingApisWithoutConnection()
        {
            var q = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            Assert.Throws<InvalidOperationException>(() => _session.SpeedJ(q, 0.5, 0.02));
            Assert.Throws<InvalidOperationException>(() => _session.ServoJ(q, 0.5, 0.5, 0.02, 0.1, 300));
            Assert.Throws<InvalidOperationException>(() => _session.SpeedStop());
            Assert.Throws<InvalidOperationException>(() => _session.ServoStop());
        }

        [Test]
        public void TestTelemetryApisWithoutConnection()
        {
            Assert.Throws<InvalidOperationException>(() => _session.GetTargetQ());
            Assert.Throws<InvalidOperationException>(() => _session.GetTargetTcpPose());
            Assert.Throws<InvalidOperationException>(() => _session.GetActualQd());
            Assert.Throws<InvalidOperationException>(() => _session.GetActualTcpSpeed());
            Assert.Throws<InvalidOperationException>(() => _session.GetActualTcpForce());
            Assert.Throws<InvalidOperationException>(() => _session.GetRobotStatus());
            Assert.Throws<InvalidOperationException>(() => _session.GetRuntimeState());
            Assert.Throws<InvalidOperationException>(() => _session.IsSteady());
        }

        [Test]
        public void TestSetupApisWithoutConnection()
        {
            var pose = new[] { 0.3, 0.0, 0.3, 0.0, 0.0, 0.0 };
            var cog = new[] { 0.0, 0.0, 0.0 };
            Assert.Throws<InvalidOperationException>(() => _session.SetAnalogOutput(0, 0.0, URAnalogOutputMode.Voltage));
            Assert.Throws<InvalidOperationException>(() => _session.SetToolDigitalOut(0, true));
            Assert.Throws<InvalidOperationException>(() => _session.SetTcp(pose));
            Assert.Throws<InvalidOperationException>(() => _session.SetPayload(1.0, cog));
        }

        [Test]
        public void TestKinematicsApisWithoutConnection()
        {
            var q = new[] { 0.0, -1.57, 1.57, 0.0, 1.57, 0.0 };
            var pose = new[] { 0.3, 0.0, 0.3, 0.0, 0.0, 0.0 };
            Assert.Throws<InvalidOperationException>(() => _session.ForwardKinematics(q));
            Assert.Throws<InvalidOperationException>(() => _session.HasInverseKinematicsSolution(pose));
            Assert.Throws<InvalidOperationException>(() => _session.InverseKinematics(pose));
        }

        [Test]
        [Category("Integration")]
        public void TestForwardKinematicsWhenConnected()
        {
            RequireConnectedSession();
            var q = _session.GetActualQ();
            var pose = _session.ForwardKinematics(q);
            Assert.That(pose, Is.Not.Null);
            Assert.That(pose.Length, Is.EqualTo(6));
        }

        [Test]
        [Category("Integration")]
        public void TestSpeedStopWhenConnected()
        {
            RequireConnectedSession();
            Assert.That(_session.SpeedStop(), Is.True, _session.LastError);
        }

        [Test]
        [Category("Integration")]
        public void TestMoveJSameTargetReturnsPromptlyWhenConnected()
        {
            RequireConnectedSession();

            var currentQ = _session.GetActualQ();
            var stopwatch = Stopwatch.StartNew();

            bool ok = _session.MoveJ(currentQ, 0.25, 0.5, false);

            stopwatch.Stop();

            Assert.That(ok, Is.True, _session.LastError);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1.5)),
                $"Synchronous MoveJ to the current joint target took too long: {stopwatch.Elapsed}.");
        }

        [Test]
        [Category("Integration")]
        public void TestStopJCanInterruptAsyncMoveWhenConnected()
        {
            RequireConnectedSession();

            var start = _session.GetActualQ();
            var target = (double[])start.Clone();
            target[0] += 0.2;

            try
            {
                bool moveStarted = _session.MoveJ(target, 0.2, 0.5, true);
                Assert.That(moveStarted, Is.True, _session.LastError);

                Thread.Sleep(100);

                bool stopSent = _session.StopJ(2.0);
                Assert.That(stopSent, Is.True, _session.LastError);
            }
            finally
            {
                try
                {
                    _session.MoveJ(start, 0.2, 0.5, false);
                }
                catch
                {
                    // Leave cleanup best-effort for simulator-backed tests.
                }
            }
        }

        [Test]
        [Category("Integration")]
        public void TestComprehensiveFeatureSurfaceWhenConnected()
        {
            RequireConnectedSession();

            var actualQ = _session.GetActualQ();
            AssertPoseVector(actualQ, 6, "ActualQ");

            var actualPose = _session.GetActualTCPPose();
            AssertPoseVector(actualPose, 6, "ActualTCPPose");

            AssertPoseVector(_session.GetTargetQ(), 6, "TargetQ");
            AssertPoseVector(_session.GetTargetTcpPose(), 6, "TargetTcpPose");
            AssertPoseVector(_session.GetActualQd(), 6, "ActualQd");
            AssertPoseVector(_session.GetActualTcpSpeed(), 6, "ActualTcpSpeed");
            AssertPoseVector(_session.GetActualTcpForce(), 6, "ActualTcpForce");

            _ = _session.GetDigitalInState();
            _ = _session.GetDigitalOutState();
            Assert.That(_session.GetStandardAnalogInput0(), Is.Not.NaN.And.Not.EqualTo(double.PositiveInfinity).And.Not.EqualTo(double.NegativeInfinity));
            Assert.That(_session.GetStandardAnalogInput1(), Is.Not.NaN.And.Not.EqualTo(double.PositiveInfinity).And.Not.EqualTo(double.NegativeInfinity));
            Assert.That(_session.GetStandardAnalogOutput0(), Is.Not.NaN.And.Not.EqualTo(double.PositiveInfinity).And.Not.EqualTo(double.NegativeInfinity));
            Assert.That(_session.GetStandardAnalogOutput1(), Is.Not.NaN.And.Not.EqualTo(double.PositiveInfinity).And.Not.EqualTo(double.NegativeInfinity));

            _ = _session.GetRobotMode();
            _ = _session.GetSafetyMode();
            _ = _session.GetRobotStatus();
            _ = _session.GetRuntimeState();
            _ = _session.IsProgramRunning();
            _ = _session.IsSteady();

            Assert.That(_session.SetStandardDigitalOut(0, true), Is.True, _session.LastError);
            Assert.That(_session.SetStandardDigitalOut(0, false), Is.True, _session.LastError);
            Assert.That(_session.SetToolDigitalOut(0, false), Is.True, _session.LastError);
            Assert.That(_session.SetAnalogOutput(0, 0.0, URAnalogOutputMode.Voltage), Is.True, _session.LastError);
            Assert.That(_session.SetAnalogOutput(1, 0.0, URAnalogOutputMode.Current), Is.True, _session.LastError);
            Assert.That(_session.SetTcp(new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }), Is.True, _session.LastError);
            Assert.That(_session.SetPayload(1.0, new[] { 0.0, 0.0, 0.0 }), Is.True, _session.LastError);

            var fk = _session.ForwardKinematics(actualQ);
            AssertPoseVector(fk, 6, "ForwardKinematics");
            Assert.That(_session.HasInverseKinematicsSolution(fk), Is.True, "IK should exist for FK output");
            var ik = _session.InverseKinematics(fk);
            AssertPoseVector(ik, 6, "InverseKinematics");

            Assert.That(_session.MoveJ(actualQ, 0.25, 0.5, false), Is.True, _session.LastError);
            Assert.That(_session.WaitForMotionComplete(3000), Is.True, _session.LastError);

            var upPose = (double[])actualPose.Clone();
            upPose[2] += 0.02;
            Assert.That(_session.MoveL(upPose, 0.10, 0.25, false), Is.True, _session.LastError);
            Assert.That(_session.MoveL(actualPose, 0.10, 0.25, false), Is.True, _session.LastError);
            Assert.That(_session.StopL(2.0), Is.True, _session.LastError);

            var tinyQd = new[] { 0.01, 0.0, 0.0, 0.0, 0.0, 0.0 };
            Assert.That(_session.SpeedJ(tinyQd, 0.5, 0.05), Is.True, _session.LastError);
            Assert.That(_session.SpeedStop(), Is.True, _session.LastError);

            var servoTarget = _session.GetActualQ();
            Assert.That(_session.ServoJ(servoTarget, 0.5, 0.5, 0.03, 0.1, 300), Is.True, _session.LastError);
            Assert.That(_session.ServoStop(), Is.True, _session.LastError);
            Assert.That(_session.StopJ(2.0), Is.True, _session.LastError);
        }

        private void RequireConnectedSession()
        {
            if (!_session.Connect())
                Assert.Ignore($"URSim not available at {TestIp}: {_session.LastError}");
        }

        private static void AssertPoseVector(double[] values, int expectedLength, string label)
        {
            Assert.That(values, Is.Not.Null, $"{label} should not be null");
            Assert.That(values.Length, Is.EqualTo(expectedLength), $"{label} should be length {expectedLength}");
            for (var i = 0; i < values.Length; i++)
            {
                Assert.That(values[i], Is.Not.NaN.And.Not.EqualTo(double.PositiveInfinity).And.Not.EqualTo(double.NegativeInfinity),
                    $"{label}[{i}] should be finite");
            }
        }
    }
}
