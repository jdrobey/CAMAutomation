using System;
using System.Linq;

using NXOpen;
using NXOpen.CAM;
using System.Collections.Generic;

namespace CAMAutomation
{
    public class ClampingConfiguration
    {
        static public double RollAngle(ClampingConfiguration cc)
        {
            return Utilities.SO3Utils.GetEulerAngles(cc.ReferenceClampingCsys.Orientation.Element).Roll;
        }


        static public double PitchAngle(ClampingConfiguration cc)
        {
            return Utilities.SO3Utils.GetEulerAngles(cc.ReferenceClampingCsys.Orientation.Element).Pitch;
        }


        static public double YawAngle(ClampingConfiguration cc)
        {
            return Utilities.SO3Utils.GetEulerAngles(cc.ReferenceClampingCsys.Orientation.Element).Yaw;
        }


        public ClampingConfiguration(Body body, ClampingFaces clampingFaces, CAMFeature[] fullyMachinableFeatures, CAMFeature[] partiallyMachinableFeatures, double clampingHeight)
        {
            Body = body;
            BodyBoundingBox = clampingFaces.Box;
            ClampingFaces = clampingFaces;
            FullyMachinableFeatures = fullyMachinableFeatures;
            PartiallyMachinableFeatures = partiallyMachinableFeatures;
            ClampingThickness = Utilities.MathUtils.GetDistance(clampingFaces.One, clampingFaces.Two);
            ClampingHeight = clampingHeight;
            LeverArmRatio = GetLeverArmRatio(ClampingFaces, FullyMachinableFeatures);

            // Compute Csys
            ComputeCsys();

            // Compute Reference Csys
            ComputeReferences();
        }


        ~ClampingConfiguration()
        {
            // empty
        }


        public CAMFeature[] GetPartiallyMachinableFeatureIntersection(CAMFeature[] partiallyMachinableFeatures)
        {
            return PartiallyMachinableFeatures.Where(p => partiallyMachinableFeatures.Any(q => CAMFeatureHandler.AreFeaturesEquivalent(p, q))).ToArray();
        }


        public CAMFeature[] GetFullyMachinableFeatureIntersection(CAMFeature[] fullyMachinableFeatures)
        {
            return FullyMachinableFeatures.Where(p => fullyMachinableFeatures.Any(q => CAMFeatureHandler.AreFeaturesEquivalent(p, q))).ToArray();
        }


        public bool IsEquivalent(ClampingConfiguration other)
        {
            if (Utilities.MathUtils.IsNeighbour(ClampingHeight, other.ClampingHeight) &&
                Utilities.MathUtils.IsNeighbour(ClampingThickness, other.ClampingThickness) &&
                Utilities.MathUtils.IsNeighbour(LeverArmRatio, other.LeverArmRatio))
            {
                return Utilities.MathUtils.IsNeighbour(ReferenceClampingCsys.Origin, other.ReferenceClampingCsys.Origin) &&
                       Utilities.MathUtils.IsNeighbour(ReferenceClampingCsys.Orientation.Element, other.ReferenceClampingCsys.Orientation.Element);
            }
            else
            {
                return false;
            }
        }


        public double GetClampingArea()
        {
            return ClampingFaces.One.GetArea();
        }


        public double GetReferenceClampingArea()
        {
            return (Body != null && Body.IsOccurrence) ? GetClampingArea() * Math.Pow(GetConversionFactor(), 2.0) : GetClampingArea();
        }


        public double GetGravityCenterHeight()
        {
            Point3d centerPoint = (Body != null) ? Utilities.NXOpenUtils.GetCentroid(Body) : BodyBoundingBox.GetBoxCenter();

            return Utilities.MathUtils.Dot(Utilities.MathUtils.UnitVector(ClampingFaces.BottomPlane.Normal),
                           Utilities.MathUtils.GetVector(centerPoint, ClampingFaces.BottomPlane.Origin))
                           + ClampingHeight;
        }


        public double GetReferenceGravityCenterHeight()
        {
            return (Body != null && Body.IsOccurrence) ? GetGravityCenterHeight() * GetConversionFactor() : GetGravityCenterHeight();
        }


        public double GetBoundingBoxDimension(Func<IEnumerable<double>, double> sequenceFunction)
        {
            return sequenceFunction((new double[] { BodyBoundingBox.XLength, BodyBoundingBox.YLength, BodyBoundingBox.ZLength }));
        }


        public double GetReferenceBoundingBoxDimension(Func<IEnumerable<double>, double> sequenceFunction)
        {
            return (Body != null && Body.IsOccurrence) ? GetBoundingBoxDimension(sequenceFunction) * GetConversionFactor() : GetBoundingBoxDimension(sequenceFunction);
        }


        public double CalculateShortestClampingFaceToBottomPlaneDistance()
        {
            // Get all the points of the clampingFaces Polygons
            Point3d[] points = ClampingFaces.One.GetPoints().Concat(ClampingFaces.Two.GetPoints()).ToArray();

            return Math.Abs(points.Max(p => Math.Abs(Utilities.MathUtils.GetDistance(p, ClampingFaces.BottomPlane))));
        }


