using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class PolygonFace : Utilities.Plane
    {
        public PolygonFace(Face face) : base(Utilities.NXOpenUtils.GetEdgeVertices(face.GetEdges().First()).First(), Utilities.NXOpenUtils.GetFaceNormal(face))
        {
            Polygons = ProjectOn(PointLoop.GetPointLoops(face).Select(p => new Polygon(p)));
            OrientPolygons(Normal);

            NXFace = face;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(Polygon polygon, Face face) : base(polygon.First(), polygon.Normal())
        {
            Polygons = new List<Polygon>() { ProjectOn(new Polygon(polygon)) };
            NXFace = face;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(List<Polygon> polygons, Face face) : base(polygons.First().First(), Polygon.ExteriorPolygons(polygons).First().Normal())
        {
            Polygons = ProjectOn(polygons);
            OrientPolygons(Normal);
            NXFace = face;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(List<Polygon> polygons, PolygonFace face) : base(face.Origin, face.Normal)
        {
            Polygons = ProjectOn(polygons);
            OrientPolygons(Normal);
            NXFace = face.NXFace;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(List<Polygon> polygons) : base(polygons.First().First(), Polygon.ExteriorPolygons(polygons).First().Normal())
        {
            Polygons = ProjectOn(polygons);
            OrientPolygons(Normal);
            NXFace = null;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(params Point3d[] polygon) : base(polygon.First(), new Polygon(polygon).Normal())
        {
            Polygons = new List<Polygon>() { ProjectOn(new Polygon(polygon)) };
            NXFace = null;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(Point3d[] polygon, Point3d origin, Vector3d normal) : base(origin, normal)
        {
            Polygons = new List<Polygon>() { ProjectOn(new Polygon(polygon)) };
            NXFace = null;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(List<Polygon> polygons, Point3d origin, Vector3d normal) : base(origin, normal)
        {
            Polygons = ProjectOn(polygons);
            OrientPolygons(Normal);
            NXFace = null;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        public PolygonFace(PolygonFace other) : base(other.Origin, other.Normal)
        {
            Polygons = other.Polygons.Select(p => new Polygon(p)).ToList();
            NXFace = other.NXFace;
            m_isValidArea = false;
            m_isValidCentroid = false;
        }


        ~PolygonFace()
        {
            // empty
        }


        public Point3d[] GetPoints()
        {
           return Polygons.SelectMany(p => p).ToArray();
        }


        public double GetArea()
        {
            if (!m_isValidArea)
            {
                m_area = CalculateVectorArea();
            }

            return Utilities.MathUtils.Length(m_area);
        }


        public Vector3d GetVectorArea()
        {
            if (!m_isValidArea)
            {
                m_area = CalculateVectorArea();
                m_isValidArea = true;
            }

            return m_area;
        }


        public Point3d GetCentroid()
        {
            if (!m_isValidCentroid)
            {
                CalculateCentroid(out m_centroid, out m_area); 
                m_isValidCentroid = true;
                m_isValidArea = true;
            }

            return m_centroid;
        }


        public Utilities.SymTensor GetInertia()
        {
            if (!m_isValidInertia)
            {
                CalculateInertia(out m_inertia, out m_centroid, out m_area);
                m_isValidInertia = true;
                m_isValidCentroid = true;
                m_isValidArea = true;
            }

            return m_inertia;
        }


        public double GetRadiusOfGyration(Vector3d rotarionAxis)
        {
            return Utilities.Inertia.GetRadiusOfGyration(GetInertia(), GetArea(), rotarionAxis);
        }


        public PolygonFace Clip(Utilities.Plane plane)
        {

            List<Polygon> polygonsBelow = Polygons.Where(p => p.IsBelowPlane(plane)).ToList();

            List<Polygon> clipPolygons = Polygons.Where(p => p.IsPlaneIntersect(plane))
                                                 .Where(q => Utilities.MathUtils.InSolidAngle(q.Normal(), Normal))
                                                 .Select(p => new Polygon(p.GetClip(plane)))
                                                 .Where(p => p.Count() >= 3)
                                                 .ToList();

            List<Polygon> polygonsHoleIntersecting = Polygons.Where(p => p.IsPlaneIntersect(plane))
                                                             .Where(q => Utilities.MathUtils.InTrailingCone(q.Normal(), Normal))
                                                             .ToList();

            List<Polygon> newClipPolygons = new List<Polygon>();
            foreach (Polygon clipPolygon in clipPolygons)
            {
                Polygon newPolygon = new Polygon(clipPolygon);
                foreach (Polygon hole in polygonsHoleIntersecting)
                {
                    if (hole.IsIntersecting(clipPolygon))
                    {
                        // CombineLoop().Count() == 1 is always true in this case
                        newPolygon = newPolygon.Combine(hole, false).First();
                    }

                }

                newClipPolygons.Add(newPolygon);
            }

            List<Polygon> newPolygons = new List<Polygon>();
            newPolygons.AddRange(polygonsBelow);
            newPolygons.AddRange(newClipPolygons);

            return new PolygonFace(newPolygons, this);
        }


        public PolygonFace IntersectProjectedFace(PolygonFace face)
        {
            face = Utilities.MathUtils.InTrailingCone(face.Normal, Normal, Math.PI) ? face.Flip() : face;
            GetPolygonsTool(face, out List<Polygon> toolPolygons);

            // List of polygon on boundary of eather PolygonFace, intersection among them need to be 
            PolygonFace newFace = new PolygonFace(this);
            foreach (Polygon toolPolygon in toolPolygons)
            {
                newFace = new PolygonFace(newFace.Combine(toolPolygon, false), this);
            }

            return newFace;
        }


        public bool IsPointOnFace(Point3d point)
        {
            return IsPointOnBoundary(point) || Utilities.MathUtils.IsNeighbour(point, this) &&
                   Utilities.MathUtils.IsNeighbour(Polygons.Select(p => p.WindingNumber(new Utilities.Line(point, Normal))).Sum(), 1.0);
        }


        public bool IsPointOnBoundary(Point3d point)
        {
            return Polygons.Any(p => p.IsPointOnLoop(point));
        }


        public bool IsValid()
        {
            return Polygon.AreValidCoplanarPolygons(Polygons);
        }


        public bool IsEquivalent(PolygonFace polygonFace)
        {
            return polygonFace.Polygons.Count() == Polygons.Count() && polygonFace.Polygons.All(p => Polygons.Any(q => q.IsEquivalent(p)));
        }


        public bool GetIntersection(Utilities.Line line, out Point3d intersection)
        {
            bool intersectPlane = Utilities.MathUtils.GetIntersection(line, this, out intersection);
            if (intersectPlane)
            {
                Point3d i = intersection;
                if (Polygons.Any(p => p.IsPointOnLoop(i, out Utilities.Pair<Point3d> edge)))
                {
                    return true;
                }

                int nbHoleLoopCirculing = Polygons.Select(p => Utilities.MathUtils.Dot(p.Normal(), Normal) < 0 && p.IsLoopCirculingLine(line)).Count();
                int nbFaceLoopCirculing = Polygons.Select(p => Utilities.MathUtils.Dot(p.Normal(), Normal) > 0 && p.IsLoopCirculingLine(line)).Count();

                return nbFaceLoopCirculing - nbHoleLoopCirculing >= 1;
            }
            else
            {
                return false;
            }
        }


        public PolygonFace Flip()
        {
            return Flip(Utilities.MathUtils.Multiply(Normal, -1.0));
        }


        public PolygonFace Flip(Vector3d normal)
        {
            if (Utilities.MathUtils.Dot(Normal, normal) < 0.0)
            {
                return new PolygonFace(Polygon.GetReversed(Polygons), Origin, Utilities.MathUtils.Multiply(Normal, -1.0));
            }

            return new PolygonFace();
        }


        public Polygon ProjectOn(Polygon polygon)
        {
            return new Polygon(polygon.ProjectToPlane(this));
        }


        public List<Polygon> ProjectOn(IEnumerable<Polygon> polygons)
        {
            return polygons.Select(p => ProjectOn(p)).ToList();
        }


        public PolygonFace ProjectOn(PolygonFace face)
        {
            return new PolygonFace(ProjectOn(face.Polygons)).Flip(Normal);
        }


        public PolygonFace Move(Vector3d direction)
        {
            Point3d[] points = GetPoints();

            return new PolygonFace(points.Select(p => Utilities.MathUtils.Add(p, direction)).ToArray());
        }


        private void OrientPolygons(Vector3d normal)
        {
            foreach (Polygon polygon in Polygons)
            {
                if (Utilities.MathUtils.AreCodirectional(polygon.Normal(), normal) != (EnclosingNumber(polygon) % 2 == 0))
                {
                    polygon.Reverse();
                }
            }
        }


        private int EnclosingNumber(Polygon polygon)
        {
            return Polygon.EnclosingNumber(polygon, Polygons);
        }


        private Vector3d CalculateVectorArea()
        {
            return Utilities.MathUtils.Add(Polygons.Select(p => p.GetVectorArea()).ToArray());
        }


        private void CalculateCentroid(out Point3d centroid, out Vector3d area)
        {
            centroid = new Point3d();
            area = new Vector3d();

            Vector3d normal = Utilities.MathUtils.UnitVector(Normal);
            foreach (Polygon polygon in Polygons)
            {
                Vector3d pArea = polygon.GetVectorArea();
                Point3d areaCentroid = Utilities.MathUtils.Multiply(polygon.GetCentroid(), Utilities.MathUtils.Dot(normal, pArea));

                centroid = Utilities.MathUtils.Add(centroid, areaCentroid);
                area = Utilities.MathUtils.Add(area, pArea);
            }

            centroid = Utilities.MathUtils.Multiply(centroid, 1.0 / Utilities.MathUtils.Length(area));
        }


        private void CalculateInertia(out Utilities.SymTensor inertia, out Point3d centroid, out Vector3d area)
        {
            CalculateCentroid(out centroid, out area);
            inertia = new Utilities.SymTensor();

            Vector3d normal = Utilities.MathUtils.UnitVector(Normal);
            foreach (Polygon polygon in Polygons)
            {
                Utilities.SymTensor pInertia = polygon.GetInertia();
                Point3d pCentroid = polygon.GetCentroid();
                Vector3d pArea = polygon.GetVectorArea();

                double pSignedArea = Utilities.MathUtils.Dot(normal, pArea);
                Utilities.SymTensor pAdd = Utilities.Inertia.GetInertia(Utilities.MathUtils.GetVector(centroid, pCentroid), pSignedArea);
                inertia = inertia.Add(pInertia.Multiply(Math.Sign(pSignedArea)), pAdd);
            }
        }


        private List<Polygon> Combine(Polygon toolPolygon, bool union = false)
        {
            List<Polygon> intersectedPolygons = new List<Polygon>() { toolPolygon };
            foreach (Polygon polygon in Polygons)
            {
                List<Polygon> newIntersectedPolygons = new List<Polygon>(intersectedPolygons);
                foreach (Polygon newToolPolygon in intersectedPolygons)
                {
                    newIntersectedPolygons.RemoveAll(p => p.IsEquivalent(newToolPolygon));

                    // If polygon is a face
                    if (Utilities.MathUtils.InSolidAngle(Normal, polygon.Normal()))
                    {
                        newIntersectedPolygons.AddRange(polygon.Combine(newToolPolygon, union, Normal));
                    }
                    else
                    {
                        newIntersectedPolygons.AddRange(newToolPolygon.Combine(polygon, union, Normal));
                    }
                }
                intersectedPolygons = newIntersectedPolygons;
            }

            return intersectedPolygons;
        }


        private void GetPolygonsTool(PolygonFace face, out List<Polygon> toolPolygons)
        {
            List<Polygon> facePolygon = face.Polygons;
            if (Utilities.MathUtils.InTrailingCone(face.Normal, Normal, Math.PI))
            {
                facePolygon = Polygon.GetReversed(facePolygon);
            }

            toolPolygons = ProjectOn(facePolygon);
        }


        public List<Polygon> Polygons { get; private set; }
        public Face NXFace { get; private set; }

        private bool m_isValidArea;
        private Vector3d m_area;
        private bool m_isValidCentroid;
        private Point3d m_centroid;
        private bool m_isValidInertia;
        private Utilities.SymTensor m_inertia;
    }
}
