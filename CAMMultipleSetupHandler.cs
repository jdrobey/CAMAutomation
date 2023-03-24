using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using System.Xml;

using NXOpen;
using NXOpen.Assemblies;
using NXOpen.CAM;
using NXOpen.Features;

namespace CAMAutomation
{
    public class CAMMultipleSetupHandler
    {
        public CAMMultipleSetupHandler(CAMAutomationManager.AutomationAlgorithm algorithm,
                                       string material,
                                       OrientGeometry mcs,
                                       FeatureGeometry mainWorkpiece,
                                       FeatureGeometry notchProbingWorkpiece,
                                       Component blankComponent,
                                       Component cadComponent,
                                       CAMFeature[] features,
                                       Utilities.CAMFixtureHandler fixtureHandler,
                                       CAMClampingConfigurator clampingConfigurator)
        {
            m_Algorithm = algorithm;

            m_material = material;

            m_mainPart = Utilities.RemoteSession.NXSession.Parts.Work;

            m_mainMcs = mcs;
            m_mainWorkpiece = mainWorkpiece;
            m_notchProbingWorkpiece = notchProbingWorkpiece;

            m_mainBlankComponent = blankComponent;
            m_mainCadComponent = cadComponent;

            m_mainFeatures = features;

            m_fixtureHandler = fixtureHandler;
            m_clampingConfigurator = clampingConfigurator;

            // Create the Feature Handler
            m_featureHandler = new CAMFeatureHandler(m_mainCadComponent, m_mainFeatures);

            // Create an empty main Run
            m_mainRun = new CAMRun();

            // Initialize warnings array
            m_warnings = new List<string>();
        }


        ~CAMMultipleSetupHandler()
        {
            // empty
        }


        public CAMRun FindOptimalSetups(out string errorMsg)
        {
            try
            {
                // Retrieve maximum number of setups
                RetrieveMaximumSetups();

                // Retrieve clamping force ratio
                RetrieveClampingForceRatio();

                // Generate First Setup
                GenerateFirstSetup();
                
                // Generate Second Setup
                GenerateSecondSetup();


                // Process Setups
                ProcessSetups();

                errorMsg = String.Empty;
                return m_mainRun;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return null;
            }
        }


        public string[] GetWarnings()
        {
            return m_warnings.ToArray();
        }


        public void GetFeaturesStatus(out CAMFeature[] usedFeatures, out CAMFeature[] unusedFeatures)
        {
            List<CAMFeature> usedFeaturesList = new List<CAMFeature>();
            List<CAMFeature> unusedFeaturesList = new List<CAMFeature>();

            foreach (CAMFeature feature in m_mainFeatures)
            {
                CAMFeature usedFeature = null;
                if (m_featureHandler.IsFeatureUsed(feature, out usedFeature))
                {
                    usedFeaturesList.Add(usedFeature);
                }
                else
                {
                    unusedFeaturesList.Add(feature);
                }
            }

            usedFeatures = usedFeaturesList.ToArray();
            unusedFeatures = unusedFeaturesList.ToArray();
        }


        private void RetrieveMaximumSetups()
        {
            string maxSetupsStr = ConfigurationManager.AppSettings["MISUMI_MAX_SETUPS"];
            if (maxSetupsStr != null && Int32.TryParse(maxSetupsStr, out m_maxSetups) && m_maxSetups > 0)
            {
                m_maxSetups = Math.Min(m_maxSetups, 6);
            }
            else
            {
                m_maxSetups = 6;
            }
        }


