using NUnit.Framework;
using UR.RTDE.Grasshopper;
using System;

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
        public void TestConnectWithoutRobot()
        {
            bool connected = _session.Connect();
            
            Assert.That(connected, Is.False, "Connection should fail without a robot running");
            Assert.That(_session.IsConnected, Is.False);
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
    }
}
