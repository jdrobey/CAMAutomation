using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class Intersection
    {
        public Intersection(Point3d intersection, Vector3d normal)
        {
            Point = intersection;
            Normal = normal;
        }


        ~Intersection()
        {
            // empty
        }


        public bool IsEquivalent(Intersection other)
        {
            return other != null &&
                   Utilities.MathUtils.AreCoincident(Point, other.Point) &&
                   Utilities.MathUtils.AreCodirectional(Normal, other.Normal);
        }


        public bool IsAntiEquivalent(Intersection other)
        {
            return other != null &&
                   Utilities.MathUtils.AreCoincident(Point, other.Point) &&
                   Utilities.MathUtils.AreAntiparallel(Normal, other.Normal);
        }

        public Point3d Point { get; }
        public Vector3d Normal { get; }
    }


    public class PointLoop : CircularList<Point3d>
    {
        public static List<PointLoop> GetPointLoops(Face face, int nbOfPoints = 8)
        {
            List<PointLoop> pointFace = new List<PointLoop>();
            List<Edge> remainingEdges = face.GetEdges().ToList();
            PointLoop points = new PointLoop();
            Edge firstEdge = remainingEdges.First();
            firstEdge.GetVertices(out Point3d startPoint, out Point3d endPoint);
            startPoint = endPoint;

            bool loopSegmentComplete = false;
            Edge edge = firstEdge;
            do
            {
                edge.GetVertices(out Point3d p1, out Point3d p2);
                if (!Utilities.MathUtils.AreCoincident(p1, endPoint))
                {
                    Utilities.MathUtils.Swap(ref p1, ref p2);
                }

                if (!loopSegmentComplete)
                {
                    points.Add(p1);
                    points.AddInternalEdgePoints(edge, p1, nbOfPoints);
                }

                endPoint = p2;
                remainingEdges.Remove(edge);
                edge = Utilities.NXOpenUtils.NextEdge(face, edge, endPoint);

                loopSegmentComplete = (edge == null || Utilities.MathUtils.AreCoincident(endPoint, startPoint));

                if (loopSegmentComplete && !Utilities.MathUtils.AreCoincident(points.First(), endPoint))
                {
                    points.Add(endPoint);
                }

                //case with remaining edges after the first closed polygon is created
                if (loopSegmentComplete && remainingEdges.Count != 0)
                {
                    // Complexe loop
                    List<Edge> loopForgottenEdges = remainingEdges.Where(p => points.Any(q => Utilities.NXOpenUtils.IsEdgeVertex(p, q))).ToList();
                    if (loopForgottenEdges.Count() > 0)
                    {
                        //loopForgottenEdges
                        edge = loopForgottenEdges.First();
                        endPoint = points.Where(p => Utilities.NXOpenUtils.IsEdgeVertex(edge, p)).First();
                        startPoint = endPoint;
                        points.RotateTo(startPoint);
                        points.Rotate();
                    }
                    else
                    {
                        pointFace.Add(points);

                        // Reinitialyse for Single or Multiple loop
                        loopSegmentComplete = false;
                        points = new PointLoop();
                        edge = remainingEdges.First();
                        edge.GetVertices(out startPoint, out endPoint);
                        firstEdge = edge;
                        startPoint = endPoint;
                    }
                }

            } while (remainingEdges.Count != 0);

            pointFace.Add(points);
            return pointFace;
        }


        public PointLoop() : base()
        {
            // empty
        }


        public PointLoop(int capacity) : base(capacity)
        {
            // empty
        }


        public PointLoop(params Point3d[] collection) : base(collection)
        {
            Clean();
        }


        public PointLoop(IEnumerable<Point3d> collection) : base(collection)
        {
            Clean();
        }


        public PointLoop(Polygon other) : base(other.Select(p => new Point3d(p.X, p.Y, p.Z)).ToArray())
        {
            // empty
        }


        ~PointLoop()
        {
            // empty
        }


        public bool IsValid()
        {
            // Should be not self intersecting also
            return Count >= 3;
        }


        public List<PointLoop> CombineLoop(PointLoop loop2, bool useToolLoopAtEntry)
        {
            if (IsEquivalent(loop2))
            {
                return new List<PointLoop>() { new PointLoop(this) };
            }

            List<PointLoop> loops = new List<PointLoop>();
            foreach (CircularList<Intersection> rotatedIntersections in RotatedIntersectionList(GetIntersections(loop2), useToolLoopAtEntry))
            {
                PointLoop loop = new PointLoop(this);

                loop.UpdateLoop(loop2, rotatedIntersections, useToolLoopAtEntry);
                if (loop.Count() >= 3)
                {
                    loops.Add(loop);
                }
            }

            return Utilities.SetTheoryUtils.Representatives(loops, AreEquivalent).ToList();
        }


        public Vector3d Normal()
        {
            return NormalAt();
        }


        public Point3d Origin()
        {
            return base[0];
        }


        public Vector3d NormalAt(int i = 0)
        {
            if (this.Count() >= 3)
            {
                IsConvexAtIndex(i, out Vector3d normal);

                return normal;
            }

            return new Vector3d();
        }


        public Vector3d NormalAt(Point3d point)
        {
           return NormalAt(IndexOf(point));
        }


        public bool IsCoplanar()
        {
            Utilities.Plane plane = new Utilities.Plane(Origin(), Normal());
            return this.All(p => Utilities.MathUtils.IsNeighbour(p, plane));
        }


        public bool IsSelfIntersecting()
        {
            return GetIntersections(this).Count() > 0;
        }


        public bool IsConvexAtIndex(int i, out Vector3d normal)
        {
            Vector3d vector1 = Utilities.MathUtils.GetVector(this[i], this[i + 1]);
            Vector3d vector2 = Utilities.MathUtils.GetVector(this[i], this[i - 1]);

            normal = Utilities.MathUtils.Cross(vector1, vector2);
            PointLoop newPointLoop = new PointLoop(this);
            newPointLoop.RemoveAt(i);

            bool isConvex = !newPointLoop.IsLoopCirculingLine(new Utilities.Line(this[i], normal));

            normal = (isConvex) ? normal : Utilities.MathUtils.Inverse(normal);

            return isConvex;
        }


        public CircularList<Intersection> GetIntersections(PointLoop loop2)
        {
            CircularList<Intersection> intersections = new CircularList<Intersection>();
            int i = 0;
            int j = 0;

            RecursivelyAddIntersections(new PointLoop(loop2), ref intersections, ref i, ref j);

            return intersections;
        }


        public void Clip(Utilities.Plane plane)
        {
            bool addIntersection = false;
            for (int i = 0; i <= this.Count() + 1; i++)
            {
                if (addIntersection && Utilities.MathUtils.GetSignedDistance(this[i - 1], plane) > Utilities.MathUtils.ABS_TOL)
                {
                    RemoveAt(--i);
                    addIntersection = false;
                }
                if (plane.IsBetweenPoints(this[i], this[i + 1]))
                {
                    Utilities.MathUtils.GetIntersection(new Utilities.Line(this[i], this[i + 1]), plane, out Point3d intersection);
                    InsertAfter(i, intersection);
                    addIntersection = true;
                }
            }
        }


        public PointLoop GetClip(Utilities.Plane plane)
        {
            PointLoop loop = new PointLoop(this);
            loop.Clip(plane);

            return loop;
        }


        public bool IsPlaneIntersect(Utilities.Plane plane)
        {
            for (int i = 0; i <= this.Count() + 1; i++)
            {
                if (plane.IsBetweenPoints(this[i], this[i + 1]))
                {
                    return true;
                }
            }

            return false;
        }


        public bool IsAbovePlane(Utilities.Plane plane)
        {
            if (!IsPlaneIntersect(plane) && Utilities.MathUtils.GetSignedDistance(this[0], plane) > 0.0)
            {
                return true;
            }

            return false;
        }


        public bool IsBelowPlane(Utilities.Plane plane)
        {
            if (!IsPlaneIntersect(plane) && Utilities.MathUtils.GetSignedDistance(this[0], plane) < 0.0)
            {
                return true;
            }

            return false;
        }


        public bool IsPointOnLoop(Point3d point)
        {
            return IsPointOnLoop(point, out Utilities.Pair<Point3d> edge);
        }


        public bool IsPointOnLoop(Point3d point, out Utilities.Pair<Point3d> edge)
        {
            int index = this.Count() - 1;
            for (int i = 0; i < this.Count(); i++)
            {
                if (Utilities.MathUtils.IsPointOnLeftOpenEdge(point, this[index], this[i]))
                {
                    edge = new Utilities.Pair<Point3d>(this[index], this[i]);
                    return true;
                }

                index = i;
            }

            edge = null;
            return false;
        }


        public bool IsPointOnVertex(Point3d point)
        {
            return this.Any(p => AreEquivalent(p, point));
        }


        public bool GetIntersections(Utilities.Line line, out List<Point3d> intersections)
        {
            intersections = new List<Point3d>();
            int index = this.Count() - 1;
            for (int i = 0; i < this.Count(); i++)
            {
                Utilities.Line edgeLine = new Utilities.Line(this[index], this[i]);
                if (Utilities.MathUtils.GetIntersection(edgeLine, line, out Point3d point) && 
                    Utilities.MathUtils.IsPointOnLeftOpenEdge(point, this[index], this[i]))
                {
                    intersections.Add(point);
                }

                index = i;
            }

            return intersections.Count() > 0;
        }


        public bool IsTouching(PointLoop loop2)
        {
            return IsIntersecting(loop2) || IsTouchingBoundary(loop2) || loop2.IsIntersecting(this) || loop2.IsTouchingBoundary(this);
        }


        public bool IsIntersecting(PointLoop loop2)
        {
            return ContainEntryAndExit(GetIntersections(loop2), out Intersection firstIntersection, out Intersection nextIntersection);
        }


        public bool IsTouchingBoundary(PointLoop loop2)
        {
            return this.Any(p => new PointLoop(loop2).IsPointOnLoop(p, out Utilities.Pair<Point3d> edge));
        }


        public bool IsCurvatureAngleAbove(double angle)
        {
            return !this.Select(p => IndexOf(p)).Any(q => IsCurvatureAngleAbove(q, angle));
        }


        public bool IsCurvatureAngleAbove(int i, double angle)
        {
            return this.All(p => Utilities.MathUtils.InSolidAngle(NormalAt(IndexOf(p)), NormalAt(i), angle));
        }


        public bool IsLoopCirculingLine(Utilities.Line line)
        {
            double windingNumber = WindingNumber(line);
            return Utilities.MathUtils.IsNeighbour(windingNumber, 1.0) || Utilities.MathUtils.IsNeighbour(windingNumber, -1.0);
        }


        public double WindingNumber(Utilities.Line line)
        {
            double windingNumber = 0.0;
            for (int i = 0; i < this.Count(); i++)
            {
                Vector3d r = NormalTo(Utilities.MathUtils.GetVector(line.Origin, this[i]), line.Axis);
                Vector3d nextr = NormalTo(Utilities.MathUtils.GetVector(line.Origin, this[i + 1]), line.Axis);

                if (!(Utilities.MathUtils.Length(r) < Utilities.MathUtils.ABS_TOL ||
                      Utilities.MathUtils.Length(nextr) < Utilities.MathUtils.ABS_TOL ||
                      Utilities.MathUtils.AreCodirectional(r, nextr)))
                {
                    windingNumber += (Utilities.MathUtils.RemapAngleDomain(Utilities.MathUtils.GetAngleAlong(r, nextr, line.Axis))) / (2 * Math.PI);
                }
            }

            return windingNumber;
        }


        public PointLoop ProjectToPlane(Utilities.Plane plan)
        {
            if (plan != null)
            {
                return new PointLoop(this.Select(p => Utilities.MathUtils.Projection(p, plan)));
            }

            return new PointLoop();
        }


        protected override bool AreEquivalent(Point3d point1, Point3d point2)
        {
            return Utilities.MathUtils.IsNeighbour(point1, point2);
        }


        protected void Clean()
        {
            for (int i = 0; i <= this.Count() + 1; i++)
            {
                if (Utilities.MathUtils.IsPointInClosedEdge(this[i], this[i - 1], this[i + 1]))
                {
                    RemoveAt(i--);
                }
            }
        }


        private void AddInternalEdgePoints(Edge edge, Point3d startPoint, int nbOfPoints = 8)
        {
            if (edge.SolidEdgeType != Edge.EdgeType.Linear && nbOfPoints > 1)
            {
                AddRange(Utilities.NXOpenUtils.PointsOnEdge(edge, startPoint, nbOfPoints - 1));
            }
        }


        private List<CircularList<Intersection>> RotatedIntersectionList(CircularList<Intersection> intersections, bool useToolLoopAtEntry)
        {
            return intersections.Where(p => IsToolLoopEntering(p, Normal()) == useToolLoopAtEntry)
                                .Select(p => intersections.GetRotated(intersections.IndexOf(p))).ToList();
        }


        private void UpdateLoop(PointLoop loop2, CircularList<Intersection> intersections, bool useToolLoopAtEntry)
        {
            Clean();

            if (ContainEntryAndExit(intersections, out Intersection firstIntersection, out Intersection nextIntersection))
            {
                // Replare range of point between 
                ReplaceLoopRange(GetRange(loop2, firstIntersection, nextIntersection), firstIntersection, nextIntersection, useToolLoopAtEntry);

                // update intersections for new vertex in loop1
                UpdatedIntersections(this, firstIntersection, nextIntersection, ref intersections);

                // while there is still valid intersection pair continue process
                UpdateLoop(loop2, intersections, useToolLoopAtEntry);
            }
        }


        private bool ContainEntryAndExit(CircularList<Intersection> intersections, out Intersection firstIntersection, out Intersection nextIntersection)
        {
            firstIntersection = intersections.FirstOrDefault();
            nextIntersection = (firstIntersection != null) ? NextOppositeIntersection(intersections) : null;

            return firstIntersection != null && nextIntersection != null;
        }


        private void ReplaceLoopRange(List<Point3d> range, Intersection firstIntersection, Intersection nextIntersection, bool useToolLoopAtEntry)
        {
            GetIntersectionVertices(this, firstIntersection, out Point3d firstLoop1StartPoint, out Point3d firstLoop1EndPoint);
            GetIntersectionVertices(this, nextIntersection, out Point3d nextLoop1StartPoint, out Point3d nextLoop1EndPoint);

            int loop1StartIndex = IndexOf(firstLoop1StartPoint);
            int loop1EndIndex = IndexOf(nextLoop1EndPoint);

            if (AreBackwardsOnEdge(this, firstIntersection, nextIntersection))
            {
                Replace(range);
            }
            else 
            {
                bool isRangeClosed = AreEquivalent(firstIntersection.Point, nextIntersection.Point);
                bool isRangeInside = IsRangeInside(range, firstIntersection); 
                bool isRangeReverse = Utilities.MathUtils.InTrailingCone(GetRangeNormal(range), NormalAt(loop1StartIndex));

                if (isRangeClosed)
                {
                    if (isRangeInside && useToolLoopAtEntry && !isRangeReverse)
                    {
                        Replace(range);
                    }
                    else if (isRangeInside == useToolLoopAtEntry && isRangeInside == isRangeReverse)
                    {
                        ReplaceRange(range, loop1StartIndex, loop1EndIndex);
                    }
                }
                else
                {
                    ReplaceRange(range, loop1StartIndex, loop1EndIndex);
                }
            }

            Clean();
        }


        private bool IsRangeInside(List<Point3d> range, Intersection intersection)
        {
            int n = range.Count() - 1;
            if (n >= 2)
            {
                GetIntersectionVertices(this, intersection, out Point3d edgeStartPoint, out Point3d edgeEndPoint);
                Vector3d normal = NormalAt(edgeStartPoint);

                Vector3d vectorEdge = Utilities.MathUtils.GetVector(edgeStartPoint, edgeEndPoint);
                Vector3d entryRangeNormal = Utilities.MathUtils.Cross(vectorEdge, Utilities.MathUtils.GetVector(range[0], range[1]));
                Vector3d exitRangeNormal = Utilities.MathUtils.Cross(vectorEdge, Utilities.MathUtils.GetVector(range[n], range[n - 1]));

                return Utilities.MathUtils.InSolidAngle(normal, entryRangeNormal, Math.PI) &&
                       Utilities.MathUtils.InSolidAngle(normal, exitRangeNormal, Math.PI);

            }

            return false;
        }


        private Vector3d GetRangeNormal(List<Point3d> range)
        {
            int n = range.Count() - 1;
            if (n >= 2)
            {
                Point3d averageConnection = Utilities.MathUtils.Average(range[0], range[1], range[n], range[n - 1]);

                Vector3d axis = Utilities.MathUtils.Cross(Utilities.MathUtils.GetVector(range[0], range[1]),
                                                          Utilities.MathUtils.GetVector(range[0], averageConnection));
                double windingNumber = (new PointLoop(range)).WindingNumber(new Utilities.Line(averageConnection, axis));

                return Utilities.MathUtils.Multiply(axis, windingNumber);
            }

            return new Vector3d();
        }


        private void UpdatedIntersections(PointLoop loop, Intersection firstIntersection, Intersection nextIntersection, ref CircularList<Intersection> intersections)
        {
            // remove first nexn and intermediate intersection
            intersections.RemoveClosedSlice(intersections.IndexOf(firstIntersection), intersections.IndexOf(nextIntersection));

            // Update intersections
            CircularList<Intersection> newIntersections = new CircularList<Intersection>();
            foreach (Intersection intersection in intersections)
            {
                if (GetIntersectionVertices(loop, intersection, out Point3d startPoint, out Point3d endPoint))
                {
                    newIntersections.Add(intersection);
                }
            }

            intersections = newIntersections;
        }


        private void RecursivelyAddIntersections(PointLoop loop2, ref CircularList<Intersection> intersections, ref int i, ref int j)
        {
            List<Intersection> nextIntersections = GetNextEdgeIntersections(i, j, loop2, intersections, out List<Intersection> rejectedIntersections);

            if (nextIntersections.Count() >= 1)
            {
                CircularList<Intersection> currentIntersections = intersections;
                if (intersections.Count() == 0 || !(nextIntersections.Concat(rejectedIntersections)).Any(p => p.IsEquivalent(currentIntersections.First())))
                {
                    // Add to intersections 
                    intersections.AddRange(nextIntersections);
                    // Add next 
                    Point3d lastIntersectsPoint = intersections.Last().Point;

                    if (GetIntersectionVertices(this, intersections.Last(), out Point3d lastLoop1StartPoint, out Point3d lastLoop1EndPoint) &&
                        GetIntersectionVertices(loop2, intersections.Last(), out Point3d lastLoop2StartPoint, out Point3d lastLoop2EndPoint))
                    {
                        i = IndexOf(lastLoop1StartPoint);
                        j = loop2.Any(p => Utilities.MathUtils.AreCoincident(p, lastIntersectsPoint)) ?
                            loop2.IndexOf(lastIntersectsPoint) : loop2.IndexOf(lastLoop2EndPoint);
                        RecursivelyAddIntersections(loop2, ref intersections, ref i, ref j);
                    }
                    else
                    {

                    }
                }
                else
                {
                    // Add to intersections && end loop
                    intersections.AddRange(nextIntersections.Where(p => !currentIntersections.Any(q => p.IsEquivalent(q))));
                }
            }
        }


        private List<Intersection> GetNextEdgeIntersections(int loop1StartingIndex, 
                                                            int loop2StartingIndex, 
                                                            PointLoop loop2,
                                                            CircularList<Intersection> intersections, 
                                                            out List<Intersection> rejectedEdgeIntersection)
        {
            // return the list of intersections itersecting the next loop2 edge (that contains intersection) after our starting indices
            // out: rejectedEdgeIntersection is the list of filtered out intersections that we all ready know (intersections)
            Point3d intersectionPoint;
            List<Intersection> edgeIntersection = new List<Intersection>();
            rejectedEdgeIntersection = new List<Intersection>();
            for (int j = loop2StartingIndex; j < loop2StartingIndex + loop2.Count(); j++)
            {
                for (int i = loop1StartingIndex; i < loop1StartingIndex + this.Count(); i++)
                {   
                    if (GetIntersectionPoint(this[i], this[i + 1], loop2[j], loop2[j + 1], out intersectionPoint) ||
                        GetCollinearIntersectionPoint(this[i], this[i + 1], loop2[j], loop2[j + 1], out intersectionPoint))
                    {
                        List<Intersection> pointIntersection = ConstructPointIntersections(i, j, loop2, intersectionPoint);
                        
                        if (pointIntersection != null && !intersections.Any(p=> pointIntersection.Any(q => q.IsEquivalent(p))))
                        {
                            if (edgeIntersection.Count() == 0 || pointIntersection.Any(p => IsPointForwardOnEdge(loop2[j], edgeIntersection.Last(), p)))
                            {
                                edgeIntersection.AddRange(pointIntersection);
                            }
                            else
                            {
                                rejectedEdgeIntersection.AddRange(pointIntersection);
                            }
                        }
                    }
                }

                if (edgeIntersection.Count() > 0)
                {
                    return edgeIntersection;
                }
            }

            return edgeIntersection;
        }


        private bool GetIntersectionPoint(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d intersectionPoint)
        {
            Utilities.Line l1 = new Utilities.Line(a1, Utilities.MathUtils.GetVector(a1, a2));
            Utilities.Line l2 = new Utilities.Line(b1, Utilities.MathUtils.GetVector(b1, b2));

            return Utilities.MathUtils.GetIntersection(l1, l2, out intersectionPoint) &&
                   Utilities.MathUtils.IsPointOnLeftOpenEdge(intersectionPoint, a1, a2) &&
                   Utilities.MathUtils.IsPointOnLeftOpenEdge(intersectionPoint, b1, b2);
        }


        private bool GetCollinearIntersectionPoint(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d intersectionPoint)
        {
            Vector3d v1 = Utilities.MathUtils.GetVector(a1, a2);
            Vector3d v2 = Utilities.MathUtils.GetVector(b1, b2);
            Vector3d d12 = Utilities.MathUtils.GetVector(a1, b1);

            bool intersect = Utilities.MathUtils.AreParallel(v1, v2) && Utilities.MathUtils.AreParallel(v1, d12);

            if (intersect && Utilities.MathUtils.IsPointOnLeftOpenEdge(b2, a1, a2))
            {
                intersectionPoint = b2;
            }
            else if (intersect && Utilities.MathUtils.IsPointOnLeftOpenEdge(a2, b1, b2))
            {
                intersectionPoint = a2;
            }
            else
            {
                intersect = false;
                intersectionPoint = new Point3d();
            }

            return intersect;
        }


        private List<Intersection> ConstructPointIntersections(int i, int j, PointLoop loop2, Point3d intersection)
        {
            Vector3d vector1 = Utilities.MathUtils.GetVector(this[i], this[i + 1]);
            Vector3d nextVector1 = Utilities.MathUtils.GetVector(this[i + 1], this[i + 2]);
            Vector3d vector2 = Utilities.MathUtils.GetVector(loop2[j], loop2[j + 1]);
            Vector3d nextVector2 = Utilities.MathUtils.GetVector(loop2[j + 1], loop2[j + 2]);

            // Might be unnecessery with clever refactor 
            bool isDifference = UpdateVectorsForDifference(loop2, intersection, ref vector2, ref nextVector2);
            if (Utilities.MathUtils.AreCoincident(intersection, this[i + 1]) && Utilities.MathUtils.AreCoincident(intersection, loop2[j + 1]))
            {
                // end points AreCoincident
                return CoincidentIntersections(intersection, vector1, vector2, nextVector1, nextVector2, isDifference);
            }
            else if (Utilities.MathUtils.AreCoincident(intersection, this[i + 1]))
            {
                // end2 on line1
                return CoincidentIntersections(intersection, vector1, vector2, nextVector1, vector2, isDifference);
            }
            else if (Utilities.MathUtils.AreCoincident(intersection, loop2[j + 1]))
            {
                // end1 on line2
                return CoincidentIntersections(intersection, vector1, vector2, vector1, nextVector2, isDifference);
            }
            else
            {
                // crossing lines
                return new List<Intersection>() { new Intersection(intersection, Utilities.MathUtils.Cross(vector1, vector2)) };
            }
        }


        private bool UpdateVectorsForDifference(PointLoop toolLoop, Point3d intersection, ref Vector3d vector, ref Vector3d nextVector)
        {
            if (toolLoop.IsPointOnLoop(intersection, out Utilities.Pair<Point3d> edge) && 
                Utilities.MathUtils.Dot(Normal(), toolLoop.Normal()) < 0 && Utilities.MathUtils.AreCoincident(intersection, edge.Two))
            {
                Vector3d tmp = vector;
                vector = nextVector;
                nextVector = tmp;

                return true;
            }

            return false;
        }


        private bool IsToolLoopEntering(Intersection intersection, Vector3d normal)
        {
            return Utilities.MathUtils.Dot(intersection.Normal, normal) > 0; 
        }


        private bool IsPointForwardOnEdge(Point3d point, Intersection lastIntersection, Intersection nextIntersection)
        {
            Vector3d dir = Utilities.MathUtils.GetVector(point, lastIntersection.Point);
            Vector3d nextDir = Utilities.MathUtils.GetVector(lastIntersection.Point, nextIntersection.Point);

            return Utilities.MathUtils.Dot(dir, nextDir) > 0;
        }


        private List<Intersection> CoincidentIntersections(Point3d intersection, Vector3d vector1, Vector3d vector2, Vector3d nextVector1, Vector3d nextVector2, bool isDifference)
        {
            List<Intersection> intersections = new List<Intersection>();

            Vector3d normal = Utilities.MathUtils.Cross(vector1, vector2);
            if (!Utilities.MathUtils.IsZeroVector(normal))
            {
                intersections.Add(new Intersection(intersection, normal));
            }

            Vector3d nextNormal = Utilities.MathUtils.Cross(nextVector1, nextVector2);
            if (!Utilities.MathUtils.IsZeroVector(nextNormal))
            {
                intersections.Add(new Intersection(intersection, nextNormal));
            }

            if (isDifference)
            {
                intersections.Reverse();
            }

            return intersections;
        }


        private Intersection NextOppositeIntersection(CircularList<Intersection> intersections)
        {
            Intersection first = intersections.FirstOrDefault();
            if (first != null)
            {
                intersections.RotateToNext(first, (p, q) => Utilities.MathUtils.Dot(p.Normal, q.Normal) < 0);
                intersections.RotateToNext(intersections.First(), (p, q) => Utilities.MathUtils.Dot(p.Normal, q.Normal) < 0);
                intersections.Rotate(-1);
            }
            Intersection next = intersections.FirstOrDefault(); 

            return !next.IsEquivalent(first) ? next: null;
        }


        private List<Point3d> GetRange(PointLoop loop2, Intersection firstIntersection, Intersection nextIntersection)
        {
            GetIntersectionVertices(loop2, firstIntersection, out Point3d firstLoop2StartPoint, out Point3d firstLoop2EndPoint);
            GetIntersectionVertices(loop2, nextIntersection, out Point3d nextLoop2StartPoint, out Point3d nextLoop2EndPoint);

            Point3d first = new Point3d(firstIntersection.Point.X, firstIntersection.Point.Y, firstIntersection.Point.Z);
            Point3d next = new Point3d(nextIntersection.Point.X, nextIntersection.Point.Y, nextIntersection.Point.Z);

            List <Point3d> range;
            if (AreForwardsOnEdge(loop2, firstIntersection, nextIntersection) || 
                AreForwardsOnEdgeVertex(loop2, firstIntersection, nextIntersection))
            {
                range = new List<Point3d>();
            }
            else
            {
                if (AreEquivalent(first, next))
                {
                    if (loop2.IsPointOnVertex(first))
                    {
                        range = loop2.GetOpenSlice(loop2.IndexOf(first), loop2.IndexOf(first));
                    }
                    else
                    {
                        range = loop2.GetOpenSlice(loop2.IndexOf(firstLoop2StartPoint), loop2.IndexOf(nextLoop2EndPoint));
                    }
                }
                else
                {
                    range = loop2.GetClosedSlice(loop2.IndexOf(firstLoop2EndPoint), loop2.IndexOf(nextLoop2StartPoint));
                }
            }

            range.Insert(0, first);
            range.Add(next);

            return range;
        }


        private bool GetIntersectionVertices(PointLoop loop, Intersection intersection, out Point3d startPoint, out Point3d endPoint)
        {
            List<Point3d> edgeStarts = loop.Where(p => Utilities.MathUtils.IsPointOnLeftOpenEdge(intersection.Point, p, loop.Next(p))).ToList();

            if (edgeStarts.Count() == 1 || edgeStarts.Count() == 2)
            {
                startPoint = edgeStarts.First();
                endPoint = loop.Next(edgeStarts.Last());

                if (loop.Contains(intersection.Point) && !AreEquivalent(endPoint, loop.Next(loop.Next(startPoint))) ||
                    !loop.Contains(intersection.Point) && !AreEquivalent(endPoint, loop.Next(startPoint)))
                {
                    Point3d tmp = startPoint;
                    startPoint = endPoint;
                    endPoint = startPoint;
                }

                return true;
            }

            startPoint = new Point3d();
            endPoint = new Point3d();

            return false;
        }


        private bool AreOnSameEdge(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2, out Point3d edgeStart)
        {
            List<Point3d> edgeStarts = loop.Where(p => Utilities.MathUtils.IsPointOnLeftOpenEdge(pointIntersection1.Point, p, loop.Next(p))).ToList();
            edgeStarts = edgeStarts.Where(p => Utilities.MathUtils.IsPointOnLeftOpenEdge(pointIntersection2.Point, p, loop.Next(p))).ToList();

            if (edgeStarts.Count() == 1)
            {
                edgeStart = edgeStarts.First();
                return true;
            }

            edgeStart = new Point3d();
            return false;
        }


        private bool AreOnSameClosedEdge(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2, out Point3d edgeStart)
        {
            List<Point3d> edgeStarts = loop.Where(p => Utilities.MathUtils.IsPointInClosedEdge(pointIntersection1.Point, p, loop.Next(p))).ToList();
            edgeStarts = edgeStarts.Where(p => Utilities.MathUtils.IsPointInClosedEdge(pointIntersection2.Point, p, loop.Next(p))).ToList();

            if (edgeStarts.Count() == 1)
            {
                edgeStart = edgeStarts.First();
                return true;
            }

            edgeStart = new Point3d();
            return false;
        }


        private bool AreBackwardsOnEdge(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            if (AreOnSameEdge(loop, pointIntersection1, pointIntersection2, out Point3d edgeStart))
            {
               return AreBackwards(edgeStart, pointIntersection1, pointIntersection2);
            }

            return false;
        }


        private bool AreForwardsOnEdge(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            if (AreOnSameEdge(loop, pointIntersection1, pointIntersection2, out Point3d edgeStart))
            {
                return AreForwards(edgeStart, pointIntersection1, pointIntersection2);
            }

            return false;
        }


        private bool AreForwardsOnEdgeVertex(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            if (AreOnSameClosedEdge(loop, pointIntersection1, pointIntersection2, out Point3d edgeStart))
            {
                Point3d edgeEnd = loop.Next(edgeStart);
                return AreEquivalent(edgeStart, pointIntersection1.Point) &&
                       AreEquivalent(edgeEnd, pointIntersection2.Point);
            }

            return false;
        }


        private bool AreBackwards(Point3d edgeStart, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            double d1 = Utilities.MathUtils.GetDistance(edgeStart, pointIntersection1.Point);
            double d2 = Utilities.MathUtils.GetDistance(edgeStart, pointIntersection2.Point);

            return d1 > d2 + Utilities.MathUtils.ABS_TOL;
        }


        private bool AreBackwardsOnEdgeVertex(PointLoop loop, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            if (AreOnSameClosedEdge(loop, pointIntersection1, pointIntersection2, out Point3d edgeStart))
            {
                Point3d edgeEnd = loop.Next(edgeStart);
                return Utilities.MathUtils.IsNeighbour(edgeStart, pointIntersection2.Point) &&
                       Utilities.MathUtils.IsNeighbour(edgeEnd, pointIntersection1.Point);
            }

            return false;
        }


        private bool AreForwards(Point3d edgeStart, Intersection pointIntersection1, Intersection pointIntersection2)
        {
            double d1 = Utilities.MathUtils.GetDistance(edgeStart, pointIntersection1.Point);
            double d2 = Utilities.MathUtils.GetDistance(edgeStart, pointIntersection2.Point);

            return d1 < d2 - Utilities.MathUtils.ABS_TOL;
        }


        private Vector3d NormalTo(Vector3d r, Vector3d axis)
        {
            return Utilities.MathUtils.Substract(r, Utilities.MathUtils.Projection(r, axis));
        }
    }
}