        private void RetrieveClampingForceRatio()
        {
            string customDir = Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM");

            if (customDir != null && Directory.Exists(customDir))
            {
               string clampingforceMap = System.IO.Path.Combine(customDir, "material_library", "material_library.xml");

                if (File.Exists(clampingforceMap))
                {
                    // Load the document
                    XmlDocument doc = new XmlDocument();
                    doc.Load(clampingforceMap);

                    // Find the clamping force ratio
                    bool valueFound = false;
                    foreach (XmlNode node in doc.DocumentElement.SelectNodes("Materials//Material"))
                    {
                        if (node.Attributes["name"] != null && node.Attributes["name"].Value == m_material)
                        {
                            if (node.Attributes["ratio"] != null && double.TryParse(node.Attributes["ratio"].Value, out double ratio) && ratio > 0.0)
                            {
                                m_clampingForceRatio = ratio;
                                valueFound = true;
                                break;
                            }
                        }
                    }

                    if (!valueFound)
                    {
                        throw new Exception("Unable to retrieve the clamping force ratio");
                    }
                }
                else
                {
                    throw new Exception("Unable to retrieve the clamping force map file");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the custom folder");
            }
        }


        private void GenerateFirstSetup()
        {
            // Create the first setup using the main mcs, main workpiece, notch probing workpiece and main component
            CAMSingleSetup firstSetup = m_mainRun.CreateSetup(m_mainMcs, m_mainWorkpiece, m_notchProbingWorkpiece, m_mainCadComponent);

            // Retrieve first setup features
            CAMFeature[] firstSetupFeatures = null;

            // The features to machine in the first setup depend on the automation algorithm
            if (m_Algorithm == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                ClampingConfiguration[] ClampingPair = m_clampingConfigurator.GetPreviousClampingConfigurations();
                if (ClampingPair.Length == 2)
                {
                    // Is there any machinable features ? If not, raise an exception
                    if (ClampingPair[0].FullyMachinableFeatures.Length == 0)
                    {
                        throw new Exception("No machinable features were found in setup \"" + firstSetup.Name + "\"");
                    }

                    // Do not machine features in the first setup that are also fully machinable in the second setup
                    CAMFeature[] fullyMachinableFeatures = ClampingPair[0].FullyMachinableFeatures.Where(p => !ClampingPair[1].FullyMachinableFeatures.Any(q => CAMFeatureHandler.AreFeaturesEquivalent(p, q))).ToArray();

                    // Add all the features that are partially machinable in the second setup
                    CAMFeature[] machinableFeatures = fullyMachinableFeatures.Concat(ClampingPair[1].PartiallyMachinableFeatures).ToArray();

                    // The above features are prototype features since they have been detected in the CAD part
                    // For each of these prototype features, we need to determine its equivalent occurrence feature, 
                    // i.e., the feature in m_mainFeatures that match that prototype feature
                    List<CAMFeature> firstSetupOccurrenceFeatures = new List<CAMFeature>();
                    foreach (CAMFeature prototypeFeature in machinableFeatures)
                    {
                        CAMFeature occurrenceFeature = m_mainFeatures.Where(p => CAMFeatureHandler.AreFeaturesEquivalent(p, prototypeFeature)).FirstOrDefault();
                        if (occurrenceFeature != null)
                        {
                            firstSetupOccurrenceFeatures.Add(occurrenceFeature);
                        }
                    }
                    firstSetupFeatures = firstSetupOccurrenceFeatures.ToArray();

                    // Is Clamping Force Sufficient for first setup
                    if (ClampingPair[0].LeverArmRatio < m_clampingForceRatio)
                    {
                        m_warnings.Add("Clamping Force is not sufficient in setup \"" + firstSetup.Name + "\"");
                    }
                }
                else
                {
                    throw new Exception("Unable to retrieve the clamping configuration pair");
                }
            }
            else
            {
                firstSetupFeatures = m_mainFeatures.ToArray();
            }

            // Is there any feature to machine in the first setup ?
            if (firstSetupFeatures.Length > 0)
            {
                // Generate Operations for the first setup
                firstSetup.GenerateOperations(firstSetupFeatures);
            }

            // Generate Toolpath
            firstSetup.GenerateToolPath();

            // Save Assembly
            SaveAssembly();
        }


        private void GenerateSecondSetup()
        {
            if (m_Algorithm == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN)
            {
                bool areAllFeaturesUsed = m_featureHandler.AreAllFeaturesUsed();

                if (m_mainRun.NumSetups < m_maxSetups && !areAllFeaturesUsed)
                {
                    // Get the Last Component
                    Component lastComponent = m_mainRun.LastComponent;

                    // Retrieve the CAD part
                    Part cadPart = lastComponent.Prototype as Part;

                    // Retrieve the CAD Fixture Datum CSYS
                    DatumCsys cadFixtureDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(cadPart, "CAD_CSYS_FIXTURE");

                    if (cadFixtureDatumCsys != null)
                    {
                        // Generate new fixture for the new setup
                        m_fixtureHandler.GenerateNewFixture();

                        // Activate Fixture mode
                        m_fixtureHandler.GetActiveFixtureHandler().ActivateFixtureMode();

                        // Create a new component for the second setup
                        Component newComponent = CreateComponent(lastComponent);

                        // Retrieve the CAD Fixture CSYS
                        CartesianCoordinateSystem cadFixtureCsys = Utilities.NXOpenUtils.GetCsysFromDatum(cadFixtureDatumCsys);

                        // Retrieve the CAD Fixture CSYS occurence in the Assembly part
                        CartesianCoordinateSystem cadFixtureCsysOccurrence = newComponent.FindOccurrence(cadFixtureCsys) as CartesianCoordinateSystem;

                        // Fix the new component
                        m_fixtureHandler.GetActiveFixtureHandler().FixComponent(newComponent, cadFixtureCsysOccurrence);

                        // Build a new setup
                        CAMSingleSetup newSetup = BuildNewSetup(newComponent,
                                                                m_fixtureHandler.GetActiveFixtureHandler().GetActiveMcs(),
                                                                m_fixtureHandler.GetActiveFixtureHandler().GetCheckBodies(false));

                        // Save Assembly
                        SaveAssembly();
                    }
                }
            }
            else if (m_Algorithm == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                bool areAllFeaturesUsed = m_featureHandler.AreAllFeaturesUsed();

                if (m_mainRun.NumSetups < m_maxSetups && !areAllFeaturesUsed)
                {
                    // Get the Last Component
                    Component lastComponent = m_mainRun.LastComponent;

                    // Retrieve the CAD part
                    Part cadPart = lastComponent.Prototype as Part;

                    // Retrieve the CAD CLAMPING Datum CSYS
                    DatumCsys cadClampingDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(cadPart, "CAD_CSYS_CLAMPING_2");

                    if (cadClampingDatumCsys != null)
                    {
                        // Get previous clamping width
                        // We will assume it will remain unchanged for the current setup
                        double clampingWidth = m_fixtureHandler.GetActiveFixtureHandler().GetClampingWidth(out Unit clampingWidthUnit);

                        // Generate new fixture for the new setup
                        m_fixtureHandler.GenerateNewFixture();

                        // Activate Clamping mode
                        m_fixtureHandler.GetActiveFixtureHandler().ActivateClampingMode();

                        // Set Clamping Width
                        m_fixtureHandler.GetActiveFixtureHandler().SetClampingWidth(clampingWidth);

                        // Create a new component for the second setup
                        Component newComponent = CreateComponent(lastComponent);

                        // Retrieve the CAD Clamping CSYS
                        CartesianCoordinateSystem cadClampingCsys = Utilities.NXOpenUtils.GetCsysFromDatum(cadClampingDatumCsys);

                        // Retrieve the CAD Clamping CSYS occurence in the Assembly part
                        CartesianCoordinateSystem cadClampingCsysOccurrence = newComponent.FindOccurrence(cadClampingCsys) as CartesianCoordinateSystem;

                        // Retrieve clamping thickness and height
                        ClampingConfiguration clampingConfiguration = m_clampingConfigurator.GetPreviousClampingConfigurations().Last();
                        double clampingThickness = clampingConfiguration.ClampingThickness;
                        double clampingHeight = clampingConfiguration.ClampingHeight;

                        // Modify the clamping thickness and height value depending on the units of the CAD and Assembly
                        double factor = Utilities.NXOpenUtils.GetConversionFactor(cadPart, m_mainPart);
                        clampingThickness *= factor;
                        clampingHeight *= factor;

                        // Set clamping thickness and height
                        m_fixtureHandler.GetActiveFixtureHandler().SetClampingThickness(clampingThickness, clampingHeight);

                        // Clamp the new component
                        m_fixtureHandler.GetActiveFixtureHandler().ClampComponent(newComponent, cadClampingCsysOccurrence);

                        // Retrieve the new MCS coordinate system
                        // If a Notch was created on the Blank, the MCS will be the Notch coordinate system
                        // If not, we use the MCS defined in the fixture assembly (as usual)
                        CartesianCoordinateSystem newMcsCsys = null;
                        Part blankPart = m_mainBlankComponent.Prototype as Part;
                        Point notchDepthCenter = blankPart.Points.ToArray().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "NOTCH_DEPTH_CENTER", out bool value) && value)
                                                                           .Select(q => m_mainBlankComponent.FindOccurrence(q) as Point)
                                                                           .FirstOrDefault();
                        if (notchDepthCenter != null)
                        {
                            // MCS Csys will have same orientation as the original one.
                            // X and Y will correspond to the NOTCH_CENTER_DEPTH point. We need to find that point in the second setup.
                            // That's why we compute the transformation between the CAD in the first setup and the CAD in the second setup.
                            // We apply then the transformation to the original MCS origin to get the new origin in the second setup
                            // For Z, the MCS origin will be at the top of the vise jaws

                            // Get Original MCS. Retrieve its origin and orientation
                            CartesianCoordinateSystem originalMcsCsys = m_fixtureHandler.GetActiveFixtureHandler().GetActiveMcs();
                            Point3d originalMcsOrigin = new Point3d(notchDepthCenter.Coordinates.X, notchDepthCenter.Coordinates.Y, notchDepthCenter.Coordinates.Z);
                            Vector3d[] originalMcsCsysAxis = Utilities.NXOpenUtils.GetCsysAxis(originalMcsCsys);

                            // Get the Transformation between the first and second setup
                            Utilities.NXOpenUtils.GetTransformation(lastComponent, newComponent, out Vector3d translation, out Matrix3x3 rotation);

                            // Apply the transormation to get the new MCS origin in the second setup
                            Point3d newMcsOrigin = Utilities.MathUtils.Add(Utilities.MathUtils.Multiply(rotation, originalMcsOrigin), translation);

                            // Set the Z as the top of the vise jaws
                            newMcsOrigin.Z = m_fixtureHandler.GetActiveFixtureHandler().GetViseJawsTopHeight(out Unit unit);

                            // Keep the same orientation as the original MCS
                            Vector3d newMcsXAxis = originalMcsCsysAxis[0];
                            Vector3d newMcsYAxis = originalMcsCsysAxis[1];

                            // Create the new MCS
                            newMcsCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(newMcsOrigin, newMcsXAxis, newMcsYAxis);
                        }
                        else
                        {
                            newMcsCsys = m_fixtureHandler.GetActiveFixtureHandler().GetActiveMcs();
                        }

                        // Build a new setup
                        CAMSingleSetup newSetup = BuildNewSetup(newComponent,
                                                                newMcsCsys,
                                                                m_fixtureHandler.GetActiveFixtureHandler().GetCheckBodies(false));

                        // Is Clamping Force Sufficient ?
                        if (clampingConfiguration.LeverArmRatio < m_clampingForceRatio)
                        {
                            m_warnings.Add("Clamping Force is not sufficient in setup \"" + newSetup.Name + "\"");
                        }

                        // Save Assembly
                        SaveAssembly();
                    }
                }
            }
        }

     


