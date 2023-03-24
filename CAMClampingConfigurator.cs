using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.CAM;


namespace CAMAutomation
{
    public class CAMClampingConfigurator
    {
        public CAMClampingConfigurator(Utilities.CAMFixtureHandler fixtureHandler, ObjectiveFunctionContainer objectiveFunctions)
        {
            m_fixtureHandler = fixtureHandler;
            m_previousClampingConfigurations = new List<ClampingConfiguration>();

            ObjectiveFunctions = objectiveFunctions;

            // Retrieve the feature accessibility angle cutoff
            string featureAngleCutoffStr = System.Configuration.ConfigurationManager.AppSettings["MISUMI_FEATURE_ACCESSIBILITY_ANGLE_CUTOFF"];

            if (featureAngleCutoffStr == null || !Double.TryParse(featureAngleCutoffStr, out m_featureAngleCutoff) || m_featureAngleCutoff < 0.0 || m_featureAngleCutoff > 180.0)
            {
                m_featureAngleCutoff = 90.0;  // Default value
            }

            string minExtraMaterialStr = System.Configuration.ConfigurationManager.AppSettings["MISUMI_MIN_EXTRA_MATERIAL"];
            // The value provided is in IN.
            if (minExtraMaterialStr == null || !double.TryParse(minExtraMaterialStr, out m_minExtraMaterial) || m_minExtraMaterial < 0.0)
            {
                m_minExtraMaterial = 0.0;
            }

            string clampingExtraMaterial = System.Configuration.ConfigurationManager.AppSettings["MISUMI_CLAMPING_EXTRA_MATERIAL"];
            // The value provided is in IN.
            if (clampingExtraMaterial == null || !double.TryParse(clampingExtraMaterial, out m_clampingExtraMaterial) || m_clampingExtraMaterial < 0.0)
            {
                m_clampingExtraMaterial = 0.0;
            }

            // Retrieve the minimum clamping area
            string minClampingAreaStr = minClampingAreaStr = System.Configuration.ConfigurationManager.AppSettings["MISUMI_MINIMUM_CLAMPING_AREA"];
            if (minClampingAreaStr == null || !Double.TryParse(minClampingAreaStr, out m_minClampingArea) || m_minClampingArea < 0.0)
            {
                m_minClampingArea = 0.0;  // Default value
            }

            // Possible Clamping Height
            if (m_fixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_3)
            {
                string discreteClampingHeights = System.Configuration.ConfigurationManager.AppSettings["MISUMI_POSSIBLE_CLAMPING_HEIGHT_3AXIS"];
                m_discreteClampingHeights = ParseDiscreteValues(discreteClampingHeights);
            }
            else
            {
                string discreteClampingHeights = System.Configuration.ConfigurationManager.AppSettings["MISUMI_POSSIBLE_CLAMPING_HEIGHT_5AXIS"];
                m_discreteClampingHeights = ParseDiscreteValues(discreteClampingHeights);
            }
        }


        ~CAMClampingConfigurator()
        {
            // empty
        }


        public ClampingConfiguration[] GetPreviousClampingConfigurations()
        {
            return m_previousClampingConfigurations.ToArray();
        }


        public void AddPreviousClampingConfiguration(ClampingConfiguration[] configs)
        {
            m_previousClampingConfigurations.AddRange(configs);
        }


        public bool GetBestClampingConfiguration(Body body,
                                                 CAMFeature[] remainingFeaturesToMachine,
                                                 out ClampingConfiguration clampingConfiguration)
        {
            clampingConfiguration = null;

            // Compute body bounding box
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(body);

            // Detect all possible clamping faces
            ClampingFacesDetector clampingFacesDetector = new ClampingFacesDetector(body);
            clampingFacesDetector.ComputeClampingFaces(out string msg, out ClampingFaces[] clampingFaces);

            // Find all the remaining sorted clamping Configurations
            ClampingConfiguration[] clampingConfigurations = GetSortedClampingConfigurations(clampingFaces, remainingFeaturesToMachine, body, false);

            if (clampingConfigurations.Length == 0 || clampingConfigurations.First().FullyMachinableFeatures.Length == 0)
            {
                return false;
            }
            else
            {
                clampingConfiguration = clampingConfigurations.First();
                m_previousClampingConfigurations.Add(clampingConfiguration);

                return true;
            }
        }


