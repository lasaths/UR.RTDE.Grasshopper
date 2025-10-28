using System;
using Rhino.Geometry;

namespace UR.RTDE.Grasshopper
{
    internal static class PoseUtils
    {
        // Convert a Rhino Plane to UR pose [x,y,z,rx,ry,rz] in meters and radians (axis-angle vector)
        public static double[] PlaneToPose(Plane p)
        {
            // Position in meters
            double x = p.OriginX;
            double y = p.OriginY;
            double z = p.OriginZ;

            // Build rotation matrix from plane axes
            var m = new double[9]
            {
                p.XAxis.X, p.YAxis.X, p.ZAxis.X,
                p.XAxis.Y, p.YAxis.Y, p.ZAxis.Y,
                p.XAxis.Z, p.YAxis.Z, p.ZAxis.Z
            };

            AxisAngleFromRotationMatrix(m, out var axis, out var angle);
            // Rodrigues vector r = axis * angle
            var rx = axis.X * angle;
            var ry = axis.Y * angle;
            var rz = axis.Z * angle;

            return new[] { x, y, z, rx, ry, rz };
        }

        // Convert UR pose [x,y,z,rx,ry,rz] to Rhino Plane
        public static Plane PoseToPlane(double[] pose)
        {
            if (pose == null || pose.Length != 6) throw new ArgumentException("pose must be length 6");
            double x = pose[0], y = pose[1], z = pose[2];
            double rx = pose[3], ry = pose[4], rz = pose[5];

            var angle = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            Vector3d axis = angle > 1e-9 ? new Vector3d(rx / angle, ry / angle, rz / angle) : new Vector3d(1, 0, 0);
            var R = RotationMatrixFromAxisAngle(axis, angle);

            var xAxis = new Vector3d(R[0], R[3], R[6]);
            var yAxis = new Vector3d(R[1], R[4], R[7]);
            var zAxis = new Vector3d(R[2], R[5], R[8]);
            var origin = new Point3d(x, y, z);
            return new Plane(origin, xAxis, yAxis);
        }

        private static void AxisAngleFromRotationMatrix(double[] m, out Vector3d axis, out double angle)
        {
            // m is in column-major [r00 r01 r02 r10 r11 r12 r20 r21 r22]? We constructed columns above.
            double r00 = m[0], r01 = m[1], r02 = m[2];
            double r10 = m[3], r11 = m[4], r12 = m[5];
            double r20 = m[6], r21 = m[7], r22 = m[8];

            double trace = r00 + r11 + r22;
            angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 3.0) * 0.5 + 1.0))) * 2.0; // Stable variant

            if (angle < 1e-9)
            {
                axis = new Vector3d(1, 0, 0);
                angle = 0;
                return;
            }

            double denom = 2.0 * Math.Sin(angle);
            double ax = (r21 - r12) / denom;
            double ay = (r02 - r20) / denom;
            double az = (r10 - r01) / denom;
            axis = new Vector3d(ax, ay, az);
            axis.Unitize();
        }

        private static double[] RotationMatrixFromAxisAngle(Vector3d axis, double angle)
        {
            axis.Unitize();
            double x = axis.X, y = axis.Y, z = axis.Z;
            double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
            // Column-major
            return new double[9]
            {
                t*x*x + c,     t*x*y - s*z, t*x*z + s*y,
                t*x*y + s*z,   t*y*y + c,   t*y*z - s*x,
                t*x*z - s*y,   t*y*z + s*x, t*z*z + c
            };
        }
    }
}

