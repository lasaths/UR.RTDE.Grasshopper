using NUnit.Framework;
using Rhino.Geometry;
using System;
using UR.RTDE.Grasshopper;
using Rhino.Testing.Fixtures;
using Rhino;

namespace UR.RTDE.Grasshopper.Tests
{
    [TestFixture]
    [RhinoTestFixture]
    public class PoseUtilsTests
    {
        [Test]
        public void TestPlaneToPoseConversion()
        {
            WithDocUnit(UnitSystem.Meters, () =>
            {
                var plane = new Plane(new Point3d(1, 2, 3), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
                var pose = PoseUtils.PlaneToPose(plane);

                Assert.That(pose, Is.Not.Null);
                Assert.That(pose.Length, Is.EqualTo(6));
                Assert.That(pose[0], Is.EqualTo(1.0).Within(0.001));
                Assert.That(pose[1], Is.EqualTo(2.0).Within(0.001));
                Assert.That(pose[2], Is.EqualTo(3.0).Within(0.001));
            });
        }

        [Test]
        public void TestPoseToPlaneConversion()
        {
            WithDocUnit(UnitSystem.Meters, () =>
            {
                double[] pose = { 1.0, 2.0, 3.0, 0.0, 0.0, 0.0 };
                var plane = PoseUtils.PoseToPlane(pose);

                Assert.That(plane, Is.Not.Null);
                Assert.That(plane.IsValid, Is.True);
                Assert.That(plane.OriginX, Is.EqualTo(1.0).Within(0.001));
                Assert.That(plane.OriginY, Is.EqualTo(2.0).Within(0.001));
                Assert.That(plane.OriginZ, Is.EqualTo(3.0).Within(0.001));
            });
        }

        [Test]
        public void TestRoundTripConversion()
        {
            WithDocUnit(UnitSystem.Meters, () =>
            {
                var originalPlane = new Plane(new Point3d(1, 2, 3), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
                var pose = PoseUtils.PlaneToPose(originalPlane);
                var convertedPlane = PoseUtils.PoseToPlane(pose);

                Assert.That(convertedPlane.OriginX, Is.EqualTo(originalPlane.OriginX).Within(0.001));
                Assert.That(convertedPlane.OriginY, Is.EqualTo(originalPlane.OriginY).Within(0.001));
                Assert.That(convertedPlane.OriginZ, Is.EqualTo(originalPlane.OriginZ).Within(0.001));
            });
        }

        [Test]
        public void TestMillimeterScaling()
        {
            WithDocUnit(UnitSystem.Millimeters, () =>
            {
                // 1m == 1000mm
                var planeMm = new Plane(new Point3d(1000, 2000, 3000), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
                var poseMeters = PoseUtils.PlaneToPose(planeMm);

                Assert.That(poseMeters[0], Is.EqualTo(1.0).Within(0.001));
                Assert.That(poseMeters[1], Is.EqualTo(2.0).Within(0.001));
                Assert.That(poseMeters[2], Is.EqualTo(3.0).Within(0.001));

                var backToPlane = PoseUtils.PoseToPlane(poseMeters);
                Assert.That(backToPlane.OriginX, Is.EqualTo(1000.0).Within(0.001));
                Assert.That(backToPlane.OriginY, Is.EqualTo(2000.0).Within(0.001));
                Assert.That(backToPlane.OriginZ, Is.EqualTo(3000.0).Within(0.001));
            });
        }

        [Test]
        public void TestUnsupportedUnitsThrow()
        {
            WithDocUnit(UnitSystem.Centimeters, () =>
            {
                Assert.Throws<InvalidOperationException>(() => PoseUtils.PlaneToPose(Plane.WorldXY));
                Assert.Throws<InvalidOperationException>(() => PoseUtils.PoseToPlane(new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }));
            });
        }

        [Test]
        public void TestPoseToPlaneInvalidInput()
        {
            Assert.Throws<ArgumentException>(() => PoseUtils.PoseToPlane(null));
            Assert.Throws<ArgumentException>(() => PoseUtils.PoseToPlane(new[] { 1.0, 2.0 }));
        }

        private static void WithDocUnit(UnitSystem target, Action action)
        {
            var doc = RhinoDoc.ActiveDoc;
            Assert.That(doc, Is.Not.Null, "ActiveDoc is required for unit-sensitive tests");
            var original = doc.ModelUnitSystem;
            try
            {
                doc.ModelUnitSystem = target;
                action();
            }
            finally
            {
                doc.ModelUnitSystem = original;
            }
        }
    }
}
