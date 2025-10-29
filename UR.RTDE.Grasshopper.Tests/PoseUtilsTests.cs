using NUnit.Framework;
using Rhino.Geometry;
using System;
using UR.RTDE.Grasshopper;
using Rhino.Testing.Fixtures;

namespace UR.RTDE.Grasshopper.Tests
{
    [TestFixture]
    [RhinoTestFixture]
    public class PoseUtilsTests
    {
        [Test]
        public void TestPlaneToPoseConversion()
        {
            var plane = new Plane(new Point3d(1, 2, 3), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var pose = PoseUtils.PlaneToPose(plane);
            
            Assert.That(pose, Is.Not.Null);
            Assert.That(pose.Length, Is.EqualTo(6));
            Assert.That(pose[0], Is.EqualTo(1.0).Within(0.001));
            Assert.That(pose[1], Is.EqualTo(2.0).Within(0.001));
            Assert.That(pose[2], Is.EqualTo(3.0).Within(0.001));
        }

        [Test]
        public void TestPoseToPlaneConversion()
        {
            double[] pose = { 1.0, 2.0, 3.0, 0.0, 0.0, 0.0 };
            var plane = PoseUtils.PoseToPlane(pose);
            
            Assert.That(plane, Is.Not.Null);
            Assert.That(plane.IsValid, Is.True);
            Assert.That(plane.OriginX, Is.EqualTo(1.0).Within(0.001));
            Assert.That(plane.OriginY, Is.EqualTo(2.0).Within(0.001));
            Assert.That(plane.OriginZ, Is.EqualTo(3.0).Within(0.001));
        }

        [Test]
        public void TestRoundTripConversion()
        {
            var originalPlane = new Plane(new Point3d(1, 2, 3), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var pose = PoseUtils.PlaneToPose(originalPlane);
            var convertedPlane = PoseUtils.PoseToPlane(pose);
            
            Assert.That(convertedPlane.OriginX, Is.EqualTo(originalPlane.OriginX).Within(0.001));
            Assert.That(convertedPlane.OriginY, Is.EqualTo(originalPlane.OriginY).Within(0.001));
            Assert.That(convertedPlane.OriginZ, Is.EqualTo(originalPlane.OriginZ).Within(0.001));
        }

        [Test]
        public void TestPoseToPlaneInvalidInput()
        {
            Assert.Throws<ArgumentException>(() => PoseUtils.PoseToPlane(null));
            Assert.Throws<ArgumentException>(() => PoseUtils.PoseToPlane(new[] { 1.0, 2.0 }));
        }
    }
}
