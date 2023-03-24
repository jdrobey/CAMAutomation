using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class BoundingBox
    {
        public static BoundingBox ComputeBodyBoundingBox(Body body, CartesianCoordinateSystem csys = null)
        {
            try
            {
                if (csys != null)
                {
                    BoundingBox box = new BoundingBox(body, csys);
                    return box.IsValid ? box : null;
                }

                List<Vector3d> normals = new List<Vector3d>();

                foreach (Face face in body.GetFaces())
                {
                    if (face.SolidFaceType == Face.FaceType.Planar)
                    {
                        Vector3d normal = Utilities.NXOpenUtils.GetFaceNormal(face);
                        {
                            // Is the normal parallel to any normal already computed ?
                            // If not, add it to the list
                            if (!normals.Any(p => Utilities.MathUtils.InDoubleCone(p, normal)))
                            {
                                normals.Add(normal);
                            }
                        }
                    }
                }

                List<BoundingBox> boxes = new List<BoundingBox>();
                for (int i = 0; i < normals.Count - 1; ++i)
                {
                    for (int j = i + 1; j < normals.Count; ++j)
                    {
                        Point3d origin = new Point3d();
                        Vector3d V1 = normals[i];
                        Vector3d V2 = normals[j];

                        // Create the 2 possible coordinate system with V1, V2
                        CartesianCoordinateSystem csys1 = Utilities.NXOpenUtils.CreateTemporaryCsys(origin, V1, V2);
                        CartesianCoordinateSystem csys2 = Utilities.NXOpenUtils.CreateTemporaryCsys(origin, V2, V1);

                        if (csys1 != null && csys2 != null)
                        {
                            CartesianCoordinateSystem[] csyss = new CartesianCoordinateSystem[] { csys1, csys2 };

                            for (int k = 0; k < csyss.Length; k++)
                            {
                                boxes.Add(new BoundingBox(body, csyss[k]));
                            }
                        }
                    }
                }

                // Select the bounding box having the minimal volume (from valid ones)
                return boxes.Where(p => p.IsValid).OrderBy(q => q.GetVolume()).FirstOrDefault();
            }
            catch (Exception)
            {
                // empty
            }

            return null;
        }


        public static BoundingBox ComputeFaceBoundingBox(Face face, CartesianCoordinateSystem csys = null, int n = 3)
        {
            try
            {
                if (csys != null)
                {
                    BoundingBox box = new BoundingBox(face, csys);
                    return box.IsValid ? box : null;
                }

                // Get face directions
                Vector3d[] directions = Utilities.NXOpenUtils.GetFaceDirections(face, n);

                List<BoundingBox> boxes = new List<BoundingBox>();
                foreach (Vector3d vector1 in directions)
                {
                    foreach (Vector3d vector2 in directions.Reverse().TakeWhile(p => !Utilities.MathUtils.IsNeighbour(p, vector1)))
                    {
                        CartesianCoordinateSystem tempCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(new Point3d(), vector1, vector2);
                        boxes.Add(new BoundingBox(face, tempCsys));
                    }
                }

                return boxes.Where(p => p.IsValid).OrderBy(q => q.GetVolume()).FirstOrDefault();
            }
            catch (Exception)
            {
                // empty
            }

            return null;
        }


        public static BoundingBox ComputeFaceBoundingBoxAlongVector(Face face, Vector3d vector, int n = 3)
        {
            // get optimise box in x dirrection
            Vector3d[] directions = Utilities.NXOpenUtils.GetFaceDirections(face, n)
                                                         .Where(p => !Utilities.MathUtils.InDoubleCone(p, vector))
                                                         .ToArray();

            Func<Vector3d, Vector3d, bool> SpanSamePlane = (u, v) => Utilities.MathUtils.InDoubleCone(Utilities.MathUtils.Cross(u, vector), Utilities.MathUtils.Cross(v, vector));
            directions = Utilities.SetTheoryUtils.Representatives(directions, SpanSamePlane).ToArray();

            List<BoundingBox> boxes = new List<BoundingBox>();
            foreach (Vector3d direction in directions)
            {
                CartesianCoordinateSystem tempCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(new Point3d(), vector, direction);
                boxes.Add(new BoundingBox(face, tempCsys));
            }

            return boxes.Where(p => p.IsValid).OrderBy(q => q.GetVolume()).FirstOrDefault();
        }


        public static BoundingBox CreateBoundingBox(Point3d origin, Vector3d x, Vector3d y, Vector3d z)
        {
            BoundingBox box = new BoundingBox(origin, x, y, z);

            return box.IsValid ? box : null;
        }


        private BoundingBox(NXObject nxObject, CartesianCoordinateSystem csys)
        {
            try
            {
                double[] min_corner = new double[3];
                double[,] directions = new double[3, 3];
                double[] distances = new double[3];

                Utilities.RemoteSession.UFSession.Modl.AskBoundingBoxExact(nxObject.Tag, csys.Tag, min_corner, directions, distances);

                // Min Corner Point
                MinCornerPoint = new Point3d(min_corner[0], min_corner[1], min_corner[2]);

                // Bounding Box Dimensions
                XLength = distances[0];
                YLength = distances[1];
                ZLength = distances[2];

                // Bounding Box Direction
                XUnitDirection = new Vector3d(directions[0, 0], directions[0, 1], directions[0, 2]);
                YUnitDirection = new Vector3d(directions[1, 0], directions[1, 1], directions[1, 2]);
                ZUnitDirection = new Vector3d(directions[2, 0], directions[2, 1], directions[2, 2]);

                // Max Corner Point
                MaxCornerPoint = GetBoxPoint(1.0, 1.0, 1.0);

                IsValid = true;
            }
            catch
            {
                IsValid = false;
            }
        }


        private BoundingBox(Point3d origin, Vector3d x, Vector3d y, Vector3d z)
        {
            // Min Corner Point
            MinCornerPoint = origin;

            // Bounding Box Dimensions
            XLength = Utilities.MathUtils.Length(x);
            YLength = Utilities.MathUtils.Length(y);
            ZLength = Utilities.MathUtils.Length(z);
            
            // Bounding Box Direction
            XUnitDirection = Utilities.MathUtils.UnitVector(x);
            YUnitDirection = Utilities.MathUtils.UnitVector(y);
            ZUnitDirection = Utilities.MathUtils.UnitVector(z);

            // Max Corner Point
            MaxCornerPoint = GetBoxPoint(1.0, 1.0, 1.0);

            // Valit if it forme a rigth handet CSYS
            IsValid = Utilities.MathUtils.IsNeighbour(Utilities.MathUtils.Dot(Utilities.MathUtils.Cross(XUnitDirection,YUnitDirection), ZUnitDirection), 1.0);
        }


        ~BoundingBox()
        {
            // empty
        }


        public double GetVolume()
        {
            return XLength * YLength * ZLength;
        }


        public Point3d[] GetCornerPoints()
        {
            List<Point3d> corners = new List<Point3d>();

            for (int i = 0; i < 2; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    for (int k = 0; k < 2; ++k)
                    {
                        corners.Add(GetBoxPoint(i, j, k));
                    }
                }
            }

            return corners.ToArray();
        }


        public Point3d GetBoxCenter()
        {
            return GetBoxPoint(0.5, 0.5, 0.5);
        }


        public PolygonFace[] GetFaces()
        {
            return new PolygonFace[]
            {
                new PolygonFace(new Point3d[]{GetBoxPoint(0.0, 0.0, 0.0), GetBoxPoint(0.0, 0.0, 1.0), GetBoxPoint(0.0, 1.0, 1.0), GetBoxPoint(0.0, 1.0, 0.0)},
                                GetBoxPoint(0.0,0.5,0.5), Utilities.MathUtils.Multiply(XUnitDirection, -1.0)),
                new PolygonFace(new Point3d[]{GetBoxPoint(0.0, 0.0, 0.0), GetBoxPoint(1.0, 0.0, 0.0), GetBoxPoint(1.0, 0.0, 1.0), GetBoxPoint(0.0, 0.0, 1.0)},
                                GetBoxPoint(0.5,0.0,0.5), Utilities.MathUtils.Multiply(YUnitDirection, -1.0)),
                new PolygonFace(new Point3d[]{GetBoxPoint(0.0, 0.0, 0.0), GetBoxPoint(0.0, 1.0, 0.0), GetBoxPoint(1.0, 1.0, 0.0), GetBoxPoint(1.0, 0.0, 0.0)},
                                GetBoxPoint(0.5,0.5,0.0), Utilities.MathUtils.Multiply(ZUnitDirection, -1.0)),
                new PolygonFace(new Point3d[]{GetBoxPoint(1.0, 0.0, 0.0), GetBoxPoint(1.0, 1.0, 0.0), GetBoxPoint(1.0, 1.0, 1.0), GetBoxPoint(1.0, 0.0, 1.0)},
                                GetBoxPoint(1.0,0.5,0.5), XUnitDirection),
                new PolygonFace(new Point3d[]{GetBoxPoint(0.0, 1.0, 0.0), GetBoxPoint(0.0, 1.0, 1.0), GetBoxPoint(1.0, 1.0, 1.0), GetBoxPoint(1.0, 1.0, 0.0)},
                                GetBoxPoint(0.5,1.0,0.5), YUnitDirection),
                new PolygonFace(new Point3d[]{GetBoxPoint(0.0, 0.0, 1.0), GetBoxPoint(1.0, 0.0, 1.0), GetBoxPoint(1.0, 1.0, 1.0), GetBoxPoint(0.0, 1.0, 1.0)},
                                GetBoxPoint(0.5,0.5,1.0), ZUnitDirection)
            };
        }


        public Point3d GetBoxPoint(double xfraction, double yfraction, double zfraction)
        {
            return Utilities.MathUtils.Add(MinCornerPoint,
                                           Utilities.MathUtils.Multiply(XUnitDirection, xfraction * XLength),
                                           Utilities.MathUtils.Multiply(YUnitDirection, yfraction * YLength),
                                           Utilities.MathUtils.Multiply(ZUnitDirection, zfraction * ZLength));
        }


        public Vector3d[] GetAxis()
        {
            return new Vector3d[] { XUnitDirection, YUnitDirection, ZUnitDirection };
        }


        public double[] GetFraction(Point3d point)
        {
            Vector3d pointVector = Utilities.MathUtils.GetVector(MinCornerPoint, point);

            return new double[]{ Utilities.MathUtils.Dot(pointVector, XUnitDirection)/XLength,
                                 Utilities.MathUtils.Dot(pointVector, YUnitDirection)/YLength,
                                 Utilities.MathUtils.Dot(pointVector, ZUnitDirection)/ZLength};
        }


        public bool IsPointInBox(Point3d point)
        {
            double[] fraction = GetFraction(point);

            return 0.0 - Utilities.MathUtils.ABS_TOL <= fraction[0] && fraction[0] <= 1.0 + Utilities.MathUtils.ABS_TOL &&
                   0.0 - Utilities.MathUtils.ABS_TOL <= fraction[1] && fraction[1] <= 1.0 + Utilities.MathUtils.ABS_TOL &&
                   0.0 - Utilities.MathUtils.ABS_TOL <= fraction[2] && fraction[2] <= 1.0 + Utilities.MathUtils.ABS_TOL;
        }


        public bool IsPointOnBox(Point3d point)
        {
            return IsPointOnXBoundary(point) || IsPointOnYBoundary(point) || IsPointOnZBoundary(point);
        }


        public bool IsPointOnXBoundary(Point3d point)
        {
            double[] fraction = GetFraction(point);

            return (Math.Abs(fraction[0]) <= Utilities.MathUtils.ABS_TOL || Math.Abs(fraction[0] - 1) <= Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[1] && fraction[1] <= 1.0 + Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[2] && fraction[2] <= 1.0 + Utilities.MathUtils.ABS_TOL);
        }


        public bool IsPointOnYBoundary(Point3d point)
        {
            double[] fraction = GetFraction(point);

            return (Math.Abs(fraction[1]) <= Utilities.MathUtils.ABS_TOL || Math.Abs(fraction[1] - 1) <= Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[0] && fraction[0] <= 1.0 + Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[2] && fraction[2] <= 1.0 + Utilities.MathUtils.ABS_TOL);
        }


        public bool IsInscribable(BoundingBox other)
        {
            double[] otherLengths = { other.XLength, other.YLength, other.ZLength };
            double[] currentLengths = { XLength, YLength, ZLength };

            otherLengths = otherLengths.OrderBy(p => p).ToArray();
            currentLengths = currentLengths.OrderBy(p => p).ToArray();

            // All lengths of other Bpounding Box should be greater than the lengths of current Bounding Box
            if ((otherLengths[0] - currentLengths[0]) > -Utilities.MathUtils.ABS_TOL &&
                (otherLengths[1] - currentLengths[1]) > -Utilities.MathUtils.ABS_TOL &&
                (otherLengths[2] - currentLengths[2]) > -Utilities.MathUtils.ABS_TOL)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        public bool IsPointOnZBoundary(Point3d point)
        {
            double[] fraction = GetFraction(point);

            return (Math.Abs(fraction[2]) <= Utilities.MathUtils.ABS_TOL || Math.Abs(fraction[2] - 1) <= Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[0] && fraction[0] <= 1.0 + Utilities.MathUtils.ABS_TOL) &&
                   (0.0 - Utilities.MathUtils.ABS_TOL <= fraction[1] && fraction[1] <= 1.0 + Utilities.MathUtils.ABS_TOL);
        }


        public Utilities.Plane[] IntersectingBoundaries(Utilities.Line line)
        {
            return GetFaces().Where(p => Utilities.MathUtils.GetIntersection(line, p, out Point3d point) && 
                                         IsPointInBox(point)).ToArray();
        }


        public Utilities.Plane[] IntersectingBoundaries(Utilities.Plane plane)
        {
            // Retrieve planes
            Utilities.Plane[] planes = GetFaces();

            // Retrieve the intersection lines between each boundary plane and the input plane
            Utilities.Line[] intersections = planes.Select(p => {Utilities.MathUtils.GetIntersection(p, plane, out Utilities.Line line); return line;})
                                                   .Where(p => p != null)
                                                   .ToArray();

            // Return only planes that intersect any of the intersection lines
            return planes.Where(p => intersections.Any(q => Utilities.MathUtils.GetIntersection(q, p, out Point3d point) && 
                                                            IsPointInBox(point))).ToArray();
        }


        public Vector3d[] ClosestDistance(Point3d point, Vector3d[] directions = null)
        {
            return OptimalDistance(point, directions, Comparer<double>.Create((d1, d2) => d1.CompareTo(d2)));
        }


        public Vector3d[] FarthestDistance(Point3d point, Vector3d[] directions = null)
        {
            return OptimalDistance(point, directions, Comparer<double>.Create((d1, d2) => d2.CompareTo(d1)));
        }


        private Vector3d[] OptimalDistance(Point3d point, Vector3d[] directions, IComparer<double> comparer)
        {
            // If no directions are specified, use the directions of the box
            Vector3d[] vectors;
            if (directions != null)
            {
                vectors = GetFaces()
                              .SelectMany(p => directions, (p, direction) =>
                              {
                                  Utilities.Line line = new Utilities.Line(point, direction);
                                  bool doIntersect = Utilities.MathUtils.GetIntersection(line, p, out Point3d intersection);
                                  return new
                                  {
                                      DoIntersect = doIntersect,
                                      Intersection = intersection,
                                      Vector = Utilities.MathUtils.GetVector(point, intersection)
                                  };
                              }
                                         )
                              .Where(p => p.DoIntersect && IsPointInBox(p.Intersection))
                              .Select(p => p.Vector)
                              .ToArray();
            }
            else
            {
                // The minimal or maximal distance will occur normal to the box plane or with the box corner
                // We retrieve all the plane vectors as well as the corner vectors and we concatenate all vectors in one array
                Vector3d[] planeVector = GetFaces().Select(p => Utilities.MathUtils.GetDistanceVector(point, p)).ToArray();
                Vector3d[] cornerVectors = GetCornerPoints().Select(p => Utilities.MathUtils.GetVector(point, p)).ToArray();

                vectors = planeVector.Concat(cornerVectors).ToArray();
            }


            // We start by ordering the vector with respect to their length
            vectors = vectors.OrderBy(p => Utilities.MathUtils.Length(p), comparer).ToArray();

            // Then, we keep only the vectors that have the optimal distance, i.e., distance equal to the first item in the array
            if (vectors.Length != 0)
            {
                vectors.Where(p => Math.Abs(Utilities.MathUtils.Length(p) - Utilities.MathUtils.Length(vectors.First())) < Utilities.MathUtils.ABS_TOL).ToArray();
            }

            return vectors;
        }


        public Point3d MinCornerPoint { get; }
        public Point3d MaxCornerPoint { get; }

        public double XLength { get; }
        public double YLength { get; }
        public double ZLength { get; }

        public Vector3d XUnitDirection { get; }
        public Vector3d YUnitDirection { get; }
        public Vector3d ZUnitDirection { get; }

        private bool IsValid { get; }
    }

}
