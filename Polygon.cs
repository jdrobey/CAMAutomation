using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class Polygon : PointLoop 
    {
        public static bool AreValidCoplanarPolygons(List<Polygon> polygons)
        {
            // Verify they are all coplanar this ensure every loop has at least 3 points
            return polygons.Count() > 0 && polygons.All(p => p.IsValid()) && polygons.Skip(1).All(p => AreCoplanar(polygons.First(), p));
        }


        public static List<Polygon> GetReversed(List<Polygon> polygons)
        {
            return polygons.Select(p => p.GetReversed()).ToList();
        }


        public static bool AreCoplanar(params Polygon[] polygons)
        {
            Polygon first = polygons.First();
            foreach (Polygon polygon in polygons.Skip(1))
            {
                if (!Utilities.MathUtils.IsNeighbour(first.GetPlane(), polygon.GetPlane()))
                {
                    return false;
                }
            }

            return true;
        }


        public static List<Polygon> ExteriorPolygons(List<Polygon> polygons)
        {
            return polygons.Where(p => EnclosingNumber(p, polygons) == 0).ToList();
        }


        public static int EnclosingNumber(Polygon polygon, List<Polygon> polygons)
        {
            int enclosingNumber = 0;
            foreach (Polygon enclosingPolygon in polygons.Where(p => !p.Equals(polygon)))
            {
                if (enclosingPolygon.IsInside(polygon))
                {
                    enclosingNumber += 1;
                }
            }

            return enclosingNumber;
        }


        public Polygon() : base()
        {
            // empty
        }


        public Polygon(int capacity) : base(capacity)
        {
            // empty
        }


        public Polygon(params Point3d[] collection) : base(collection)
        {
            Clean();
            // A refactor of LoopPoint should allow us to uncomment this line: 
            //Utilities.Debug.ThrowIf(!IsValid(), "Polygon points error in construction");
        }


        public Polygon(IEnumerable<Point3d> collection) : base(collection)
        {
            Clean();
            // A refactor of LoopPoint should allow us to uncomment this line: 
            //Utilities.Debug.ThrowIf(!IsValid(), "Polygon points error in construction");
        }


        public Polygon(Polygon other) : base(other.Select(p => new Point3d(p.X, p.Y, p.Z)).ToArray())
        {
            // empty
        }


        ~Polygon()
        {
            // empty
        }


        public new bool IsValid()
        {
            // Should be not self intersecting also
            return Count >= 3 && IsCoplanar() && !IsSelfIntersecting();
        }


        public new bool IsSelfIntersecting()
        {
            // Once a refactor of LoopPoint should allow self touching Loop This will change  
            return GetIntersections(this).Count() > 0;
        }


        public Polygon GetReversed()
        {
            Polygon reversedPolygon = new Polygon(this);
            reversedPolygon.Reverse();

            return reversedPolygon;
        }


        public List<Polygon> Union(Polygon polygon)
        {
            bool isFace = Utilities.MathUtils.InSolidAngle(Normal(), polygon.Normal());
            bool isOutside = IsOutside(polygon);
            bool isInside = IsInside(polygon);

            // union in this Polygon plane
            List<Polygon> combination = new List<Polygon>();
            if (AreCoplanar(this, polygon))
            {
                // union with a hole is discarded
                if (!isInside && !isOutside && isFace)
                {
                    combination.AddRange(CombineLoop(polygon, false).Select(p => new Polygon(p)));
                }
                else if (isFace && isOutside)
                {
                    combination.Add(this);
                    combination.Add(polygon);
                }
                else
                {
                    combination.Add(polygon);
                }
            }
            else
            {
                combination.Add(this);
            }

            return combination;
        }


        public List<Polygon> Difference(Polygon polygon)
        {
            bool isFace = Utilities.MathUtils.InSolidAngle(Normal(), polygon.Normal());
            bool isOutside = IsOutside(polygon);
            bool isInside = IsInside(polygon);

            Polygon flipPolygon = polygon.GetReversed();

            // difference in this Polygon plane
            List<Polygon> combination = new List<Polygon>();
            if (AreCoplanar(this, polygon))
            {
                // difference with a hole is discarded
                if (!isInside && !isOutside && isFace)
                {
                    combination.AddRange(CombineLoop(flipPolygon, true).Select(p => new Polygon(p)));
                }
                else if (isFace && isInside)
                {
                    combination.Add(this);
                    combination.Add(flipPolygon);
                }
                else
                {
                    combination.Add(this);
                }
            }
            else
            {
                combination.Add(this);
            }

            return combination;
        }


        public List<Polygon> Intersection(Polygon polygon)
        {
            bool isFace = Utilities.MathUtils.InSolidAngle(Normal(), polygon.Normal());
            bool isOutside = IsOutside(polygon);
            bool isInside = IsInside(polygon);

            // intersection in this Polygon plane
            List<Polygon> combination = new List<Polygon>();
            if (AreCoplanar(this, polygon))
            {
                // intersection with a hole is a difference
                if (!isInside && !isOutside)
                {
                    combination.AddRange(CombineLoop(polygon, true).Select(p => new Polygon(p)));
                }
                else if (isFace && isInside)
                {
                    combination.Add(polygon);
                }
                else if (!isFace && isInside)
                {
                    combination.Add(this);
                    combination.Add(polygon);
                }
                else if (!isFace && !isInside)
                {
                    combination.Add(this);
                }
            }

            return combination;
        }


        public List<Polygon> Combine(Polygon polygon, bool union, Vector3d normal = new Vector3d())
        {
            normal = Utilities.MathUtils.IsNeighbour(normal, new Vector3d()) ? Normal() : normal;
            bool isPolygonFace = Utilities.MathUtils.InSolidAngle(normal, polygon.Normal());

            if (union)
            {
                return Union(polygon);
            }
            else if (!union && isPolygonFace)
            {
                return Intersection(polygon);
            }
            else if (!union && !isPolygonFace)
            {
                return Difference(polygon.GetReversed());
            }

            return new List<Polygon>();
        }


        public Polygon Projection(Polygon polygon)
        {
            return new Polygon(polygon.Select(p => Utilities.MathUtils.Projection(p, GetPlane())));
        }


        public bool IsInside(Polygon polygon)
        {
            Utilities.Plane polygonPlane = polygon.GetPlane();
            if (Utilities.MathUtils.IsNeighbour(GetPlane(), polygonPlane))
            {
                List<Utilities.Line> polygonNormarLine = polygon.Select(p => new Utilities.Line(p, polygonPlane.Normal)).ToList();

                return !IsTouching(polygon) && polygonNormarLine.All(p => Utilities.MathUtils.IsNeighbour(Math.Abs(WindingNumber(p)), 1.0));
            }

            return false;
        }


        public bool IsOutside(Polygon polygon)
        {
            Utilities.Plane polygonPlane = polygon.GetPlane();
            if (Utilities.MathUtils.IsNeighbour(GetPlane(), polygonPlane))
            {
                List<Utilities.Line> polygonNormarLine = polygon.Select(p => new Utilities.Line(p, polygonPlane.Normal)).ToList();

                return !IsTouching(polygon) && polygonNormarLine.All(p => Utilities.MathUtils.IsNeighbour(WindingNumber(p), 0.0));
            }
            
            return false;
        }


        public bool IsInside(Point3d point)
        {
            return Utilities.MathUtils.IsNeighbour(point, GetPlane()) && GetOpenIntersection(new Utilities.Line(point, Normal()), out Point3d intersection);
        }


        public bool IsOutside(Point3d point)
        {
            return !Utilities.MathUtils.IsNeighbour(point, GetPlane()) || 
                   !GetOpenIntersection(new Utilities.Line(point, Normal()), out Point3d intersection) && 
                   !IsPointOnLoop(point, out Utilities.Pair<Point3d> edge);
        }


        public bool GetOpenIntersection(Utilities.Line line, out Point3d point)
        {
            return Utilities.MathUtils.GetIntersection(line, GetPlane(), out point) && IsLoopCirculingLine(line);
        }


        public Utilities.Plane GetPlane()
        {
            return new Utilities.Plane(Origin(), Normal());
        }


        public double GetArea()
        {
            return Utilities.MathUtils.Length(GetVectorArea());
        }


        public Vector3d GetVectorArea()
        {
            Vector3d area = new Vector3d();
            Point3d origin = this.First();

            for (int i = 2; i < this.Count(); i++)
            {
                Vector3d tArea = Utilities.Inertia.GetTriangleAreaVector(origin, this[i - 1], this[i]);
                area = Utilities.MathUtils.Add(area, tArea);
            }

            return area;
        }


        public Point3d GetCentroid()
        {
            Vector3d area = new Vector3d();
            Point3d centroid = new Point3d();

            Vector3d normal = Utilities.MathUtils.UnitVector(Normal());
            Point3d origin = this.First();

            for (int i = 2; i < this.Count(); i++)
            {
                Point3d tCenter = Utilities.Inertia.GetTriangleCentroid(origin, this[i - 1], this[i]);
                Vector3d tArea = Utilities.Inertia.GetTriangleAreaVector(origin, this[i - 1], this[i]);

                centroid = Utilities.MathUtils.Add(centroid, Utilities.MathUtils.Multiply(tCenter, Utilities.MathUtils.Dot(normal, tArea)));

                area = Utilities.MathUtils.Add(area, tArea);
            }

            return centroid = Utilities.MathUtils.Multiply(centroid, 1.0 / Utilities.MathUtils.Dot(normal, area));
        }


        public Utilities.SymTensor GetInertia()
        {
            Utilities.SymTensor inertia = new Utilities.SymTensor();

            Point3d centroid = GetCentroid();

            Vector3d normal = Utilities.MathUtils.UnitVector(Normal());
            Point3d origin = this.First();

            for (int i = 2; i < this.Count(); i++)
            {
                Point3d tCenter = Utilities.Inertia.GetTriangleCentroid(origin, this[i - 1], this[i]);
                Vector3d tArea = Utilities.Inertia.GetTriangleAreaVector(origin, this[i - 1], this[i]);
                Utilities.SymTensor tInertia = Utilities.Inertia.GetTriangleInertia(origin, this[i - 1], this[i]);

                double tSignedArea = Utilities.MathUtils.Dot(normal, tArea);
                Utilities.SymTensor tAdd = Utilities.Inertia.GetInertia(Utilities.MathUtils.GetVector(centroid, tCenter), tSignedArea);
                inertia = inertia.Add(tInertia.Multiply(Math.Sign(tSignedArea)), tAdd);
            }

            return inertia;
        }
    }
}