        public void CenterCsys()
        {
            // Compute body bounding box along the clamping csys
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(Body, ClampingCsys);

            // Retrieve the bounding box center point
            Point3d boxCenter = boundingBox.GetBoxCenter();

            // Up direction = Inverse of bottom plane normal
            Vector3d upDirection = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Inverse(ClampingFaces.BottomPlane.Normal));

            ClampingCsys.Origin = new Point3d(Utilities.MathUtils.IsNeighbour(upDirection.X, 0.0) ? boxCenter.X : ClampingCsys.Origin.X,
                                              Utilities.MathUtils.IsNeighbour(upDirection.Y, 0.0) ? boxCenter.Y : ClampingCsys.Origin.Y,
                                              Utilities.MathUtils.IsNeighbour(upDirection.Z, 0.0) ? boxCenter.Z : ClampingCsys.Origin.Z);

            // Re-compute the references
            ComputeReferences();
        }

        private double GetLeverArmRatio(ClampingFaces clampingFaces, CAMFeature[] machinableFeatures)
        {
            return clampingFaces.One.GetRadiusOfGyration(clampingFaces.One.Normal) / (Math.Sqrt(clampingFaces.One.GetArea()) *
                                                         GetLongestLeverArm(machinableFeatures, clampingFaces.One.GetCentroid(), clampingFaces.One.Normal));
        }


        private double GetLongestLeverArm(CAMFeature[] features, Point3d centroid, Vector3d rotationAxis)
        {
            double longestLeverArm = double.MinValue;

            foreach (CAMFeature feature in features)
            {
                foreach (Face face in feature.GetFaces())
                {
                    foreach (Edge edge in face.GetEdges())
                    {
                        edge.GetVertices(out Point3d p1, out Point3d p2);
                        Utilities.Plane rotationPlane = new Utilities.Plane(rotationAxis);
                        double d1 = Utilities.MathUtils.Length(Utilities.MathUtils.Projection(Utilities.MathUtils.GetDistanceVector(centroid, p1), rotationPlane));
                        double d2 = Utilities.MathUtils.Length(Utilities.MathUtils.Projection(Utilities.MathUtils.GetDistanceVector(centroid, p2), rotationPlane));

                        longestLeverArm = Math.Max(d1, d2) > longestLeverArm ? Math.Max(d1, d2) : longestLeverArm;
                    }
                }
            }

            return longestLeverArm;
        }
        

        private double GetConversionFactor()
        {
            return Body.IsOccurrence ? Utilities.NXOpenUtils.GetConversionFactor(Body.OwningPart, Body.Prototype.OwningPart) : 1.0;
        }


        private void ComputeCsys()
        {
            Vector3d xVector = Utilities.MathUtils.Cross(ClampingFaces.BottomPlane.Normal, ClampingFaces.One.Normal);
            Vector3d yVector = ClampingFaces.One.Normal;

            ClampingCsys = Utilities.NXOpenUtils.CreatePersistentCsys(ClampingFaces.BottomPlane.Origin, xVector, yVector);
        }


        private void ComputeReferences()
        {
            // We compute here the ReferenceClampingCsys that represents the ClampingCsys in the ACS of the Part
            // If the body is a prototye, both Csys are the same
            if (Body != null && Body.IsOccurrence)
            {
                Utilities.ClampingUtils.GetClampingConfigulationFromAssemblyContext(Body.OwningComponent, Body.OwningPart as Part, ClampingCsys, ClampingThickness, ClampingHeight,
                                                                                    out CartesianCoordinateSystem referenceClampingCsys, out double referenceClampingThickness, out double referenceClampingHeight);
                ReferenceClampingCsys = referenceClampingCsys;
                ReferenceClampingThickness = referenceClampingThickness;
                ReferenceClampingHeight = referenceClampingHeight;
                ReferenceLeverArmRatio = LeverArmRatio / GetConversionFactor();
            }
            else
            {
                ReferenceClampingCsys = ClampingCsys;
                ReferenceClampingThickness = ClampingThickness;
                ReferenceClampingHeight = ClampingHeight;
                ReferenceLeverArmRatio = LeverArmRatio;
            }
        }


        public Body Body { get; }
        public BoundingBox BodyBoundingBox { get; }

        public ClampingFaces ClampingFaces { get; }

        public CAMFeature[] FullyMachinableFeatures { get; }
        public CAMFeature[] PartiallyMachinableFeatures { get; set; }

        public double ClampingThickness { get; }
        public double ReferenceClampingThickness { get; private set; }

        public double ClampingHeight { get; }
        public double ReferenceClampingHeight { get; private set; }

        public double LeverArmRatio { get; }
        public double ReferenceLeverArmRatio { get; private set; }

        public CartesianCoordinateSystem ClampingCsys { get; private set; }
        public CartesianCoordinateSystem ReferenceClampingCsys { get; private set; }

        public int Priority { get; internal set; }
        public double ObjectiveFunctionValue { get; internal set; }
    }
}