        public bool GetFirstClampingConfigurationCandidates(BoundingBox cadBoundingBox, Unit bodyUnits, CAMFeature[] cadFeatures, out ClampingConfiguration[] configurationCandidates)
        {
            List<ClampingConfiguration> firstConfigurationCandidates = new List<ClampingConfiguration>();

            // This function approximate the stockClampingFaces from bounding box and extra material define in app.config 
            // a more precise version would require reading from stock library for available stock.
            ClampingFaces[] stockClampingFaces = GetClampingFacesOnExtraMaterialBoundingBox(cadBoundingBox, bodyUnits);

            foreach (ClampingFaces cf in stockClampingFaces)
            {
                // Up orientatiopn when clamping on ClampingFaces
                Vector3d up = Utilities.MathUtils.Inverse(cf.BottomPlane.Normal);

                CAMFeature[] fullyMachinableFeatures = cadFeatures.Where(p => IsMachinable(p, up)).ToArray();

                CAMFeature[] nonMachinableFeature = cadFeatures.Where(p => !fullyMachinableFeatures.Contains(p)).ToArray();

                // Don't partially machine holes or pockets
                CAMFeature[] partiallyMachinableFeatures = nonMachinableFeature.Where(p => !p.Type.Contains("POCKET") && !p.Type.Contains("HOLE") && IsInMachiningAxis(p)).ToArray();

                double viseHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit viseHeightUnit);
                viseHeight = Utilities.NXOpenUtils.ConvertValue(viseHeight, viseHeightUnit, bodyUnits);

                // Constructar Clamping Configuration from clamping face bounding box reference 
                firstConfigurationCandidates.Add(new ClampingConfiguration(null, cf, fullyMachinableFeatures, partiallyMachinableFeatures, viseHeight));
            }

            // Order according to Objective Function
            configurationCandidates = firstConfigurationCandidates.ToArray();
            if (ObjectiveFunctions != null && ObjectiveFunctions.SingleClamping != null && configurationCandidates.Length > 0)
            {
                configurationCandidates = ObjectiveFunctions.SingleClamping.FilterNonFiniteLambdaOutput(configurationCandidates);
                configurationCandidates = SortAndUpdate(configurationCandidates);

                ObjectiveFunctions.SingleClampingCSVWriter(configurationCandidates, "1");
            }

            if (configurationCandidates.Length == 0 || configurationCandidates.First().FullyMachinableFeatures.Length == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }


        ClampingConfiguration[] SortAndUpdate(ClampingConfiguration[] configurations)
        {
            Tuple<ClampingConfiguration, double>[] configurationOrderedTuples = ObjectiveFunctions.SingleClamping.SortTuple(configurations,
                ClampingConfiguration.RollAngle, ClampingConfiguration.PitchAngle, ClampingConfiguration.YawAngle);

            List<ClampingConfiguration> sortedConfiguration = new List<ClampingConfiguration>();
            int index = 0;
            foreach (Tuple<ClampingConfiguration, double> configurationOrderedTuple in configurationOrderedTuples)
            {
                // copy ClampingConfiguration
                ClampingConfiguration c = configurationOrderedTuple.Item1;
                ClampingConfiguration configuration = new ClampingConfiguration(c.Body, c.ClampingFaces, c.FullyMachinableFeatures, c.PartiallyMachinableFeatures, c.ClampingHeight);

                // update the copy
                configuration.ObjectiveFunctionValue = configurationOrderedTuple.Item2;
                configuration.Priority = index++;
                sortedConfiguration.Add(configuration);
            }

            return sortedConfiguration.ToArray();
        }
        

