using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;

using NXOpen;

namespace CAMAutomation
{
    public class HolePatternsDetector
    {
        public HolePatternsDetector(Part part, double holeDistance, int minNumHoles = 3, bool compact = true)
        {
            m_part = part;
            m_patternHolesDistance = holeDistance;
            m_minimumNumberOfHoles = minNumHoles > 3 ? minNumHoles: 3;
            m_compact = compact;

            m_holes = new List<Hole>();
            m_holePatterns = new List<HolePattern>();

            m_holeAxisSolidAngleTolerance = 1.0e-3;
        }


        ~HolePatternsDetector()
        {
            // empty
        }


        public bool ComputePatterns(out string msg, out HolePattern[] holePatterns)
        {
            try
            {
                // Set DisplayPart
                SetDisplayPart();

                // Retrieve the Body
                RetrieveBody();

                // Retrieve valid radius 
                RetrieveValidRadius();

                // Retrieve Hole Pattern Tolerance
                RetrieveHolePatternTolerance();

                // Find Holes
                FindHoles();

                // Find pattern
                FindPattern();

                msg = String.Empty;
                holePatterns = m_holePatterns.ToArray();

                return true;
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                holePatterns = null;

                return false;
            }
        }


        private void SetDisplayPart()
        {
            PartLoadStatus status;
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_part, false, true, out status);
        }


        private void RetrieveBody()
        {
            Body[] bodies = m_part.Bodies.ToArray();

            if (bodies.Length == 1)
            {
                m_body = bodies.First();
            }
            else
            {
                throw new Exception("Multiple bodies found in the CAD part");
            }
        }


        private void RetrieveValidRadius()
        {
            m_validRadiusMinMax = ConfigurationManager.AppSettings["MISUMI_VALID_HOLES_RADIUS"]
                .Split(new char[] { ',' })
                .Select(p => p.Split(new char[] { '-' }))
                .Select(q => new Utilities.Pair<double>(Convert.ToDouble(q.First()), Convert.ToDouble(q.Last())))
                .ToArray();
        }


        private void RetrieveHolePatternTolerance()
        {
            string holePatternToleranceStr = ConfigurationManager.AppSettings["MISUMI_HOLE_PATTERN_TOLERANCE"];
            if (holePatternToleranceStr == null || !double.TryParse(holePatternToleranceStr, out m_holePatternTolerance) || m_holePatternTolerance <= 0.0)
            {
                m_holePatternTolerance = 2.0e-3;
            }

            // The value provided is in INCH. Make the conversion if necessary
            if (m_part.PartUnits == BasePart.Units.Millimeters)
            {
                m_holePatternTolerance *= 25.4;
            }
        }


        private void FindHoles()
        {
            foreach (Face face in m_body.GetFaces())
            {
                Hole hole = CreateHoleFromFace(face);
                if (hole != null && IsValidHole(hole))
                {
                    m_holes.Add(hole);
                }
            }

            m_holes = Utilities.SetTheoryUtils.Representatives(m_holes, (p, q) => p.IsEquivalent(q, deltaHoleRadius: double.MaxValue)).ToList();
        }


        private Hole CreateHoleFromFace(Face face)
        {
            if (!IsHoleFace(face))
            {
                return null;
            }

            // Compute the radius of the cylinder face
            int type;
            double[] pointOnAxis = new double[3];
            double[] axisDir = new double[3];
            double[] box = new double[6];
            double radius;
            double radData;
            int normDir;
            Utilities.RemoteSession.UFSession.Modl.AskFaceData(face.Tag, out type, pointOnAxis, axisDir, box, out radius, out radData, out normDir);

            Point3d holePoint = new Point3d(pointOnAxis[0], pointOnAxis[1], pointOnAxis[2]);
            Vector3d holeAxis = new Vector3d(axisDir[0], axisDir[1], axisDir[2]);
            
            return new Hole(holePoint, holeAxis, radius, face);
        }


        private bool IsHoleFace(Face face)
        {
            return face.SolidFaceType == Face.FaceType.Cylindrical && Has360DegreeCoverage(face) && GetCurvature(face)[0] > 0.0;
        }


        private bool Has360DegreeCoverage(Face face)
        {
            double[] uv = new double[4];
            Utilities.RemoteSession.UFSession.Modl.AskFaceUvMinmax(face.Tag, uv);

            return (Math.Abs(uv[0]) < Utilities.MathUtils.ABS_TOL && Math.Abs(uv[1] - 2 * Math.PI) < Utilities.MathUtils.ABS_TOL ||
                   (Math.Abs(uv[1]) < Utilities.MathUtils.ABS_TOL && Math.Abs(uv[2] - 2 * Math.PI) < Utilities.MathUtils.ABS_TOL));
        }