        private void ProcessSetups()
        {
            bool success = true;
            bool areAllFeaturesUsed = m_featureHandler.AreAllFeaturesUsed();

            while (success && m_mainRun.NumSetups < m_maxSetups && !areAllFeaturesUsed)
            {
                // Retrieve last component
                Component lastComponent = m_mainRun.LastComponent;

                // Retrieve body of last component
                Body lastComponentBody = Utilities.NXOpenUtils.GetComponentBodies(lastComponent).First();

                // Retrieve unused features of last setup
                CAMFeature[] unusedFeatures = m_featureHandler.GetUnusedFeatures(lastComponent);

                // Find the best clamping configuration
                success = m_clampingConfigurator.GetBestClampingConfiguration(lastComponentBody,
                                                                              unusedFeatures,
                                                                              out ClampingConfiguration clampingConfiguration);

                if (success)
                {
                    // Get previous clamping width
                    // We will assume it will remain unchanged for the current setup
                    double clampingWidth = m_fixtureHandler.GetActiveFixtureHandler().GetClampingWidth(out Unit clampingWidthUnit);

                    // Generate new fixture for the new setup
                    m_fixtureHandler.GenerateNewFixture();

                    // Activate Clamping mode
                    m_fixtureHandler.GetActiveFixtureHandler().ActivateClampingMode();

                    // Set Clamping Width
                    m_fixtureHandler.GetActiveFixtureHandler().SetClampingWidth(clampingWidth);

                    // Set the Clamping parameters (value should already be in Assembly base units)
                    m_fixtureHandler.GetActiveFixtureHandler().SetClampingThickness(clampingConfiguration.ClampingThickness, clampingConfiguration.ClampingHeight);

                    // Create a new component for the current setup
                    Component newComponent = CreateComponent(lastComponent);

                    // Clamp the new component
                    m_fixtureHandler.GetActiveFixtureHandler().ClampComponent(newComponent, clampingConfiguration.ClampingCsys);

                    // Build a new setup
                    CAMSingleSetup newSetup = BuildNewSetup(newComponent, 
                                                            m_fixtureHandler.GetActiveFixtureHandler().GetActiveMcs(),
                                                            m_fixtureHandler.GetActiveFixtureHandler().GetCheckBodies(false));

                    // Is Clamping Force Sufficient ?
                    if (clampingConfiguration.LeverArmRatio < m_clampingForceRatio)
                    {
                        m_warnings.Add("Clamping Force is not sufficient in setup \"" + newSetup.Name + "\"");
                    }

                    // Save Assembly
                    SaveAssembly();

                    // Are all Features used ?,
                    areAllFeaturesUsed = m_featureHandler.AreAllFeaturesUsed();
                }
            }

            if (success && areAllFeaturesUsed)
            {
                // Cleanup Features with empty toolpath
                //m_featureHandler.CleanObsoleteFeatures();

                // Cleanup operation with no Toolpath time
                //m_featureHandler.CleanObsoleteOperations();
            }
        }


