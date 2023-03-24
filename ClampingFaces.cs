using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class ClampingFaces : Utilities.Pair<PolygonFace>
    {
        public ClampingFaces(Body body, PolygonFace one, PolygonFace two, Utilities.Plane bottomPlane ) : base(new PolygonFace(one), new PolygonFace(two))
        {
            InitialyseClampingFaces(bottomPlane, body.GetFaces().SelectMany(p => Utilities.NXOpenUtils.GetFaceVertices(p)).ToArray());
            Box = BoundingBox.ComputeBodyBoundingBox(body);
        }


        public ClampingFaces(BoundingBox boundindBox, PolygonFace one, PolygonFace two, Utilities.Plane bottomPlane) : base(new PolygonFace(one), new PolygonFace(two))
        {
            InitialyseClampingFaces(bottomPlane, boundindBox.GetFaces().SelectMany(p => p.GetPoints()).ToArray());
            Box = boundindBox;
        }


        private void InitialyseClampingFaces(Utilities.Plane bottomPlane, Point3d[] points)
        {
            Point3d origin = Utilities.MathUtils.Projection(Utilities.MathUtils.Average(One.Origin, Two.Origin), bottomPlane);

            Vector3d lineVector = Utilities.MathUtils.Cross(bottomPlane.Normal, One.Normal);
            Utilities.Line viseCenterLine = new Utilities.Line(origin, lineVector);

            List<Point3d> orderedPoints = points.Select(p => Utilities.MathUtils.Projection(p, viseCenterLine))
                                                .OrderBy(p => Utilities.MathUtils.Dot(lineVector, p)).ToList();

            Point3d ViseCenter = Utilities.MathUtils.Average(orderedPoints.First(), orderedPoints.Last());

            BottomPlane = new Utilities.Plane(ViseCenter, bottomPlane.Normal);
        }


        ~ClampingFaces()
        {
            // empty
        }


        public bool IsOutsideClampingPlanes(Point3d point)
        {
            return Utilities.MathUtils.GetSignedDistance(point, One) > Utilities.MathUtils.ABS_TOL ||
                   Utilities.MathUtils.GetSignedDistance(point, Two) > Utilities.MathUtils.ABS_TOL;
        }


        public Utilities.Plane BottomPlane { get; private set; }
        public BoundingBox Box { get; private set; }
    }
}