        public bool GetSecondClampingConfigurationCandidates(Body body,
                                                             CAMFeature[] remainingFeaturesToMachine,
                                                             out ClampingConfiguration[] clampingConfigurations)
        {
            // Detect all possible clamping faces
            ClampingFacesDetector clampingFacesDetector = new ClampingFacesDetector(body);
            clampingFacesDetector.ComputeClampingFaces(out string msg, out ClampingFaces[] clampingFaces);

            // Find all the remaining sorted clamping Configurations
            clampingConfigurations = GetSortedClampingConfigurations(clampingFaces, remainingFeaturesToMachine, body, true);

            // Allow FullyMachinableFeatures of Length == 0 to be processed as a result of the chosen objective funtcion
            if (clampingConfigurations.Length == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }


        private ClampingFaces[] GetClampingFacesOnExtraMaterialBoundingBox(BoundingBox boundingBox, Unit bodyUnits)
        {
            // Update the Bounding box for extra material then calculate ClampingFaces
            PolygonFace[] faces = boundingBox.GetFaces();
            IEnumerable<IEnumerable<PolygonFace>> leftRightFaceSet = Utilities.SetTheoryUtils.Categories(faces, Utilities.MathUtils.AreParallel);

            List<ClampingFaces> cfs = new List<ClampingFaces>();

            double viseExtraHeight = Utilities.NXOpenUtils.ConvertValue(m_fixtureHandler.GetActiveFixtureHandler().GetViseExtraHeight(out Unit unit), unit, bodyUnits);

            bool isBodyInMilliMeter = bodyUnits.TypeName == "MilliMeter";
            double clampingExtraMaterial = isBodyInMilliMeter ? m_clampingExtraMaterial * 25.4 : m_clampingExtraMaterial;
            double minExtraMaterial = isBodyInMilliMeter ? m_minExtraMaterial * 25.4 : m_minExtraMaterial;

            // Comparaison of double with Tolerence
            Utilities.ToleranceDouble compareDouble = new Utilities.ToleranceDouble();

            // Find bottom planes
            foreach (IEnumerable<PolygonFace> polygonFaces in leftRightFaceSet)
            {
                IEnumerable<PolygonFace> sortedPolygonFaces = polygonFaces.OrderByDescending(p => p.Normal.X, compareDouble)
                                                                          .ThenByDescending(p => p.Normal.Y, compareDouble)
                                                                          .ThenByDescending(p => p.Normal.Z, compareDouble);

                PolygonFace[] candidateBottomPlanes = faces.Where(p => !sortedPolygonFaces.Contains(p)).ToArray();
                foreach (PolygonFace bottomPlane in candidateBottomPlanes)
                {
                    // Create minimal BoundingBox with the proper extramaterial 
                    Vector3d clampingExtraMaterialDir = Utilities.MathUtils.UnitVector(bottomPlane.Normal);
                    double maxClampingExtraMaterial = Math.Max(clampingExtraMaterial, minExtraMaterial);

                    double xPlus = Utilities.MathUtils.AreCodirectional(clampingExtraMaterialDir, boundingBox.XUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                    double yPlus = Utilities.MathUtils.AreCodirectional(clampingExtraMaterialDir, boundingBox.YUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                    double zPlus = Utilities.MathUtils.AreCodirectional(clampingExtraMaterialDir, boundingBox.ZUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                    double xMinus = Utilities.MathUtils.AreAntiparallel(clampingExtraMaterialDir, boundingBox.XUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                    double yMinus = Utilities.MathUtils.AreAntiparallel(clampingExtraMaterialDir, boundingBox.YUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                    double zMinus = Utilities.MathUtils.AreAntiparallel(clampingExtraMaterialDir, boundingBox.ZUnitDirection) ? maxClampingExtraMaterial : minExtraMaterial;
                        
                    // Cleate the stock exact BoundingBox with extramaterial
                    Vector3d x = Utilities.MathUtils.Multiply(boundingBox.XUnitDirection, boundingBox.XLength + xMinus + xPlus);
                    Vector3d y = Utilities.MathUtils.Multiply(boundingBox.YUnitDirection, boundingBox.YLength + yMinus + yPlus);
                    Vector3d z = Utilities.MathUtils.Multiply(boundingBox.ZUnitDirection, boundingBox.ZLength + zMinus + zPlus);
                    Point3d origin = Utilities.MathUtils.Add(boundingBox.MinCornerPoint,
                                                             Utilities.MathUtils.Multiply(boundingBox.XUnitDirection, -xMinus),
                                                             Utilities.MathUtils.Multiply(boundingBox.YUnitDirection, -yMinus),
                                                             Utilities.MathUtils.Multiply(boundingBox.ZUnitDirection, -zMinus));

                    BoundingBox bb = BoundingBox.CreateBoundingBox(origin, x, y, z);

                    // Construct the Clamping faces Bottum PolygonFace which is a plane
                    PolygonFace bottum = bb.GetFaces().Where(p => Utilities.MathUtils.AreCodirectional(p.Normal, bottomPlane.Normal)).First();

                    // Clip Clamping face at the height of the viseExtraHeight to have the real clamping face
                    Utilities.Plane viceTopPlane = bottum.Flip().Move(Utilities.MathUtils.Multiply(clampingExtraMaterialDir, -viseExtraHeight));
                    PolygonFace one = bb.GetFaces().Where(p => Utilities.MathUtils.AreCodirectional(p.Normal, sortedPolygonFaces.First().Normal)).First().Clip(viceTopPlane);
                    PolygonFace two = bb.GetFaces().Where(p => Utilities.MathUtils.AreCodirectional(p.Normal, sortedPolygonFaces.Last().Normal)).Last().Clip(viceTopPlane);

                    cfs.Add(new ClampingFaces(bb, one, two, bottum));
                    cfs.Add(new ClampingFaces(bb, two, one, bottum));
                }
            }

            return cfs.ToArray();
        }


        private ClampingConfiguration[] GetSortedClampingConfigurations(ClampingFaces[] clampingFaces, CAMFeature[] featuresToMachine, Body body, bool isPairedConfiguration)
        {
            List<ClampingConfiguration> clampingConfigurationsList = new List<ClampingConfiguration>();
            foreach (ClampingFaces clampingFace in clampingFaces)
            {
                ClampingConfiguration[] configs = GetMachinableFeatures(body, clampingFace, featuresToMachine, isPairedConfiguration);               
                clampingConfigurationsList.AddRange(configs);  
            }

            // Filter out Previous Clamping Configurations and to small ClampingHeight
            ClampingConfiguration[] clampingConfigurations = clampingConfigurationsList.Where(p => !m_previousClampingConfigurations.Any(q => p.IsEquivalent(q))).ToArray();

            // Order according to Objective Function
            if (ObjectiveFunctions != null && ObjectiveFunctions.SingleClamping != null && clampingConfigurations.Length > 0)
            {
                clampingConfigurations = ObjectiveFunctions.SingleClamping.FilterNonFiniteLambdaOutput(clampingConfigurations);
                clampingConfigurations = SortAndUpdate(clampingConfigurations);

                int setupNumber = isPairedConfiguration ? m_previousClampingConfigurations.Count() + 2 : m_previousClampingConfigurations.Count() + 1;
                ObjectiveFunctions.SingleClampingCSVWriter(clampingConfigurations, setupNumber.ToString());
            }

            return clampingConfigurations;
        }



        public Utilities.Pair<ClampingConfiguration> GetBestClampingPairOption(Utilities.Pair<ClampingConfiguration>[] clampingConfigurationPairs)
        {
            Func<ClampingConfiguration, double> Roll = ClampingConfiguration.RollAngle;
            Func<ClampingConfiguration, double> Pitch = ClampingConfiguration.PitchAngle;
            Func<ClampingConfiguration, double> Yaw = ClampingConfiguration.YawAngle;
            Func<ClampingConfiguration, double> ClampingHeight = (c) => c.ClampingHeight;

            // Order according to Objective Function
            Tuple<Utilities.Pair<ClampingConfiguration>, double>[] sortedConfigurationTuples = null;
            if (ObjectiveFunctions != null && ObjectiveFunctions.PairClamping != null && clampingConfigurationPairs.Length > 0)
            {
                clampingConfigurationPairs = ObjectiveFunctions.PairClamping.FilterNonFiniteLambdaOutput(clampingConfigurationPairs);
                sortedConfigurationTuples = ObjectiveFunctions.PairClamping.SortTuple(clampingConfigurationPairs,
                        Utilities.Pair.Func(Roll).One, Utilities.Pair.Func(Pitch).One, Utilities.Pair.Func(Yaw).One,
                        Utilities.Pair.Func(Roll).Two, Utilities.Pair.Func(Pitch).Two, Utilities.Pair.Func(Yaw).Two,
                        Utilities.Pair.Func(ClampingHeight).Two);
 
                ObjectiveFunctions.PairClampingCSVWriter(sortedConfigurationTuples);
            }

            bool configurationTuplesIsValid = sortedConfigurationTuples != null && sortedConfigurationTuples.Length > 0;
            return configurationTuplesIsValid ? sortedConfigurationTuples.First().Item1: clampingConfigurationPairs.First();
        }


        private ClampingConfiguration[] GetMachinableFeatures(Body body, ClampingFaces clampingFace, CAMFeature[] remainingFeatures, bool isPairedConfiguration)
        {
            List<ClampingConfiguration> clampingConfigurations = new List<ClampingConfiguration>();

            double viseHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit viseHeightUnit); 
            viseHeight = Utilities.NXOpenUtils.ConvertValue(viseHeight, viseHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

            double[] candidateClampingHeights = GetCandidateClampingHeights(body, clampingFace, remainingFeatures).Where(p => p < viseHeight).OrderByDescending(p => p).ToArray();

            // Starting with the biggest height. Check if the clamping area is valid
            foreach (double candidateClampingHeight in candidateClampingHeights)
            {
                // Check if clamping at this height yields a valid clamping area
                if (IsClampingAreaValid(body, clampingFace, candidateClampingHeight, out ClampingFaces updatedClampingFace))
                {
                    // Get Machinable Features at given clamping Height
                    if (GetFeaturesMachinableAtGivenHeight(body, clampingFace, remainingFeatures, candidateClampingHeight, isPairedConfiguration, 
                                                           out CAMFeature[] fullyMachinableFeatures, out CAMFeature[] partiallyMachinableFeatures))
                    {
                        clampingConfigurations.Add(new ClampingConfiguration(body, updatedClampingFace, fullyMachinableFeatures, partiallyMachinableFeatures, candidateClampingHeight));
                    }
                }
            }

            return clampingConfigurations.ToArray();
        }


        private double[] GetCandidateClampingHeights(Body body, ClampingFaces clampingFace, CAMFeature[] remainingFeatures)
        {
            if (m_discreteClampingHeights == null || m_discreteClampingHeights.Length == 0)
            {
                // We are adding the vise extra height and extra material because for a feature to be machinable it has to clear
                // the stock clamping area by an amount equal to the extra material.  we do this for every feature
                double viseHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit viseHeightUnit);
                viseHeight = Utilities.NXOpenUtils.ConvertValue(viseHeight, viseHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

                double viseExtraHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseExtraHeight(out Unit viseExtraHeightUnit);
                viseExtraHeight = Utilities.NXOpenUtils.ConvertValue(viseExtraHeight, viseExtraHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

                // Clamping height in this case is the viseHeight minus the feature minimum distance Plus extra material and viseExtraHeight
                return GetAllFeaturesLowestDistanceToPlane(clampingFace.BottomPlane, remainingFeatures).Select(p => viseHeight - p.Value + viseExtraHeight).ToArray();
            }
            else
            {
                // Discrete Height values are in INCH. Convert to MM if necessary
                double factor = body.OwningPart.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1.0;
                return m_discreteClampingHeights.Select(p => p * factor).ToArray();
            }
        }


        private bool IsClampingAreaValid(Body body, ClampingFaces clampingFace, double candidateClampingHeight, out ClampingFaces updatedClampingFace)
        {
            double viseWidth = m_fixtureHandler.GetActiveFixtureHandler().GetViseWidth(out Unit viseWidthUnit);
            viseWidth = Utilities.NXOpenUtils.ConvertValue(viseWidth, viseWidthUnit, body.OwningPart.UnitCollection.GetBase("Length"));

            // Vertical height of the Vise face = ViseFaceHeight - clampingHeight
            double viseHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit viseHeightUnit);
            viseHeight = Utilities.NXOpenUtils.ConvertValue(viseHeight, viseHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

            double effectiveViseFaceHeight = viseHeight - candidateClampingHeight;

            PolygonFace one;
            PolygonFace two;

            // Try/Catch Beacose of PointLoop algorithm limitations. 
            try
            {
                PolygonFace viseFace = GetViseFaceAsPolygon(clampingFace, viseWidth, effectiveViseFaceHeight);
                one = clampingFace.One.IntersectProjectedFace(viseFace);
                two = clampingFace.Two.IntersectProjectedFace(viseFace);
            }
            catch
            {
                updatedClampingFace = null;
                return false;
            }

            // Check Clamping Area
            // Min clamping area is provided in INCH^2
            // Conversion should be performed if part/body is in MM
            double areaOne = one.GetArea();
            double areaTwo = two.GetArea();
            double factor = body.OwningPart.PartUnits == BasePart.Units.Millimeters ? Math.Pow(25.4, 2.0) : 1.0;
            if (areaOne > m_minClampingArea * factor && areaTwo > m_minClampingArea * factor && Utilities.MathUtils.IsNeighbour(areaOne, areaTwo))
            {
                updatedClampingFace = new ClampingFaces(body, one, two, clampingFace.BottomPlane);
                return true;
            }
            else
            {
                updatedClampingFace = null;
                return false;
            }
        }


        private bool GetFeaturesMachinableAtGivenHeight(Body body, ClampingFaces clampingFace, CAMFeature[] remainingFeatures,
                                                       double clampingHeight, bool isPairedConfiguration, out CAMFeature[] fullyMachinableFeatures,
                                                       out CAMFeature[] partiallyMachinableFeatures)
        {
            List<KeyValuePair<CAMFeature, double>> featureMinDistances = GetAllFeaturesLowestDistanceToPlane(clampingFace.BottomPlane, remainingFeatures);

            double viseHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit viseHeightUnit);
            viseHeight = Utilities.NXOpenUtils.ConvertValue(viseHeight, viseHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

            double viseExtraHeight = m_fixtureHandler.GetActiveFixtureHandler().GetViseExtraHeight(out Unit viseExtraHeightUnit);
            viseExtraHeight = Utilities.NXOpenUtils.ConvertValue(viseExtraHeight, viseExtraHeightUnit, body.OwningPart.UnitCollection.GetBase("Length"));

            // Height that any feature must clear to be fully machinable
            double featureClearanceHeight = viseHeight + viseExtraHeight + EXTRA_CLAMPING_HEIGHT;

            // Allow Machining of pockets below the plane that are in the machining axis, the logic being that the feature
            // does not border on the edge of a part, it is a concave feature of the part by definition of a pocket and so 
            // there is no risk for the tool to damage the fixture/clamp while machining this feature within the vise faces
            CAMFeature[] pocketFeatures = featureMinDistances.Where(p => p.Value + clampingHeight < featureClearanceHeight)
                                                             .Select(p => p.Key)
                                                             .Where(p => (p.Type.Contains("STEP1HOLE") || p.Type.Contains("STEP2HOLE") || p.Type.Contains("POCKET")) &&
                                                                          IsMachinable(p, Utilities.MathUtils.Inverse(clampingFace.BottomPlane.Normal)) &&
                                                                          // Get Features where the length of the projection of its normal along the clamping Face normal line is near zero
                                                                          Utilities.MathUtils.IsNeighbour(Utilities.MathUtils.Length(Utilities.MathUtils.Projection(new Vector3d(p.CoordinateSystem.Orientation.Element.Zx,
                                                                                                                                                                                 p.CoordinateSystem.Orientation.Element.Zy,
                                                                                                                                                                                 p.CoordinateSystem.Orientation.Element.Zz),
                                                                                                                                                                    Utilities.MathUtils.Inverse(clampingFace.One.Normal))),
                                                                                                         0.0,
                                                                                                         Utilities.MathUtils.ABS_TOL)).ToArray();

            // Get all features that are fully above the supplied ClampingHeight
            CAMFeature[] machinableFeatures = featureMinDistances.Where(p => p.Value + clampingHeight > featureClearanceHeight)
                                                                 .Select(p => p.Key)
                                                                 .Where(p => IsMachinable(p, Utilities.MathUtils.Inverse(clampingFace.BottomPlane.Normal))).ToArray();

            machinableFeatures = machinableFeatures.Concat(pocketFeatures).Distinct().ToArray();
            fullyMachinableFeatures = machinableFeatures;

            if (isPairedConfiguration)
            {
                // All these features are either below the plane, intersecting the plane, or have a face with a downward normal
                CAMFeature[] nonMachinableFeatures = remainingFeatures.Where(p => !machinableFeatures.Contains(p)).ToArray();
                partiallyMachinableFeatures = GetPartiallyMachinableFeatures(nonMachinableFeatures, clampingFace.BottomPlane, clampingHeight, featureClearanceHeight);
            }
            else
            {
                partiallyMachinableFeatures = new CAMFeature[] { };
            }

            return (fullyMachinableFeatures.Length + partiallyMachinableFeatures.Length) != 0;
        }


        private CAMFeature[] GetPartiallyMachinableFeatures(CAMFeature[] remainingFeaturesToMachine, Utilities.Plane bottomPlane, double clampingHeight, double featureClearanceHeight)
        {          
            // Compute offset distance
            double offsetDistance = featureClearanceHeight - clampingHeight;

            // Offset the bottom plane upward by the clamping height
            Point3d XYPlaneOrigin = Utilities.MathUtils.Add(bottomPlane.Origin, Utilities.MathUtils.Multiply(Utilities.MathUtils.UnitVector(Utilities.MathUtils.Inverse(bottomPlane.Normal)), offsetDistance));
            Utilities.Plane zPlane = new Utilities.Plane(XYPlaneOrigin, Utilities.MathUtils.Inverse(bottomPlane.Normal));

            List<CAMFeature> partiallyMachinable = new List<CAMFeature>();
            foreach (CAMFeature feature in remainingFeaturesToMachine)
            {
                if ((!feature.Type.Contains("POCKET") && !feature.Type.Contains("HOLE")) && IsInMachiningAxis(feature) || IntersectsMachinablePlane(feature, zPlane))
                {
                    partiallyMachinable.Add(feature);
                }
            }

            return partiallyMachinable.ToArray();
        }


        private bool IntersectsMachinablePlane(CAMFeature feature, Utilities.Plane machinablePlane)
        {
            foreach (Edge edge in feature.GetFaces().First().GetEdges())
            {
                edge.GetVertices(out Point3d start, out Point3d end);

                // Check if any of the edges intersect the plane which features must clear to be fully machinable
                if (Utilities.MathUtils.GetIntersection(new Utilities.Line(start, end), machinablePlane, out Point3d intersect))
                {
                    // This features has a edge that intersects the plane so it is partially machinable
                    // The part above the plane being the machinable part
                    return true;
                }
            }

            return false;
        }


        private bool IsMachinable(CAMFeature feature, Vector3d machiningAxis)
        {
            if (m_fixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_5)
            {
                if (feature.Type.Contains("POCKET") || feature.Type.Contains("STEP1HOLE") || feature.Type.Contains("STEP2HOLE"))
                {

                    return Utilities.MathUtils.InSolidAngle(new Vector3d (feature.CoordinateSystem.Orientation.Element.Zx,
                                                                          feature.CoordinateSystem.Orientation.Element.Zy,
                                                                          feature.CoordinateSystem.Orientation.Element.Zz),
                                                            machiningAxis,
                                                            Utilities.MathUtils.ToRadians(2.0 * m_featureAngleCutoff));
                }
                else
                {
                    // Check if all of the features faces are in solid angle
                    return feature.GetFaces().All(p => Utilities.MathUtils.InSolidAngle(Utilities.NXOpenUtils.GetFaceNormal(p), machiningAxis, Utilities.MathUtils.ToRadians(2.0 * m_featureAngleCutoff)));
                }
            }
            else
            {
                // Hack for STEP2HOLE, the normal of the planar face gets inverted probably because
                // We are taking the normal at a point in the hole of a washer shaped face
                if (feature.Type.Contains("STEP2HOLE"))
                {
                    Face[] planarFaces = feature.GetFaces().Where(p => p.SolidFaceType == Face.FaceType.Planar).ToArray();
                    return planarFaces.All(p => Utilities.MathUtils.AreCodirectional(machiningAxis, Utilities.MathUtils.Inverse(Utilities.NXOpenUtils.GetFaceNormal(p))));
                }
                if (feature.Type.Contains("HOLE") || feature.Type.Contains("POCKET"))
                {
                    // Planar components of the hole must be codirectional to the tool's up axis
                    Face[] planarFaces = feature.GetFaces().Where(p => p.SolidFaceType == Face.FaceType.Planar).ToArray();
                    if (planarFaces.Length != 0)
                    {
                        return planarFaces.All(p => Utilities.MathUtils.AreCodirectional(machiningAxis, Utilities.NXOpenUtils.GetFaceNormal(p)));
                    }
                }

                return true;
            }
        }


        private bool IsInMachiningAxis(CAMFeature feature)
        {
            if (m_fixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_5)
            {
                Matrix3x3 matrix = m_fixtureHandler.GetActiveFixtureHandler().GetClampingCsys().Orientation.Element;
                Vector3d zAxis = new Vector3d(matrix.Zx, matrix.Zy, matrix.Zz);

                // Check if any of the features faces are in solid angle
                return feature.GetFaces().Any(p => Utilities.MathUtils.InSolidAngle(Utilities.NXOpenUtils.GetFaceNormal(p), zAxis, Utilities.MathUtils.ToRadians(2.0 * m_featureAngleCutoff)));
            }
            else
            {
                return true;
            }
        }


        private PolygonFace GetViseFaceAsPolygon(ClampingFaces clampingFaces, double viseFaceWidth, double viseHeight)
        {
            // This is the bottom center of the Vise Face, we only model the face above this 
            Point3d viseFaceOrigin = clampingFaces.BottomPlane.Origin;

            Vector3d xAxis = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Cross(clampingFaces.One.Normal, clampingFaces.BottomPlane.Normal));
            Vector3d zAxis = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Inverse(clampingFaces.BottomPlane.Normal));

            Point3d bottomLeft = Utilities.MathUtils.Add(viseFaceOrigin, Utilities.MathUtils.Multiply(xAxis, -viseFaceWidth / 2.0));
            Point3d bottomRight = Utilities.MathUtils.Add(viseFaceOrigin, Utilities.MathUtils.Multiply(xAxis, viseFaceWidth / 2.0));

            Point3d topRight = Utilities.MathUtils.Add(viseFaceOrigin, 
                                                       Utilities.MathUtils.Multiply(xAxis, viseFaceWidth / 2.0), 
                                                       Utilities.MathUtils.Multiply(zAxis, viseHeight)); 

            Point3d topLeft = Utilities.MathUtils.Add(viseFaceOrigin, 
                                                      Utilities.MathUtils.Multiply(xAxis, -viseFaceWidth / 2.0), 
                                                      Utilities.MathUtils.Multiply(zAxis, viseHeight));

            return new PolygonFace(new Point3d[] {bottomLeft, bottomRight, topRight, topLeft}, viseFaceOrigin, clampingFaces.One.Normal);
        }


        private List<KeyValuePair<CAMFeature, double>> GetAllFeaturesLowestDistanceToPlane(Utilities.Plane bottomPlane, CAMFeature[] remainingFeaturesToMachine)
        {
            List<KeyValuePair<CAMFeature, double>> featureDistancesToPlane = new List<KeyValuePair<CAMFeature, double>>();
            foreach (CAMFeature feature in remainingFeaturesToMachine)
            {
                double minDistanceToBottomPlane = GetMinDistanceToBottomPlane(feature, bottomPlane);
                featureDistancesToPlane.Add(new KeyValuePair<CAMFeature, double>(feature, minDistanceToBottomPlane));
            }

            return featureDistancesToPlane.OrderBy(p => p.Value).ToList();
        }


        private double GetMinDistanceToBottomPlane(CAMFeature feature, Utilities.Plane bottomPlane)
        {
            double min = double.MaxValue;
            foreach (Face face in feature.GetFaces())
            {
                if (face.SolidFaceType == Face.FaceType.Planar)
                {
                    min = Math.Min(min, GetMinDistance(new PolygonFace(face), bottomPlane));
                }
                else
                {
                    // Get Bounding Box of the face
                    BoundingBox box = BoundingBox.ComputeFaceBoundingBox(face);
                    Point3d[] cornerPoints = box.GetCornerPoints();

                    foreach (Point3d corner in cornerPoints)
                    {
                        min = Math.Min(min, Utilities.MathUtils.GetDistance(corner, bottomPlane));
                    }
                }
            }

            return min;
        }


        private double GetMinDistance(PolygonFace face, Utilities.Plane clampingFace)
        {
            return face.GetPoints().Select(p => Utilities.MathUtils.GetDistance(p, clampingFace)).Min();
        }


        private double[] ParseDiscreteValues(string str)
        {          
            List<double> discreteValues = new List<double>();

            if (!String.IsNullOrEmpty(str))
            {
                string[] values = str.Split(',');
                foreach (string value in values)
                {
                    if (Double.TryParse(value, out double doubleValue) && doubleValue >= 0.0)
                    {
                        discreteValues.Add(doubleValue);
                    }
                }
            }

            return discreteValues.ToArray();
        }

        private const double EXTRA_CLAMPING_HEIGHT = 0.01;

        private Utilities.CAMFixtureHandler m_fixtureHandler;

        private List<ClampingConfiguration> m_previousClampingConfigurations;

        private double m_featureAngleCutoff;        // In degrees
        private double m_minClampingArea;           // In Square Inches
        private double[] m_discreteClampingHeights; // In Inches

        private double m_minExtraMaterial;
        private double m_clampingExtraMaterial;

        private ObjectiveFunctionContainer ObjectiveFunctions { get; }
    }
}