        private void SaveAssembly()
        {
            m_mainPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }

     
        private CAMSingleSetup BuildNewSetup(Component component, CartesianCoordinateSystem mcsCsys, Body[] checkGeometry)
        {
            // Get MCS and Workpiece Name
            string mcsName = GetMcsName();
            string workpieceName = GetWorkpieceName();

            // Create Mcs
            int offset = GetMcsOffset();
            bool allAxes = m_fixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_5;
            OrientGeometry newMcs = CAMMcsHandler.CreateMcs(mcsName, mcsCsys, offset, allAxes);

            // Set Active CSYS and MCS name as attributes
            Utilities.NXOpenUtils.SetAttribute(newMcs, "MISUMI", "CSYS", m_fixtureHandler.GetActiveFixtureHandler().GetActiveCsysName());
            Utilities.NXOpenUtils.SetAttribute(newMcs, "MISUMI", "MCS", m_fixtureHandler.GetActiveFixtureHandler().GetActiveMcsName());

            // Retrieve source workpiece and create new workpiece
            FeatureGeometry sourceWorkpiece = m_mainRun.LastWorkpiece;
            FeatureGeometry newWorkpiece = CAMWorkpieceHandler.CreateWorkpiece(workpieceName, newMcs, sourceWorkpiece, component, checkGeometry);

            if (newMcs != null && newWorkpiece != null)
            {
                // Detect component features
                // We will hide the Blank component first, then show it after detection
                // This is due to a NX bug: some features may not be detected if hidden by other parts
                m_mainBlankComponent.Blank();
                CAMFeature[] componentFeatures = CAMFeatureHandler.DetectFeatures(component, out bool isDetectionComplete);
                m_mainBlankComponent.Unblank();

                // Features csys will be modified only if we have a 3-Axis machine
                bool changeFeatureCsys = m_fixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_3;
                if (changeFeatureCsys)
                {
                    CAMFeatureHandler.ChangeFeaturesCsys(componentFeatures);
                }

                // Add the component features to the Feature Handler
                m_featureHandler.AddFeatures(component, componentFeatures);

                CAMFeature[] machinableFeatures;
                if (m_Algorithm == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN && m_mainRun.NumSetups == 1)    // Second setup in STOCK_NO_HOLE_PATTERN algorithm
                {
                    ClampingConfiguration[] clampingPair = m_clampingConfigurator.GetPreviousClampingConfigurations();

                    CAMFeature[] secondSetupFeatures = clampingPair[1].FullyMachinableFeatures.Concat(clampingPair[1].GetPartiallyMachinableFeatureIntersection(clampingPair[0].PartiallyMachinableFeatures)).ToArray();

                    // The above features are prototype features since they have been detected in the CAD part
                    // For each of these prototype features, we need to determine its equivalent occurrence feature, 
                    // i.e., the feature in componentFeatures that match that prototype feature
                    List<CAMFeature> secondSetupOccurrenceFeatures = new List<CAMFeature>();
                    foreach (CAMFeature prototypeFeature in secondSetupFeatures)
                    {
                        CAMFeature occurrenceFeature = componentFeatures.Where(p => CAMFeatureHandler.AreFeaturesEquivalent(p, prototypeFeature)).FirstOrDefault();
                        if (occurrenceFeature != null)
                        {
                            secondSetupOccurrenceFeatures.Add(occurrenceFeature);
                        }
                    }

                    machinableFeatures = secondSetupOccurrenceFeatures.ToArray();
                }
                else
                {
                    // Machinable features will be the unused component features
                    machinableFeatures = m_featureHandler.GetUnusedFeatures(component);
                }

                // Create a new Single Setup
                CAMSingleSetup singleSetup = m_mainRun.CreateSetup(newMcs, newWorkpiece, null, component);

                // Is there any machinable features ? If not, raise an exception
                if (machinableFeatures.Length != 0)
                { 
                    // Generate operations using the machinable features
                    singleSetup.GenerateOperations(machinableFeatures);
                }
                else
                {
                    throw new Exception("No machinable features were found in setup \"" + singleSetup.Name + "\"");
                }

                // Generate the ToolPath
                singleSetup.GenerateToolPath();

                return singleSetup;
            }
            else
            {
                return null;
            }
        }