        private double[] GetCurvature(Face face)
        {
            // We will compute the sign of the cylinder face curvature
            // An internal hole face has a positive curvature
            // An external face has a negative curvature

            // (1) Retrieve UV min/max values
            double[] uv = new double[4];
            Utilities.RemoteSession.UFSession.Modl.AskFaceUvMinmax(face.Tag, uv);

            // (2) Take the half value point
            double[] param = new double[2];
            param[0] = (uv[0] + uv[1] / 2.0);
            param[1] = (uv[1] + uv[2] / 2.0);

            // (3) Compute the curvature
            double[] refPointOnFace = new double[3];
            double[] u1 = new double[3];
            double[] v1 = new double[3];
            double[] u2 = new double[3];
            double[] v2 = new double[3];
            double[] normal = new double[3];
            double[] curvature = new double[2];
            Utilities.RemoteSession.UFSession.Modl.AskFaceProps(face.Tag, param, refPointOnFace, u1, v1, u2, v2, normal, curvature);

            return curvature;
        }


        private bool IsValidHole(Hole hole)
        {
            double factor = m_part.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1.0;
            return m_validRadiusMinMax.Any(p => p.One * factor <= hole.Radius && hole.Radius <= p.Two * factor);
        }


        private bool FindPattern()
        {
            if (m_holes.Count == 0)
            {
                return false;
            }

            // Project holes points on their axis plan
            List<Hole> holeList = new List<Hole>(); 
            foreach (Hole hole in m_holes)
            {
                Point3d point = Utilities.MathUtils.Projection(hole.Origin, new Utilities.Plane(hole.Axis));
                holeList.Add(new Hole(point, hole.Axis, hole.Radius, hole.NXFace));
            }

            // Gather Holes by object caracteristics
            IEnumerable<IEnumerable<Hole>> holesByCategorys = Utilities.SetTheoryUtils.Categories(holeList, (x, y) => HolesRelation(x, y))
                                                                                      .Where((l) => l.Count() >= m_minimumNumberOfHoles);

            // Gather Holes by pair object caracteristics
            List<HolePattern> allCompactHolesPattern = new List<HolePattern>();
            foreach (Hole[] holesByCategory in Utilities.MathUtils.ToArray(holesByCategorys))
            {
                IEnumerable<IEnumerable<Hole>> holesByPairCategorys = Utilities.SetTheoryUtils.PairCategories(holesByCategory, (x, y) => HolesPairsRelation(x, y))
                                                                                              .Where((l) => l.Count() >= m_minimumNumberOfHoles);

                // Only keep valid pattern 
                List<HolePattern> compactHolesPattern = new List<HolePattern>();
                foreach (Hole[] holes in Utilities.MathUtils.ToArray(holesByPairCategorys))
                {
                    HolePattern[] holesPattern = CompactHolesPatterns(new HolePattern(holes), m_holePatternTolerance);
                    compactHolesPattern.AddRange(holesPattern);
                }

                allCompactHolesPattern.AddRange(Utilities.SetTheoryUtils.Representatives(compactHolesPattern, (p,q) => p.IsEquivalent(q, deltaHoleRadius: double.MaxValue)));
            }

            m_holePatterns = Utilities.SetTheoryUtils.Representatives(allCompactHolesPattern, Equals).ToList();

            return true;
        }


        private bool HolesRelation(Hole hole1, Hole hole2)
        {
            return Utilities.MathUtils.InDoubleCone(hole1.Axis, hole2.Axis, m_holeAxisSolidAngleTolerance);
        }


        private bool HolesPairsRelation(Utilities.Pair<Hole> holePair1, Utilities.Pair<Hole> holePair2)
        {
            return AreInSameToleroncePlane(holePair1, holePair2, m_holePatternTolerance, m_holeAxisSolidAngleTolerance);
        }


        private bool AreInSameToleroncePlane(Utilities.Pair<Hole> holePair1, Utilities.Pair<Hole> holePair2, double radius = 0.0, double solidAngle = 0.0)
        {
            Vector3d holePatternDir = Utilities.MathUtils.GetDistanceVector(holePair1.One.GetCenterLine(), holePair1.Two.GetCenterLine());
            Vector3d axisDir = holePair1.One.Axis;
            Utilities.Plane tolerancePlane = new Utilities.Plane(holePair1.One.Origin, Utilities.MathUtils.Cross(holePatternDir, axisDir));

            bool inTolerancePlane = Utilities.MathUtils.IsNeighbour(holePair2.One.Origin, tolerancePlane, radius) &&
                                    Utilities.MathUtils.IsNeighbour(holePair2.Two.Origin, tolerancePlane, radius);

            bool inToleranceCone = Utilities.MathUtils.InDoubleCone(axisDir, holePair1.Two.Axis, solidAngle) &&
                                   Utilities.MathUtils.InDoubleCone(axisDir, holePair2.One.Axis, solidAngle) &&
                                   Utilities.MathUtils.InDoubleCone(axisDir, holePair2.Two.Axis, solidAngle);

            return inTolerancePlane && inToleranceCone;
        }


