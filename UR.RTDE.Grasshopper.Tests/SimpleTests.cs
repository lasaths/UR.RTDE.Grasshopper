using NUnit.Framework;
using System;

namespace UR.RTDE.Grasshopper.Tests
{
    [TestFixture]
    public class SimpleTests
    {
        [Test]
        public void TestBasicMath()
        {
            Assert.That(2 + 2, Is.EqualTo(4));
        }

        [Test]
        public void TestStringOperations()
        {
            string test = "Hello World";
            Assert.That(test.Length, Is.EqualTo(11));
            Assert.That(test.Contains("World"), Is.True);
        }

        [Test]
        public void TestArrayOperations()
        {
            double[] pose = { 1.0, 2.0, 3.0, 0.0, 0.0, 0.0 };
            Assert.That(pose.Length, Is.EqualTo(6));
            Assert.That(pose[0], Is.EqualTo(1.0));
            Assert.That(pose[1], Is.EqualTo(2.0));
            Assert.That(pose[2], Is.EqualTo(3.0));
        }

        [Test]
        public void TestExceptionHandling()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                if (true) throw new ArgumentException("Test exception");
            });
        }
    }
}
