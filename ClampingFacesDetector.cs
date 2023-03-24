using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class ClampingFacesDetector
    {
        public ClampingFacesDetector(Body body, double minClampingArea = 0.0)
        {
            m_body = body;
            m_minClampingArea = minClampingArea;
        }


        public bool ComputeClampingFaces(out string msg, out ClampingFaces[] clampingPairFaces)
        {
            try
            {
                // Compute Bounding Box
                ComputeBoundingBox();

                // Find All polygon face
                FindPlanarBodyPolygonFace();

                // Find parallel face set
                FindParallelClampingPolygonFaceSet();

                // Find planar clamping face pair
                ComputeCandidateClampingPairSet();

                // Compute candidate clamping normals
                ComputeCandidateClampingNormals();

                // Compute candidate bottom plane directions
                ComputeCandidateBottomPlaneDirections();

                // Compute bottom plane candidates
                ComputeCandidateBottomPlanes();

                // Find body Faces bounding plane that intersect with either clamping face of each pair
                clampingPairFaces = CreateClampingFaces();

                msg = String.Empty;

                return true;
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                clampingPairFaces = new ClampingFaces[]{ };

                return false;
            }
        }


        public Vector3d[] GetCandidateClampingNormal()
        {
            return m_candidateClampingNormal.ToArray();
        }


        public Utilities.Pair<PolygonFace>[][] GetCandidateClampingPairSet()
        {
            return Utilities.MathUtils.ToArray(m_candidateClampingPairSet);
        }


        public Vector3d[][] GetCandidateBottomPlaneDirections()
        {
            return Utilities.MathUtils.ToArray(m_candidateBottomPlaneDirections);
        }


        public Utilities.Plane[][] GetCandidateBottomPlanes()
        {
            return Utilities.MathUtils.ToArray(m_candidateBottomPlanes);
        }


        private void ComputeBoundingBox()
        {
            m_boundingBox = BoundingBox.ComputeBodyBoundingBox(m_body);
        }


        private void FindPlanarBodyPolygonFace()
        {
            m_planarPolygonFaces = m_body.GetFaces().Where(p => p.SolidFaceType == Face.FaceType.Planar).Select(p => new PolygonFace(p)).ToList();
        }


        private void FindParallelClampingPolygonFaceSet()
        {
            Utilities.ToleranceDouble compareDouble = new Utilities.ToleranceDouble();
            List<PolygonFace> representatives = Utilities.SetTheoryUtils.Representatives(m_planarPolygonFaces, Utilities.MathUtils.AreParallel)
                                                                        .OrderByDescending(p => p.Normal.X, compareDouble)
                                                                        .ThenByDescending(p => p.Normal.Y, compareDouble)
                                                                        .ThenByDescending(p => p.Normal.Z, compareDouble)
                                                                        .ToList();

            m_parallelClampingFaceSet  = Utilities.SetTheoryUtils.Preimage(m_planarPolygonFaces, Utilities.MathUtils.AreParallel, representatives)
                                                                 .Select(p => p.ToList())
                                                                 .Where(p => p.Count > 1)
                                                                 .ToList();
        }


        private void ComputeCandidateClampingPairSet()
        {
            m_candidateClampingPairSet = new List<List<Utilities.Pair<PolygonFace>>>();
            foreach (IEnumerable<PolygonFace> parallelClampingFaces in m_parallelClampingFaceSet)
            {
                Vector3d xAxis = new Vector3d(1.0, 0.0, 0.0);
                Vector3d yAxis = new Vector3d(0.0, 1.0, 0.0);
                Vector3d zAxis = new Vector3d(0.0, 0.0, 1.0);

                // Split in to left and right set
                IEnumerable<IEnumerable<PolygonFace>> leftRightFaceSet =
                    Utilities.SetTheoryUtils.Categories(parallelClampingFaces, (a, b) => Utilities.MathUtils.Dot(a.Normal, b.Normal) > 0)
                                            .OrderBy(p => Utilities.MathUtils.GetAngle(p.First().Normal, xAxis))
                                            .OrderBy(p => Utilities.MathUtils.GetAngle(p.First().Normal, yAxis))
                                            .OrderBy(p => Utilities.MathUtils.GetAngle(p.First().Normal, zAxis));

                m_candidateClampingPairSet.Add(new List<Utilities.Pair<PolygonFace>>());

                foreach (PolygonFace leftFace in leftRightFaceSet.First().Where(p =>p.IsValid()))
                {
                    foreach (PolygonFace rightFace in leftRightFaceSet.Last().Where(p => p.IsValid()))
                    {
                        // Update ClampingFace Polygon with intersection
                        Utilities.Pair<PolygonFace> newPair = null;
                        try
                        {
                            newPair = UpdateClampingFacePair(new Utilities.Pair<PolygonFace>(leftFace, rightFace));
                        }
                        catch (Exception)
                        {
                            Utilities.Debug.ThrowIf(newPair == null, "UpdateClampingFacePair error");
                        }

                        // And filter out to small Polygon intersection and interior facing face pairs
                        if (IsClampingPairAreaValid(newPair) && Utilities.MathUtils.IsPointBelow(newPair.Two.GetPoints().First(), newPair.One))
                        {
                            m_candidateClampingPairSet.Last().Add(newPair);
                        }
                    }
                }

                if(m_candidateClampingPairSet.Last().Count == 0)
                {
                    m_candidateClampingPairSet.Remove(m_candidateClampingPairSet.Last());
                }
            }
        }
   

        private void ComputeCandidateClampingNormals()
        {
            m_candidateClampingNormal = m_candidateClampingPairSet.Select(p => p.First().One.Normal).ToList();
        }


        private void ComputeCandidateBottomPlaneDirections()
        {
            m_candidateBottomPlaneDirections = new List<List<Vector3d>>();
            for (int i = 0; i < m_candidateClampingPairSet.Count(); i++)
            {
                PolygonFace clampingPolygonFace = m_candidateClampingPairSet[i].First().One;

                // Get a list of the directions of candidate bottom planes
                List<Vector3d> boxNormals = m_boundingBox.GetFaces().Select(p => p.Normal).ToList();
                List<Vector3d> directions = boxNormals.Union(GetBottomPlaneDirectionCandidate(clampingPolygonFace.Normal))
                                                      .Where(p => !Utilities.MathUtils.AreOrthogonal(p, clampingPolygonFace))
                                                      .ToList();

                List<Vector3d> projectedDirection = directions.Select(p => Utilities.MathUtils.Projection(p, clampingPolygonFace)).ToList();
                List<Vector3d> representativesDirection = Utilities.SetTheoryUtils.Representatives(projectedDirection, Utilities.MathUtils.AreCodirectional).ToList();

                m_candidateBottomPlaneDirections.Add(representativesDirection);
            }
        }


        private void ComputeCandidateBottomPlanes()
        {
            m_candidateBottomPlanes = new List<List<Utilities.Plane>>();
            List<Point3d> linearInterferencePoints = m_planarPolygonFaces.SelectMany(p => p.GetPoints()).ToList();
            linearInterferencePoints = Utilities.SetTheoryUtils.Representatives(linearInterferencePoints, (p, q) => Utilities.MathUtils.IsNeighbour(p, q)).ToList();

            for (int i = 0; i < m_candidateClampingNormal.Count; i++)
            {
                Vector3d clampingNormal = m_candidateClampingNormal[i];

                List<Utilities.Plane> bottomPlanes = new List<Utilities.Plane>();
                foreach (Vector3d directionCandidate in m_candidateBottomPlaneDirections[i])
                {
                    List<Point3d> nonLinearInterferencePoints = m_body.GetFaces()
                                                                      .Where(p => !Utilities.NXOpenUtils.IsPlanar(p))
                                                                      .Select(p => GetBoundingBox(p, directionCandidate, clampingNormal))
                                                                      .SelectMany(p => p.GetCornerPoints())
                                                                      .ToList();

                    // Bottom plane normal is directed away from body, the origin is taken from projections of interference points on normal
                    List<Point3d> interferencePoints = linearInterferencePoints.Union(nonLinearInterferencePoints).ToList();
                    Point3d origin = interferencePoints.OrderBy(p => Utilities.MathUtils.Dot(directionCandidate, p)).Last();
                    bottomPlanes.Add(new Utilities.Plane(origin, directionCandidate));
                }

                m_candidateBottomPlanes.Add(bottomPlanes);
            }
        }


        private ClampingFaces[] CreateClampingFaces()
        {
            List<ClampingFaces> clampingFaces = new List<ClampingFaces>();

            // Lists might not be the same length because we did not add facePairs with insufficient area
            for (int i = 0; i < m_candidateClampingPairSet.Count; ++i)
            {
                for (int j = 0; j < m_candidateClampingPairSet[i].Count; ++j)
                {
                    foreach (Utilities.Plane bottomPlane in m_candidateBottomPlanes[i])
                    {
                        ClampingFaces clampingFace = new ClampingFaces(m_body,
                                                                       m_candidateClampingPairSet[i][j].One,
                                                                       m_candidateClampingPairSet[i][j].Two,
                                                                       bottomPlane);

                        clampingFace = GetUpdateClampingFace(clampingFace);

                        if (clampingFace != null && IsClampingPairAreaValid(clampingFace))
                        {
                            clampingFaces.Add(clampingFace);
                        }
                    }
                }
            }

            return clampingFaces.ToArray();
        }


        private bool IsClampingPairAreaValid(Utilities.Pair<PolygonFace> clampingPair)
        {
            return clampingPair != null && clampingPair.One.GetArea() > m_minClampingArea && clampingPair.Two.GetArea() > m_minClampingArea;
        }


        private Vector3d[] GetBottomPlaneDirectionCandidate(Vector3d normal)
        {
            Utilities.Plane plane = new Utilities.Plane(normal);

            // Planar case
            List<Vector3d> planarDirectionCandidate = m_planarPolygonFaces.Select(p => p.Normal).Where(q => !Utilities.MathUtils.AreParallel(q, normal)).ToList();

            // Non planar case
            List<Face> nonPlanarFaces = m_body.GetFaces().Where(p => !Utilities.NXOpenUtils.IsPlanar(p)).ToList();
            List<PolygonFace> nonPlanarPolygonFaces = nonPlanarFaces.SelectMany(p => GetBoundingPolygonFaces(p, normal)).ToList();
            List<Vector3d> nonPlanarDirectionCandidate = nonPlanarPolygonFaces.Select(p => p.Normal).Where(q => !Utilities.MathUtils.AreParallel(q, normal)).ToList();

            List<Vector3d> projectedDirection = planarDirectionCandidate.Union(nonPlanarDirectionCandidate).Select(p => Utilities.MathUtils.Projection(p, plane)).ToList();

            return Utilities.SetTheoryUtils.Representatives(projectedDirection, AreInCodirectionalTolerance).ToArray();
        }


        private bool AreInCodirectionalTolerance(Vector3d one, Vector3d two)
        {
            return Utilities.MathUtils.InSolidAngle(one, two, Utilities.MathUtils.ToRadians(0.01));
        }


        private Utilities.Pair<PolygonFace> UpdateClampingFacePair(Utilities.Pair<PolygonFace> clampingFacePair)
        {
            PolygonFace pointsOne = clampingFacePair.One.IntersectProjectedFace(clampingFacePair.Two);
            PolygonFace pointsTwo = clampingFacePair.Two.IntersectProjectedFace(clampingFacePair.One);

            if (Polygon.AreValidCoplanarPolygons(pointsOne.Polygons) && Polygon.AreValidCoplanarPolygons(pointsTwo.Polygons))
            {
                return new Utilities.Pair<PolygonFace>(
                    new PolygonFace(pointsOne.Polygons, clampingFacePair.One.NXFace),
                    new PolygonFace(pointsTwo.Polygons, clampingFacePair.Two.NXFace)
                    );
            }

            return null;
        }


        private ClampingFaces GetUpdateClampingFace(ClampingFaces clampingFace)
        {
            List<Point3d> allPolygonPoints = m_planarPolygonFaces.Union(GetNonPlanarPolygonFaces(clampingFace)).SelectMany(p => p.GetPoints()).ToList();
            List<Point3d> limitsPoints = allPolygonPoints.Where(p => clampingFace.IsOutsideClampingPlanes(p)).ToList();

            ClampingFaces newClampingFace = new ClampingFaces(m_body, clampingFace.One, clampingFace.Two, clampingFace.BottomPlane);
            if (limitsPoints.Count() > 0)
            {
                Point3d maximumHeightPoint = limitsPoints.OrderBy(p => Utilities.MathUtils.GetSignedDistance(p, clampingFace.BottomPlane)).Last();
                Utilities.Plane clipingPlane = new Utilities.Plane(maximumHeightPoint, Utilities.MathUtils.Inverse(clampingFace.BottomPlane.Normal));

                PolygonFace one = clampingFace.One.Clip(clipingPlane);
                PolygonFace two = clampingFace.Two.Clip(clipingPlane);

                newClampingFace = IsClampingPairAreaValid(new Utilities.Pair<PolygonFace>(one, two)) ? new ClampingFaces(m_body, one, two, clampingFace.BottomPlane) : null;
            }

            return newClampingFace;
        }


        private List<PolygonFace> GetNonPlanarPolygonFaces(ClampingFaces clampingFace)
        {
            return GetNonPlanarPolygonFaces(clampingFace.One.Normal, clampingFace.BottomPlane.Normal);
        }


        private List<PolygonFace> GetNonPlanarPolygonFaces(Vector3d x, Vector3d y)
        {
            List<Face> nonPlanarFaces = m_body.GetFaces().Where(p => p.SolidFaceType != Face.FaceType.Planar).ToList();
            return nonPlanarFaces.SelectMany(p => GetBoundingPolygonFaces(p, x, y)).ToList();
        }


        private BoundingBox GetBoundingBox(Face face, Vector3d x, Vector3d y)
        {
            CartesianCoordinateSystem csys = Utilities.NXOpenUtils.CreateTemporaryCsys(new Point3d(), x, y);
            return BoundingBox.ComputeFaceBoundingBox(face, csys);
        }


        private PolygonFace[] GetBoundingPolygonFaces(Face face, Vector3d x, Vector3d y)
        {
            BoundingBox bbox = GetBoundingBox(face, x, y);

            return bbox != null ? bbox.GetFaces().Select(p => new PolygonFace(p.Polygons, face)).ToArray() : new PolygonFace[0];
        }


        private PolygonFace[] GetBoundingPolygonFaces(Face face, Vector3d x)
        {
            BoundingBox bbox = BoundingBox.ComputeFaceBoundingBoxAlongVector(face, x);
            return bbox != null ? bbox.GetFaces().Select(p => new PolygonFace(p.Polygons, face)).ToArray(): new PolygonFace[0];
        }


        private double m_minClampingArea;

        private Body m_body;
        private BoundingBox m_boundingBox;

        private List<PolygonFace> m_planarPolygonFaces;
        private List<List<PolygonFace>> m_parallelClampingFaceSet;

        private List<Vector3d> m_candidateClampingNormal;
        private List<List<Utilities.Pair<PolygonFace>>> m_candidateClampingPairSet;
        private List<List<Vector3d>> m_candidateBottomPlaneDirections;
        private List<List<Utilities.Plane>> m_candidateBottomPlanes;
    }
}