        private Component CreateComponent(Component sourceComponent)
        {
            // Copy Component
            Component newComponent = m_mainPart.ComponentAssembly.CopyComponents(new Component[] { sourceComponent }).First();

            // Set Component Name     
            string componentName = GetComponentName();
            newComponent.SetName(componentName);

            // Hide the source component
            sourceComponent.Blank();

            return newComponent;
        }


        private string GetMcsName()
        {
            string mcsName;
            int newSetupId = m_mainRun.NumSetups + 1;

            if (newSetupId == 1)
            {
                mcsName = "FIRST_SETUP";
            }
            else if (newSetupId == 2)
            {
                mcsName = "SECOND_SETUP";
            }
            else if (newSetupId == 3)
            {
                mcsName = "THIRD_SETUP";
            }
            else if (newSetupId == 4)
            {
                mcsName = "FOURTH_SETUP";
            }
            else if (newSetupId == 5)
            {
                mcsName = "FIFTH_SETUP";
            }
            else if (newSetupId == 6)
            {
                mcsName = "SIXTH_SETUP";
            }
            else
            {
                mcsName = "SETUP_" + newSetupId.ToString();
            }

            return mcsName;
        }


        private string GetWorkpieceName()
        {
            return m_fixtureHandler.GetActiveFixtureHandler().GetActiveOpCode();
        }


        private string GetComponentName()
        {
            // Component Name will be the same as the mcs name
            return GetMcsName();
        }


        private int GetMcsOffset()
        {
            return 54 + m_mainRun.NumSetups;
        }


        private Part m_mainPart;

        private CAMAutomationManager.AutomationAlgorithm m_Algorithm;

        private string m_material;

        private OrientGeometry m_mainMcs;
        private FeatureGeometry m_mainWorkpiece;
        private FeatureGeometry m_notchProbingWorkpiece;

        private Component m_mainBlankComponent;
        private Component m_mainCadComponent;

        private CAMFeature[] m_mainFeatures;

        private Utilities.CAMFixtureHandler m_fixtureHandler;
        private CAMClampingConfigurator m_clampingConfigurator;

        private CAMFeatureHandler m_featureHandler;

        private CAMRun m_mainRun;

        List<string> m_warnings;

        private int m_maxSetups;
        private double m_clampingForceRatio;
    }
}
