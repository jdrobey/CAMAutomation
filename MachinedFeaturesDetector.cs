using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class MachinedFeaturesDetector
    {
        public static readonly string[] RadialFeatures = new string[]
        {
            "STEP1POCKET",
            "STEP1POCKET_THREAD",
            "STEP2POCKET",
            "STEP2POCKET_THREAD",
            "STEP3POCKET",
            "STEP3POCKET_THREAD",
            "STEP4POCKET",
            "STEP4POCKET_THREAD",
            "STEP5POCKET",
            "STEP5POCKET_THREAD",
            "STEP6POCKET",
            "STEP6POCKET_THREAD",
            "STEP1HOLE",
            "STEP1HOLE_THREAD",
            "STEP2HOLE",
            "STEP2HOLE_THREAD",
            "STEP3HOLE",
            "STEP3HOLE_THREAD",
            "STEP4HOLE",
            "STEP4HOLE_THREAD",
            "STEP5HOLE",
            "STEP5HOLE_THREAD",
            "STEP6HOLE",
            "STEP6HOLE_THREAD",
            "STEP6HOLE1",
            "STEP6HOLE1_THREAD",
            "STEP6HOLE2",
            "STEP6HOLE2_THREAD",
            "STEP5HOLE1",
            "STEP5HOLE1_THREAD",
            "STEP5HOLE2",
            "STEP5HOLE2_THREAD",
            "STEP4HOLE1",
            "STEP4HOLE1_THREAD",
            "STEP3HOLE1",
            "STEP3HOLE1_THREAD",
            "HOLE_OBROUND_STRAIGHT",
            "HOLE_ROUND_TAPERED",
            "HOLE_ROUND_INTERRUPT_STRAIGHT",
            "BOSS_ROUND_STRAIGHT",
            "BOSS_ROUND_STRAIGHT_BR",
            "BOSS_ROUND_STRAIGHT_TC",
            "BOSS_ROUND_STRAIGHT_TR",
            "BOSS_ROUND_STRAIGHT_TC_BR",
            "BOSS_ROUND_STRAIGHT_TR_BR",
            "BOSS_ROUND_STRAIGHT_THREAD",
            "BOSS_ROUND_STRAIGHT_THREAD_BR",
            "BOSS_ROUND_STRAIGHT_THREAD_TC",
            "BOSS_ROUND_STRAIGHT_THREAD_TC_BR",
            "BOSS_ROUND_STRAIGHT_THREAD_TR",
            "BOSS_ROUND_STRAIGHT_THREAD_TR_BR"
        };


        // Custom FeatureFace Class 
        public class FeatureFace
        {
            // Finds the orientation an normal from 3 points
            public static bool NormalFromPoints(Point3d[] points, out Vector3d normal)
            {              
                if (points.Count() >= 3)
                {
                    // Tries with the first set of three point, then with the next ...
                    for (int i = 0; i <= points.Count() - 3; i++)
                    {
                        Point3d p1 = points[i];
                        Point3d p2 = points[i + 1];
                        Point3d p3 = points[i + 2];

                        Vector3d v1 = Utilities.MathUtils.GetVector(p2, p1);
                        Vector3d v2 = Utilities.MathUtils.GetVector(p3, p1);

                        if (!(Utilities.MathUtils.AreParallel(v1, v2) || Utilities.MathUtils.AreParallel(v1, v2)) && Utilities.MathUtils.Length(v1) > 0.05 && Utilities.MathUtils.Length(v2) > 0.05)
                        {
                            if (Utilities.MathUtils.GetNormalFrom3Points(p1, p2, p3, out normal))
                            {
                                return true;
                            }
                        }                           
                    }
                }

                normal = new Vector3d();
                return false;
            }


            // Initialize FeatureFace
            public FeatureFace(Feature parent, Face face)
            {
                Parent = parent;
                FaceType = face.SolidFaceType;

                Points = new List<Point3d>();
                foreach (Edge edge in face.GetEdges())
                {
                    Point3d p1;
                    Point3d p2;

                    edge.GetVertices(out p1, out p2);

                    Points.Add(p1);
                    Points.Add(p2);
                }

                Point3d origin = Points[0];
                Vector3d normal;

                switch (FaceType)
                {
                    case Face.FaceType.Planar:
                        // Get the normal of the planar face from points
                        if (!NormalFromPoints(Points.ToArray(), out normal))
                           normal = parent.Plane.Normal;
                        break;

                    case Face.FaceType.Blending:
                    case Face.FaceType.Conical:
                    case Face.FaceType.Convergent:
                    case Face.FaceType.Cylindrical:
                    case Face.FaceType.Offset:
                    case Face.FaceType.Parametric:
                    case Face.FaceType.Rubber:
                    case Face.FaceType.Spherical:
                    case Face.FaceType.SurfaceOfRevolution:
                    case Face.FaceType.Swept:
                    case Face.FaceType.Undefined:
                    default:
                        // Use the feature normal
                        normal = parent.Plane.Normal;
                        break;
                }

                Plane = new Utilities.Plane(origin, normal);
            }


            public bool IsOnFeatureFace(FeatureFace blank, Matrix3x3 rotation, Vector3d translation)
            {
                // Check that all points of the cad feature Face are on the blank Feature Face plane
                foreach (Point3d point in Points)
                {
                    // Rotate the point
                    Point3d movedPoint = Utilities.MathUtils.Multiply(rotation, point);
                    movedPoint = Utilities.MathUtils.Add(movedPoint, translation);

                    // If the point is past the plane, its not a correspondance
                    if (!Utilities.MathUtils.IsPointOnPlane(movedPoint, blank.Plane))
                    {
                        return false;
                    }
                }

                return true;
            }


            public Feature Parent { get; }

            public Face.FaceType FaceType { get; }
            public List<Point3d> Points { get; }

            public Utilities.Plane Plane { get; }
        }


        // Custom Feature Class to avoid lookups 
        public class Feature
        {
            // Returns the vector normal to the feature normal that goes through a point
            public static Vector3d OrthoVector(Point3d point, FeatureFace face)
            {
                if (RadialFeatures.Contains(face.Parent.Type))
                {
                    // Distance to a line (axis of a radial feature)
                    // Vector from Point to point on the line
                    Vector3d vToPoint = Utilities.MathUtils.GetVector(face.Parent.Plane.Origin, point);


                    return Utilities.MathUtils.Projection(vToPoint, face.Parent.Plane);
                }
                else
                {
                    return Utilities.MathUtils.GetDistanceVector(point, face.Plane);
                }
            }

            // Returns the vector normal to the feature normal that goes through a point
            public static Vector3d OrthoVector(Point3d point, Feature feature)
            {
                if (RadialFeatures.Contains(feature.Type))
                {
                    // Distance to a line (axis of a radial feature)
                    // Vector from Point to point on the line
                    Vector3d vToPoint = Utilities.MathUtils.GetVector(feature.Plane.Origin, point);

                    return Utilities.MathUtils.Projection(vToPoint, feature.Plane);
                }
                else
                {
                    return Utilities.MathUtils.GetDistanceVector(point, feature.Plane);
                }                       
            }

            // Initialize the feature
            public Feature(CAMFeature feature)
            {
                CAMObj = feature;
                Name = feature.Name;
                Type = feature.Type;
                Faces = new List<FeatureFace>();

                // NX orientation matrix is transposed
                Matrix3x3 featureMatrix = feature.CoordinateSystem.Orientation.Element;
                Vector3d normal = new Vector3d(featureMatrix.Zx, featureMatrix.Zy, featureMatrix.Zz);

                // Normals of 'SURFACE_PLANAR' are inverted
                if (feature.Type == "SURFACE_PLANAR")
                    normal = Utilities.MathUtils.Inverse(normal);

                Point3d origin = feature.CoordinateSystem.Origin;

                Plane = new Utilities.Plane(origin, normal);

                // Add all faces
                foreach (Face face in feature.GetFaces())
                {
                    FeatureFace FF = new FeatureFace(this, face);
                    Faces.Add(FF);
                }

            }


            // Find the farthest point along a direction 'd'
            public Point3d FarPoint(Vector3d d)
            {
                Point3d farthestPoint = Faces[0].Points[0];
                foreach (FeatureFace FF in Faces)
                    foreach (Point3d P in FF.Points)

                        if (Utilities.MathUtils.Dot(Utilities.MathUtils.Substract(P, farthestPoint), d) > 0)
                            farthestPoint = P;

                return farthestPoint;
            }

            // Find the farthest points (+/-) along the feature normal
            public Point3d[] FarPoint()
            {
                return new Point3d[] {
                    FarPoint(Plane.Normal),
                    FarPoint(Utilities.MathUtils.Inverse(Plane.Normal))};
            }

            public CAMFeature CAMObj { get; }
            public string Name { get; }
            public string Type { get; }

            public Utilities.Plane Plane { get; }

            public List<FeatureFace> Faces { get; }
        }


        // Component Class (Blank/Part)
        public class Component
        {
            public Component(Matrix3x3 orientation, Point3d position, CAMFeature[] features)
            {
                Features = new List<Feature>();
                Frames = new List<RefFrame>();
                m_CAMFeatures = features;
                PointsOnEdges = GetPointsOnEdges();

                // This code should be refactor to use NX convention
                Orientation = Utilities.MathUtils.Transpose(orientation);
                Position = position;

                // Add all features to component
                foreach (CAMFeature F in features)
                {
                    Feature f = new Feature(F);

                    Features.Add(f);
                }

                // Listing valid features for creating frames
                List<int> validFeatures = new List<int>();
                for (int i = 0; i < Features.Count(); i++)
                {
                    // Using only planar surfaces and holes to build ref frames
                    if (Features[i].Type == "SURFACE_PLANAR_RECTANGULAR" ||
                        Features[i].Type == "SURFACE_PLANAR" ||
                        Features[i].Type == "STEP1HOLE" ||
                        Features[i].Type == "STEP1POCKET")
                        validFeatures.Add(i);
                }

                foreach (int featureIndex1 in validFeatures)
                {
                    foreach (int featureIndex2 in validFeatures)
                    {
                        // Cannot use itself to build a valid basis
                        if (featureIndex2 == featureIndex1)
                            continue;

                        Vector3d v1 = Features[featureIndex1].Plane.Normal;
                        Vector3d v2 = Features[featureIndex2].Plane.Normal;

                        // Special case where 2 parallel holes are used to generate a common normal direction V2 (Needs a third orthogonal feature)
                        if (Utilities.MathUtils.AreParallel(v1,v2)
                            && (Features[featureIndex1].Type == "STEP1HOLE" || Features[featureIndex1].Type == "STEP1POCKET")
                            && (Features[featureIndex2].Type == "STEP1HOLE" || Features[featureIndex2].Type == "STEP1POCKET"))
                        {
                            v2 = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(v1,
                                                                                          Utilities.MathUtils.Cross(Utilities.MathUtils.GetVector(Features[featureIndex2].Plane.Origin, Features[featureIndex1].Plane.Origin),
                                                                                                                                                  v1)));

                            foreach (int featureIndex3 in validFeatures)
                            {
                                if (featureIndex3 == featureIndex1 || featureIndex3 == featureIndex2)
                                    continue;

                                // We don't allow hole for third feature
                                if (Features[featureIndex3].Type == "STEP1HOLE" || Features[featureIndex3].Type == "STEP1POCKET")
                                    continue;

                                Vector3d v3 = Features[featureIndex3].Plane.Normal;

                                // The third feature must have normal parallel to first feature
                                if (Utilities.MathUtils.AreParallel(v1,v3))
                                {
                                    // Add valid frame to the list
                                    Frames.Add(new RefFrame(Features, featureIndex1, featureIndex2, featureIndex3));
                                }
                            }

                            continue;
                        }

                        // For other cases we avoid holes
                        if (Features[featureIndex1].Type == "STEP1HOLE" || Features[featureIndex1].Type == "STEP1POCKET" ||
                            Features[featureIndex2].Type == "STEP1HOLE" || Features[featureIndex2].Type == "STEP1POCKET")
                            continue;


                        Vector3d vector12Cross = Utilities.MathUtils.Cross(v1, v2);

                        // Looking for orthogonal vector (vector1_2Angle is the cos value)
                        if (Utilities.MathUtils.AreOrthogonal(v1, v2))
                            continue;

                        foreach (int featureIndex3 in validFeatures)
                        {
                            // Avoiding Holes
                            if (Features[featureIndex3].Type == "STEP1HOLE" || Features[featureIndex3].Type == "STEP1POCKET")
                                continue;

                            if (featureIndex3 == featureIndex1 || featureIndex3 == featureIndex2)
                                continue;

                            Vector3d v3 = Features[featureIndex3].Plane.Normal;

                            if (Utilities.MathUtils.AreOrthogonal(v1,v3) && Utilities.MathUtils.AreOrthogonal(v2, v3))
                            {
                                if (!Utilities.MathUtils.AreOrthogonal(vector12Cross, v3))
                                {
                                    // Add valid frame to the list
                                    Frames.Add(new RefFrame(Features, featureIndex1, featureIndex2, featureIndex3));
                                }
                            }
                        }
                    }
                }
            }

            // List all points of the component
            private List<Point3d> GetPointsOnEdges()
            {
                List<Point3d> pointList = new List<Point3d>();

                foreach (CAMFeature a in m_CAMFeatures)
                {
                    foreach (Face b in a.GetFaces())
                    {
                        foreach (Edge c in b.GetEdges())
                        {
                            // Only take linear edges
                            if (c.SolidEdgeType == Edge.EdgeType.Linear)
                            {
                                Point3d p1, p2;

                                // Get two vertices of the edge
                                c.GetVertices(out p1, out p2);

                                // Add vertices to the point list
                                pointList.Add(p1);
                                pointList.Add(p2);
                            }
                        }
                    }
                }

                return pointList;
            }

            public List<Feature> Features { get; }
            public Matrix3x3 Orientation { get; }
            public Point3d Position { get; }
            public List<RefFrame> Frames { get; }
            public List<Point3d> PointsOnEdges { get; }
            private CAMFeature[] m_CAMFeatures;

        }


        // Reference Frame class
        public class RefFrame
        {
            public RefFrame(List<Feature> features, int i, int j, int k)
            {
                m_frame = new List<int> { i, j, k };

                Vector3d v1 = features[i].Plane.Normal;
                Vector3d v2 = features[j].Plane.Normal;

                // Orthonormal basis constructed out of the first 2 vectors
                Vector3d v3 = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(v1, v2));

                double[] m1Eq, m2Eq, m3Eq;
                m1Eq = new double[4];
                m2Eq = new double[4];
                m3Eq = new double[4];

                if (Utilities.MathUtils.AreParallel(v1,v2))
                { // Special case where 2 parallel normal are used to generate a common normal direction V2 (Needs a third orthogonal feature)
                    v2 = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(v1, 
                                                                                  Utilities.MathUtils.Cross(Utilities.MathUtils.GetVector3d(
                                                                                                            Utilities.MathUtils.Substract(features[j].Plane.Origin,
                                                                                                                                          features[i].Plane.Origin)),
                                                                                                                                          v1)));

                    v3 = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(v1, v2));

                    Utilities.Plane plane1 = new Utilities.Plane(features[k].Plane.Origin, v1);
                    plane1.GetParameters(out m1Eq[0], out m1Eq[1], out m1Eq[2], out m1Eq[3]);

                    Utilities.Plane plane2 = new Utilities.Plane(features[i].Plane.Origin, v2);
                    plane2.GetParameters(out m2Eq[0], out m2Eq[1], out m2Eq[2], out m2Eq[3]);

                    Utilities.Plane plane3 = new Utilities.Plane(features[i].Plane.Origin, v3);
                    plane3.GetParameters(out m3Eq[0], out m3Eq[1], out m3Eq[2], out m3Eq[3]);
                }
                else
                { // Other general cases
                    v2 = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(v3, v1));

                    Utilities.Plane plane1 = new Utilities.Plane(features[i].Plane.Origin, v1);
                    plane1.GetParameters(out m1Eq[0], out m1Eq[1], out m1Eq[2], out m1Eq[3]);

                    Utilities.Plane plane2 = new Utilities.Plane(features[j].Plane.Origin, v2);
                    plane2.GetParameters(out m2Eq[0], out m2Eq[1], out m2Eq[2], out m2Eq[3]);

                    Utilities.Plane plane3 = new Utilities.Plane(features[k].Plane.Origin, v3);
                    plane3.GetParameters(out m3Eq[0], out m3Eq[1], out m3Eq[2], out m3Eq[3]);
                }


                Matrix3x3 mc = Utilities.MathUtils.CreateMatrix(
                    new Vector3d(m1Eq[0], m1Eq[1], m1Eq[2]),
                    new Vector3d(m2Eq[0], m2Eq[1], m2Eq[2]),
                    new Vector3d(m3Eq[0], m3Eq[1], m3Eq[2]));

                // Vector from d parameter
                Vector3d d = new Vector3d(-m1Eq[3], -m2Eq[3], -m3Eq[3]);

                Orientation = Utilities.MathUtils.Transpose(Utilities.MathUtils.CreateMatrix(v1, v2, v3));

                Point3d pos = new Point3d();
                Utilities.MathUtils.SolveLinearEquations(mc, d, out pos);

                Position = new Point3d(pos.X, pos.Y, pos.Z);
            }

            private List<int> m_frame;
            public Matrix3x3 Orientation { get; }
            public Point3d Position { get; }
        }


        // Structure to a solution
        public class Solution
        {
            public static Solution FindSolutions(Component blank, Component cad, BoundingBox blankBB)
            {
                List<Solution> solutions = new List<Solution>();

                foreach (RefFrame blankFrames in blank.Frames)
                {
                    foreach (RefFrame cadFrames in cad.Frames)
                    {
                        Solution possibleSolution = new Solution();
 
                        possibleSolution.BlankFrame = blankFrames;
                        possibleSolution.CadFrame = cadFrames;

                        // ***************************************************************************
                        // ***************************************************************************
                        // Transformation from CAD to Blank Ref Frames

                        // Rotate
                        Matrix3x3 rotation_ABS2BF = blankFrames.Orientation;
                        Matrix3x3 rotation_ABS2CF = cadFrames.Orientation;

                        Matrix3x3 rotation_C2B = new Matrix3x3();
                        if (Utilities.MathUtils.Inverse(rotation_ABS2CF, out Matrix3x3 inverse))
                        {
                            rotation_C2B = Utilities.MathUtils.Multiply(rotation_ABS2BF, inverse);
                        }
                        else
                        {
                            throw new Exception("Could Not Compute Inverse of Matrix, determinant is 0");
                        }

                        Vector3d translation_C2B = Utilities.MathUtils.GetVector(Utilities.MathUtils.Multiply(rotation_C2B, cadFrames.Position), blankFrames.Position);

                        // Check that the CAD body is within the blank body
                        if (!IsCadInsideBlank(cad, blankBB, rotation_C2B, translation_C2B))
                        {
                            continue;
                        }

                        // ***************************************************************************
                        // ***************************************************************************

                        possibleSolution.CompareFeatures(blank, cad, translation_C2B, rotation_C2B);

                        if (possibleSolution.Correspondances.Count() > 0)
                        {
                            solutions.Add(possibleSolution);
                        }
                    }
                }

                // Check if there is a solution
                if (solutions.Count() == 0)
                {
                    throw new Exception("No Solutions");
                }
                else
                {
                    return solutions.OrderByDescending(p => p.Correspondances.Count + p.IgnorePair.Count).FirstOrDefault();
                }
            }


            public void GetFeatureFaceCorrespondances(Feature blankFeature, Feature cadFeature, Vector3d translation_C2B, Matrix3x3 rotation_C2B)
            {
                int faceCorrespondances = 0;
                foreach (FeatureFace blankFace in blankFeature.Faces) // Blank = k
                {
                    foreach (FeatureFace cadFace in cadFeature.Faces) // Cad = l
                    {
                        if (blankFace.FaceType != cadFace.FaceType)
                            continue;

                        // *** NORMALS VECTORS COMPARISON

                        // Transform the CAD face normal in the blank reference frame (Rotation only)
                        Vector3d cadFrameNormToBlank = Utilities.MathUtils.Multiply(rotation_C2B, cadFace.Plane.Normal);

                        // *** DISTANCE VECTORS COMPARISON

                        // Vector orthogonal to the feature's normal that goes through the reference frame origin
                        Vector3d blankFrameVec = Feature.OrthoVector(BlankFrame.Position, blankFace);
                        Vector3d cadFrameVec = Feature.OrthoVector(CadFrame.Position, cadFace);

                        // Transform the vector orthogonal to the CAD feature normal in the blank reference frame (Rotation only)
                        Vector3d cadFrameVecToBlank = Utilities.MathUtils.Multiply(rotation_C2B, cadFrameVec);

                        // Check if the orthogonal vectors have the same orientation
                        double orientSimil;
                        if (Utilities.MathUtils.Length(blankFrameVec) < 0.001 || Utilities.MathUtils.Length(cadFrameVec) < 0.001)
                            orientSimil = 1;
                        else
                            orientSimil = Utilities.MathUtils.Dot(Utilities.MathUtils.UnitVector(blankFrameVec),
                                                                  Utilities.MathUtils.UnitVector(cadFrameVecToBlank));

                        double t = 1 - 0.001; //Threshold

                        // Increment Face correspondance if faces similar
                        if (Utilities.MathUtils.AreParallel(blankFace.Plane.Normal, cadFrameNormToBlank) &&
                            orientSimil > t &&
                            cadFace.IsOnFeatureFace(blankFace, rotation_C2B, translation_C2B))
                        {
                            faceCorrespondances++;
                        }
                    }

                    // Increment correspondance all faces of the feature match
                    if (faceCorrespondances >= blankFeature.Faces.Count)
                    {
                        // We shall only add a correspondance if all faces of the feature match ?
                        Correspondances.Add(new Utilities.Pair<Feature>(blankFeature, cadFeature));

                        Rotation = rotation_C2B;
                        Translation = translation_C2B;
                    }
                }
            }


            public void CompareFeatures(Component blank, Component cad, Vector3d translation_C2B, Matrix3x3 rotation_C2B)
            {
                foreach (Feature blankFeature in blank.Features)
                {
                    foreach (Feature cadFeature in cad.Features)
                    {
                        if ((blankFeature.Type == "STEP1HOLE" || blankFeature.Type == "STEP1POCKET") && (cadFeature.Type == "STEP1HOLE" || cadFeature.Type == "STEP1POCKET"))
                        {
                            // Transform the CAD face normal in the blank reference frame (Rotation only)
                            Vector3d cadFaceNormToBlank = Utilities.MathUtils.Multiply(rotation_C2B, cadFeature.Plane.Normal);

                            // Cosine similarity of Normals (Direction is not accounted for, Abs is used)
                            double normalSimil = Math.Abs(Utilities.MathUtils.Dot(blankFeature.Plane.Normal, cadFaceNormToBlank));

                            // *** DISTANCE VECTORS COMPARISON

                            // Vector orthogonal to the feature's normal that goes through the reference frame origin
                            Vector3d blankFrameVec = Feature.OrthoVector(BlankFrame.Position, blankFeature);
                            Vector3d cadFrameVec = Feature.OrthoVector(CadFrame.Position, cadFeature);

                            // Transform the vector orthogonal to the CAD feature normal in the blank reference frame (Rotation only)
                            Vector3d cadFrameVecToBlank = Utilities.MathUtils.Multiply(rotation_C2B, cadFrameVec);

                            // Check if the orthogonal vectors have the same orientation
                            double orientSimil;
                            if (Utilities.MathUtils.Length(blankFrameVec) < 0.001 || Utilities.MathUtils.Length(cadFrameVec) < 0.001)
                                orientSimil = 1;
                            else
                                orientSimil = Math.Abs(Utilities.MathUtils.Dot(Utilities.MathUtils.UnitVector(blankFrameVec),
                                                                               Utilities.MathUtils.UnitVector(cadFrameVecToBlank)));

                            // Check that both have the same length
                            double lengthDiff = Math.Abs(Utilities.MathUtils.Length(blankFrameVec) - Utilities.MathUtils.Length(cadFrameVecToBlank));

                            double t = 1 - 0.001; //Threshold

                            if (Utilities.MathUtils.AreParallel(blankFeature.Plane.Normal, cadFaceNormToBlank)
                                && lengthDiff < 1 - t && orientSimil > t)
                            {
                                IgnorePair.Add(new Utilities.Pair<Feature>(blankFeature, cadFeature));
                            }
                        }
                        else
                        {
                            GetFeatureFaceCorrespondances(blankFeature, cadFeature, translation_C2B, rotation_C2B);
                        }
                    }
                }
            }


            public Solution()
            {
                Correspondances = new List<Utilities.Pair<Feature>>();
                IgnorePair = new List<Utilities.Pair<Feature>>();
            }


            ~Solution()
            {
                // empty
            }


            public RefFrame BlankFrame { get; set; }
            public RefFrame CadFrame { get; set; }
            public List<Utilities.Pair<Feature>> Correspondances { get; }
            public Vector3d Translation { get; set; }
            public Matrix3x3 Rotation { get; set; }
            public List<Utilities.Pair<Feature>> IgnorePair { get; }
        }


        public static bool IsCadInsideBlank(Component cad, BoundingBox blankBB, Matrix3x3 rotation, Vector3d translation)
        {
            foreach (Point3d p in cad.PointsOnEdges)
            {
                Point3d d = Utilities.MathUtils.Multiply(rotation, p);
                d = Utilities.MathUtils.Add(d, translation);
                if (!(blankBB.IsPointInBox(d) || blankBB.IsPointOnBox(d)))
                {
                    return false;
                }
            }

            return true;
        }


        // Main function
        public static bool RetrieveFeaturesToBeMachined(NXOpen.Assemblies.Component blankComponent, NXOpen.Assemblies.Component cadComponent, CAMFeature[] blankFeatures, CAMFeature[] cadFeatures,
                                                        out CAMFeature[] machinedFeatures, out string[] warningMsg, out string errorMsg)
        {
            bool success = false;

            try
            {
                BoundingBox blankBoundingBox = BoundingBox.ComputeBodyBoundingBox(Utilities.NXOpenUtils.GetComponentBodies(blankComponent).First());
                BoundingBox cadBoundingBox = BoundingBox.ComputeBodyBoundingBox(Utilities.NXOpenUtils.GetComponentBodies(cadComponent).First());

                if (!cadBoundingBox.IsInscribable(blankBoundingBox))
                {
                    throw new Exception("CAD part does not fit within the blank");
                }

                // Retrieve the CAD part
                Part cadPart = cadComponent.Prototype as Part;
                Part blankPart = blankComponent.Prototype as Part;

                // Get Blank position and orientation
                Point3d blankPosition;
                Matrix3x3 blankOrientation;
                blankComponent.GetPosition(out blankPosition, out blankOrientation);

                // Get CAD position and orientation
                Point3d cadPosition;
                Matrix3x3 cadOrientation;
                cadComponent.GetPosition(out cadPosition, out cadOrientation);
                
                // Construct and Pre-Process the Blank data
                Component blank = new Component(blankOrientation, blankPosition, blankFeatures);

                // Construct and Pre-Process the CAD data
                Component cad = new Component(cadOrientation, cadPosition, cadFeatures);

                // Find the best solution
                Solution bestSolution = Solution.FindSolutions(blank, cad, blankBoundingBox);

                // List features that needs to be machined
                List<Feature> featureList = cad.Features.ToList();
                foreach (Utilities.Pair<Feature> correspondance in bestSolution.Correspondances)
                {
                    featureList.RemoveAll(p => p == correspondance.Two);
                }

                List<string> warningMsgList = new List<string>();
                foreach (Utilities.Pair<Feature> matchingPair in bestSolution.IgnorePair)
                {
                    if (featureList.IndexOf(matchingPair.Two) != -1)
                    {
                        featureList.RemoveAll(item => item == matchingPair.Two);

                        string msg = string.Format("Blank feature '{0}' of type '{1}' matching with CAD feature '{2}' of type '{3}' will be ignored",
                                                    matchingPair.One.Name, matchingPair.Two.Type,  matchingPair.Two.Name, matchingPair.Two.Type);

                        warningMsgList.Add(msg);
                    }
                }

                // Defines the features to machine
                machinedFeatures = featureList.Select(p => p.CAMObj).ToArray();
                if (machinedFeatures.Length == 0)
                {
                    throw new Exception("No machined features were found");
                }

                warningMsg = warningMsgList.ToArray();
                errorMsg = String.Empty;
                success = true;
            }
            catch (Exception ex)
            {
                machinedFeatures = new CAMFeature[] { };
                warningMsg = new string[] { };
                errorMsg = ex.Message;
                success = false;
            }

            return success;
        }
    }
}