        private HolePattern[] CompactHolesPatterns(HolePattern holePattern, double radius = 0.0 )
        {
            // Using patternDistance, minNumHoles and compact definition of a HolePattern
            // Find all compact/non-compact HolePatters within a holePattern (the given aligned holes)
            // If * is a hole there in tow compact holePatterns but only one holePattern in :   * * *   * * * *     *

            List<HolePattern> compactHolesPatterns = new List<HolePattern>();
            if (holePattern.Count >= m_minimumNumberOfHoles)
            {
                // Compute Holes Distance Table
                Vector3d direction = HolesDistanceVector(holePattern.First(), holePattern.Last());
                HolePattern orderedHolePattern = OrderHoles(holePattern, direction);
                double[][] holesDistanceTable = ComputeHolesDistanceTable(orderedHolePattern, direction);

                foreach (double[] distancePattern in holesDistanceTable)
                {
                    HolePattern[] allCompactHolesPattern = ComputeCompactHolesPatterns(distancePattern, orderedHolePattern, radius);
                    foreach (HolePattern pattern in allCompactHolesPattern)
                    {
                        if (pattern.Count() >= m_minimumNumberOfHoles)
                        {
                            compactHolesPatterns.Add(OrderHoles(pattern, direction));
                        }
                    }
                }
            }

            return compactHolesPatterns.ToArray();
        }


        private double[][] ComputeHolesDistanceTable(HolePattern holePattern, Vector3d direction)
        {
            List<List<double>> patternTable = new List<List<double>>();

            if (holePattern.Count >= 1)
            {
                Utilities.Plane plane = new Utilities.Plane(holePattern.First().Axis);

                for (int i = 0; i < holePattern.Count(); i++)
                {
                    List<double> holesDistancePattern = new List<double>();

                    for (int j = 0; j < holePattern.Count(); j++)
                    {
                        Vector3d distanceVectorToFirst = HolesDistanceVector(holePattern[i], holePattern[j]);

                        double signedPairDistance = Utilities.MathUtils.Dot(Utilities.MathUtils.Projection(distanceVectorToFirst, plane), 
                                                                            Utilities.MathUtils.UnitVector(direction));

                        holesDistancePattern.Add(signedPairDistance / m_patternHolesDistance);
                    }

                    patternTable.Add(holesDistancePattern);
                }
            }


            return Utilities.MathUtils.ToArray(patternTable);
        }


        private HolePattern[] ComputeCompactHolesPatterns(double[] orderedDistancePattern, HolePattern orderedHolePattern, double radius = 0.0)
        {
            // From one of the orderedDistancePatterns of orderedHolePattern create all HolePatterns
            // by keping only holes that are distance by near integer of orderedDistancePattern
            // when compact only keep continus serie of near-integer as pattern

            List<HolePattern> holePatterns = new List<HolePattern>();

            if (orderedDistancePattern.Count() >= 1)
            {
                double integerRadius = radius / m_patternHolesDistance;
                double[] distancePattern = orderedDistancePattern.ToArray();

                bool firstHolePatternFound = false;
                double curentDistanceFraction = distancePattern.First();
                holePatterns.Add(new HolePattern());
                for (int i = 0; i < distancePattern.Length; i++)
                {
                    if (!Utilities.MathUtils.IsNearInteger(distancePattern[i], integerRadius))
                    {
                        if (!firstHolePatternFound)
                        {
                            curentDistanceFraction = distancePattern[i];
                        }
                        else if (m_compact)
                        {
                            holePatterns.Add(new HolePattern());
                        }
                        continue;
                    }

                    firstHolePatternFound = true;
                    if (Utilities.MathUtils.IsNeighbour(curentDistanceFraction, distancePattern[i], 1.0 + integerRadius))
                    {
                        holePatterns.Last().Add(orderedHolePattern[i]);
                    }
                    else
                    {
                        holePatterns.Add(new HolePattern());
                        holePatterns.Last().Add(orderedHolePattern[i]);
                    }
                    curentDistanceFraction = distancePattern[i];
                }
            }

            return holePatterns.ToArray();
        }


        private HolePattern OrderHoles(HolePattern holePattern, Vector3d direction)
        {
            List<KeyValuePair<double, Hole>> distanceHolePairs = new List<KeyValuePair<double, Hole>>();

            if (holePattern.Count > 1)
            {
                Hole firstHole = holePattern.First();

                Utilities.Plane plane = new Utilities.Plane(firstHole.Axis);

                foreach (Hole hole in holePattern)
                {
                    double signedDistance = Utilities.MathUtils.Dot(HolesDistanceVector(firstHole, hole), direction);
                    distanceHolePairs.Add(new KeyValuePair<double, Hole>(signedDistance, hole));
                }
            }

            return new HolePattern(distanceHolePairs.OrderBy(x => x.Key).Select(x => x.Value));
        }


        private Vector3d HolesDistanceVector(Hole hole1, Hole hole2)
        {
            return Utilities.MathUtils.GetDistanceVector(hole1.GetCenterLine(), hole2.GetCenterLine());
        }


        private Part m_part;
        private double m_patternHolesDistance;
        private int m_minimumNumberOfHoles;
        private bool m_compact;

        private Body m_body;

        private List<Hole> m_holes;
        private List<HolePattern> m_holePatterns;

        private Utilities.Pair<double>[] m_validRadiusMinMax;

        private double m_holeAxisSolidAngleTolerance;
        private double m_holePatternTolerance;
    }
}
