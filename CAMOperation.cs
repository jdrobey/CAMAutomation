using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Configuration;

using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Features;
using NXOpen.CAM;
using NXOpen.Facet;
using NXOpen.SIM;

namespace CAMAutomation
{
    public class CAMOperation
    {
        public CAMOperation(Part assemblyPart)
        {
            m_assemblyPart = assemblyPart;
            m_manager = CAMAutomationManager.GetInstance();
        }


        ~CAMOperation()
        {
            // empty
        }


        public bool Run()
        {
            try
            {
                // Create CAM Session
                CreateCAMSession();

                //Select Roughing Template
                SelectRoughingTemplate();

                // Set Part Material
                SetPartMaterial();

                // Set CAM Preferences
                SetCAMPreferences();

                // Set Fixture Handler mode
                SetFixtureHandlerMode();

                // Add Blank and CAD components
                AddBlankAndCADComponents();

                // Position Blank and CAD for initial setup
                PositionBlankAndCADForInitialSetup();

                // Define Clamping Exclusion Zone
                DefineClampingExclusionZone();

                //Add Face Attributes
                AddAttributes();

                // Detect Features
                DetectFeatures();

                // Retrieve Feature to be machined
                RetrieveFeaturesToBeMachined();

                // Check Probing State
                CheckProbingState();

                // Retrieve and edit main Mcs
                RetrieveAndEditMainMcs();

                // Retrieve and edit main workpiece
                RetrieveAndEditMainWorkpiece();

                // Edit Probing Operations
                EditProbingOperations();

                // ProcessMultipleSetups 
                ProcessMultipleSetups();

                // Simulate Tool path
                SimulateToolPath();

                // Post-Process
                PostProcess();

                // Compute Manufacturing Time
                ComputeManufacturingTime();

                // Perform SucessCheck
                PerformSuccessCheck();

                // Create Probe Points
                FindAndCreateProbePoints();

                // Save Assembly
                SaveAssembly();
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);
                m_manager.CAMReport.SetStatus(CAMReport.Status.FAILURE);

                return false;
            }

            return true;
        }


        private void CreateCAMSession()
        {
            m_manager.LogFile.AddMessage("Creating CAM Session ...");

            // Make assembly display part
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_assemblyPart, false, true, out PartLoadStatus status);

            // Create CAM Session
            Utilities.RemoteSession.NXSession.CreateCamSession();

            // Set CAM Machine
            string customDir = Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM");
            if (customDir != null)
            {
                string camMachineName = customDir + "\\" + m_manager.Machine.MachineID;
                Utilities.RemoteSession.NXSession.CAMSession.SpecifyConfiguration(camMachineName);
            }
            else
            {
                throw new Exception("Unable to set the CAMMachine");
            }
        }


        private void SelectRoughingTemplate()
        {
            m_manager.LogFile.AddMessage("Selecting Roughing Template ...");

            string unitSystem = m_assemblyPart.PartUnits == BasePart.Units.Inches ? "english" : "metric";
            CAMRoughingTemplateSelector roughingSelector = new CAMRoughingTemplateSelector(m_manager.Machine.Type,
                                                                                           m_manager.Machine.MachineID,
                                                                                           m_manager.Material,
                                                                                           m_manager.GetAutomationAlgorithm(),
                                                                                           unitSystem);
            string template = roughingSelector.SelectRoughingTemplate();
            m_manager.LogFile.AddMessage("Roughing Template " + template + " has been selected"); 

            // Switch Application
            Utilities.RemoteSession.NXSession.ApplicationSwitchImmediate("UG_APP_MANUFACTURING");
            CAMSetup setup = m_assemblyPart.CreateCamSetup(template);
        }


        private void SetPartMaterial()
        {
            m_manager.LogFile.AddMessage("Set Part Material ...");

            try
            {
                m_assemblyPart.CAMSetup.SetPartMaterial(m_manager.Material);
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddWarning("Unable to set the part material: " + ex.Message);
            }
        }


        private void SetCAMPreferences()
        {
            m_manager.LogFile.AddMessage("Set CAM Preferences ...");

            // Create the Preference Builder
            Preferences preference = Utilities.RemoteSession.NXSession.CAMSession.CreateCamPreferences();

            // Options
            preference.AutomaticallySetMachingData = true;

            // Commit the builder
            preference.Commit();

            // Destroy the builder
            preference.Destroy();
        }


        private void SetFixtureHandlerMode()
        {
            m_manager.LogFile.AddMessage("Set Fixture Handler Mode ...");

            // Activate the right mode depending on the automation algorithm
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                m_manager.CAMFixtureHandler.GetActiveFixtureHandler().ActivateFixtureMode();
            }
            else
            {
                m_manager.CAMFixtureHandler.GetActiveFixtureHandler().ActivateClampingMode();
            }
        }


        private void AddBlankAndCADComponents()
        {
            m_manager.LogFile.AddMessage("Add Blank and CAD components ...");

            // Add the BLANK and CAD components at the Absoulte Coordinate System of the Assembly part
            Point3d position = new Point3d();
            Matrix3x3 rotation = Utilities.MathUtils.Identity();
            PartLoadStatus status;
            m_blankComponent = m_assemblyPart.ComponentAssembly.AddComponent(m_manager.BlankPart, "None", "BLANK", position, rotation, -1, out status);
            m_cadComponent = m_assemblyPart.ComponentAssembly.AddComponent(m_manager.CADPart, "None", "CAD", position, rotation, -1, out status);

            // Reset name of the CAD component
            m_cadComponent.SetName("FIRST_SETUP");
        }


        private void PositionBlankAndCADForInitialSetup()
        {
            m_manager.LogFile.AddMessage("Position Blank and CAD for initial setup ...");

            // Blank from Stock
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN ||
                m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                string blankCsysName = m_manager.IsNearNetBlank ? "BLANK_CSYS_CLAMPING_NEAR_NET_BLANK" : "BLANK_CSYS_CLAMPING";
                string cadCsysName = m_manager.IsNearNetBlank ? "CAD_CSYS_CLAMPING_NEAR_NET_BLANK" : "CAD_CSYS_CLAMPING";

                // Retrieve the Blank and CAD Clamping Datum CSYS
                DatumCsys blankClampingDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(m_manager.BlankPart, blankCsysName);
                DatumCsys cadClampingDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(m_manager.CADPart, cadCsysName);

                if (blankClampingDatumCsys != null && cadClampingDatumCsys != null)
                {
                    // Retrieve the Blank and CAD Clamping CSYS
                    CartesianCoordinateSystem blankClampingCsys = Utilities.NXOpenUtils.GetCsysFromDatum(blankClampingDatumCsys);
                    CartesianCoordinateSystem cadClampingCsys = Utilities.NXOpenUtils.GetCsysFromDatum(cadClampingDatumCsys);

                    // Retrieve the BLANK and CAD Clamping CSYS occurence in the Assembly part
                    CartesianCoordinateSystem blankClampingCsysOccurrence = m_blankComponent.FindOccurrence(blankClampingCsys) as CartesianCoordinateSystem;
                    CartesianCoordinateSystem cadClampingCsysOccurrence = m_cadComponent.FindOccurrence(cadClampingCsys) as CartesianCoordinateSystem;

                    // Retrieve the Clamping Thickness and Clamping Width
                    Utilities.NXOpenUtils.GetAttribute(blankClampingDatumCsys, "MISUMI", "CLAMPING_THICKNESS", out double clampingThicknessValue);
                    Utilities.NXOpenUtils.GetAttribute(blankClampingDatumCsys, "MISUMI", "CLAMPING_WIDTH", out double clampingWidthValue);

                    // Modify the clamping thickness and clamping width value depending on the units of the Blank and Assembly
                    clampingThicknessValue *= Utilities.NXOpenUtils.GetConversionFactor(m_manager.BlankPart, m_assemblyPart);
                    clampingWidthValue *= Utilities.NXOpenUtils.GetConversionFactor(m_manager.BlankPart, m_assemblyPart);

                    // Set the Clamping Width
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().SetClampingWidth(clampingWidthValue);

                    // Set Clamping Thickness and Height
                    double clampingHeight = m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetViseHeight(out Unit unit);
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().SetClampingThickness(clampingThicknessValue, clampingHeight);

                    // Clamp Blank and CAD component
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().ClampComponent(m_blankComponent, blankClampingCsysOccurrence);
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().ClampComponent(m_cadComponent, cadClampingCsysOccurrence);
                }
                else if (blankClampingDatumCsys == null)
                {
                    throw new Exception("Unable to find the " + blankCsysName + " coordinate system in the BLANK part");
                }
                else if (cadClampingDatumCsys == null)
                {
                    throw new Exception("Unable to find the " + cadCsysName + " coordinate system in the CAD part");
                }
            }

            // Prefinished Blank with HOLE PATTERN
            else if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                string blankCsysName = "BLANK_CSYS_FIXTURE";
                string cadCsysName = "CAD_CSYS_FIXTURE";

                // Retrieve the Blank and CAD Fixture Datum CSYS
                DatumCsys blankFixtureDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(m_manager.BlankPart, blankCsysName);
                DatumCsys cadFixtureDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(m_manager.CADPart, cadCsysName);

                if (blankFixtureDatumCsys != null && cadFixtureDatumCsys != null)
                {
                    // Retrieve Blank and CAD Fixture CSYS
                    CartesianCoordinateSystem blankFixtureCsys = Utilities.NXOpenUtils.GetCsysFromDatum(blankFixtureDatumCsys);
                    CartesianCoordinateSystem cadFixtureCsys = Utilities.NXOpenUtils.GetCsysFromDatum(cadFixtureDatumCsys);

                    // Retrieve the Blank and CAD Fixture CSYS occurence in the Assembly part
                    CartesianCoordinateSystem blankFixtureCsysOccurrence = m_blankComponent.FindOccurrence(blankFixtureCsys) as CartesianCoordinateSystem;
                    CartesianCoordinateSystem cadFixtureCsysOccurrence = m_cadComponent.FindOccurrence(cadFixtureCsys) as CartesianCoordinateSystem;

                    // Fix Blank and CAD component.
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().FixComponent(m_blankComponent, blankFixtureCsysOccurrence);
                    m_manager.CAMFixtureHandler.GetActiveFixtureHandler().FixComponent(m_cadComponent, cadFixtureCsysOccurrence);
                }
                else if (blankFixtureDatumCsys == null)
                {
                    throw new Exception("Unable to find the " + blankCsysName + " coordinate system in the BLANK part");
                }
                else if (cadFixtureDatumCsys == null)
                {
                    throw new Exception("Unable to find the " + cadCsysName + " coordinate system in the CAD part");
                }
            }

            // Prefinished Blank without HOLE PATTERN
            else if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_NO_HOLE_PATTERN)
            {
                // Pathway not supported
            }
        }


        private void DefineClampingExclusionZone()
        {
            m_manager.LogFile.AddMessage("Defining clamping exclusion zone ...");

            // Blank from Stock
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN ||
                m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                // Retrieve the Blank body
                Body blankBody = Utilities.NXOpenUtils.GetComponentBodies(m_blankComponent).First();

                // Compute the Blank bounding box along the clamping csys
                CartesianCoordinateSystem clampingCsys = m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetClampingCsys();
                BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(blankBody, clampingCsys);

                // Retrieve the length of the bounding box along X since X is along the jaws width
                double blankXLength = boundingBox.XLength;

                // Retrieve the vise width
                double viseWidth = m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetViseWidth(out Unit unit);

                // Set the Clamping Exclusion Zone length
                // The length is as wide as jaws or as wide as blank, whichever is greater
                double exclusionZoneLength = Math.Max(blankXLength, viseWidth);
                m_manager.CAMFixtureHandler.GetActiveFixtureHandler().SetClampingExclusionZoneLength(exclusionZoneLength);
            }
        }
        private void AddAttributes()
        {
            ComponentFaceAttributeHandler handler = new ComponentFaceAttributeHandler();
            m_manager.LogFile.AddMessage("Building and Adding Face Attributes...");
            CAMFaceAnalyzer comp = new CAMFaceAnalyzer(m_cadComponent);
            //foreach (var face in comp.Cylinders) {  }
            //foreach (var face in comp.Cones) {  }
            foreach (var face in comp.Planes) { handler.BuildAttributes(face); }
            //foreach (var face in comp.Bsurfs) {  }
            //foreach (var face in comp.Nurbs) {  }
        }

        private void DetectFeatures()
        {
            m_manager.LogFile.AddMessage("Detecting Features ...");

            // Detect Blank Features
            // We will hide the CAD component first, then show it after detection
            // This is due to a NX bug: some features may not be detected if hidden by other parts
            m_cadComponent.Blank();
            m_blankFeatures = CAMFeatureHandler.DetectFeatures(m_blankComponent, out bool isBlankFeatureDetectionComplete);
            m_cadComponent.Unblank();
            if (m_blankFeatures == null || m_blankFeatures.Length == 0)
            {
                throw new Exception("Unable to detect the Blank Features");
            }
            else if (!isBlankFeatureDetectionComplete)
            {
                throw new Exception("BLANK Faces with no identified feature were found. Teach feature in order to recognize those faces");
            }

            // Detect CAD Features
            // We will hide the Blank component first, then show it after detection
            // This is due to a NX bug: some features may not be detected if hidden by other parts
            m_blankComponent.Blank();
            m_cadFeatures = CAMFeatureHandler.DetectFeatures(m_cadComponent, out bool isCADFeatureDetectionComplete);
            m_blankComponent.Unblank();

            if (m_cadFeatures == null || m_cadFeatures.Length == 0)
            {
                throw new Exception("Unable to detect the CAD Features");
            }
            else if (!isCADFeatureDetectionComplete)
            {
                m_manager.LogFile.AddWarning("CAD Faces with no identified feature were found. Teach feature in order to recognize those faces");
                m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
            }

            // Features csys will be modified only if we have a 3-Axis machine
            bool changeFeatureCsys = m_manager.Machine.Type == Machine.MachineType.AXIS_3 ? true : false;
            if (changeFeatureCsys)
            {
                CAMFeatureHandler.ChangeFeaturesCsys(m_blankFeatures);
                CAMFeatureHandler.ChangeFeaturesCsys(m_cadFeatures);
            }
        }


        private void RetrieveFeaturesToBeMachined()
        {
            m_manager.LogFile.AddMessage("Retrieve Features to be machined ...");

            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN ||
                m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_NO_HOLE_PATTERN)
            {

                bool success = MachinedFeaturesDetector.RetrieveFeaturesToBeMachined(m_blankComponent, m_cadComponent,
                                                                                     m_blankFeatures, m_cadFeatures,
                                                                                     out m_machinedFeatures, out string[] warningMsg, out string errorMsg);

                if (success)
                {
                    // Retrieve the warning messages from the MachinedFeaturesDetector
                    foreach (string msg in warningMsg)
                    {
                        m_manager.LogFile.AddWarning(msg);
                    }
                }
                else
                {
                    throw new Exception("Unable to retrieve the features to be machined: " + errorMsg);
                }
            }
            else
            {
                m_machinedFeatures = m_cadFeatures;
            }

            // Populate features to machine in Manager
            m_manager.CAMReport.AddTotalFeaturesToMachine(m_machinedFeatures.Select(s => s.Type).ToArray());
        }


        private void CheckProbingState()
        {
            m_manager.LogFile.AddMessage("Checking Probing State ...");

            string enableProbingStr = ConfigurationManager.AppSettings["MISUMI_ENABLE_PROBING"];
            if (enableProbingStr != null && int.TryParse(enableProbingStr, out int enableProbing) && enableProbing == 1)
            {
                m_enableProbing = true;
            }
        }


        private void RetrieveAndEditMainMcs()
        {
            m_manager.LogFile.AddMessage("Retrieving and Editing main MCS ...");

            m_mainMcs = Utilities.NXOpenUtils.GetMcsByName(m_assemblyPart, "FIRST_SETUP");

            if (m_mainMcs == null)
            {
                m_mainMcs = Utilities.NXOpenUtils.GetMcsByName(m_assemblyPart, "MCS_MILL");

                if (m_mainMcs != null)
                {
                    // Reset the name of the Mcs
                    m_mainMcs.SetName("FIRST_SETUP");
                }
                else
                {
                    throw new Exception("Unable to find the initial MCS \"FIRST_SETUP\" or \"MCS_MILL\"");
                }
            }

            if (m_mainMcs != null)
            {
                int offset = 54;
                bool allAxes = m_manager.CAMFixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_5;
                CAMMcsHandler.EditMcs(m_mainMcs, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveMcs(), offset, allAxes);

                // Set Active CSYS and MCS name as attributes
                Utilities.NXOpenUtils.SetAttribute(m_mainMcs, "MISUMI", "CSYS", m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveCsysName());
                Utilities.NXOpenUtils.SetAttribute(m_mainMcs, "MISUMI", "MCS", m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveMcsName());
            }
        }


        private void RetrieveAndEditMainWorkpiece()
        {
            m_manager.LogFile.AddMessage("Retrieving and Editing main workpiece ...");

            // Main Worpiece
            m_mainWorkpiece = m_mainMcs.GetMembers().FirstOrDefault(p => p.Name.StartsWith("WORKPIECE")) as FeatureGeometry;
            if (m_mainWorkpiece != null)
            {
                // Reset the name of the workpiece to be the OpCode
                m_mainWorkpiece.SetName(m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveOpCode());

                // Edit the workpiece
                CAMWorkpieceHandler.EditWorkpiece(m_mainWorkpiece, m_blankComponent, m_cadComponent, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetCheckBodies(true));
            }
            else
            {
                throw new Exception("Unable to find the initial Workpiece");
            }

            // Notch Probing Workpiece (only for STOCK_NO_HOLE_PATTERN pathway)
            if (m_enableProbing)
            {
                if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
                {
                    m_notchProbingWorkpiece = m_mainMcs.GetMembers().FirstOrDefault(p => p.Name == "NOTCH_PROBING") as FeatureGeometry;

                    if (m_notchProbingWorkpiece != null)
                    {
                        CAMWorkpieceHandler.EditWorkpiece(m_notchProbingWorkpiece, m_mainWorkpiece, m_cadComponent, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetCheckBodies(false));
                    }
                    else
                    {
                        throw new Exception("Unable to retrieve the notch probing workpiece");
                    }
                }
            }
        }


        private void EditProbingOperations()
        {
            m_manager.LogFile.AddMessage("Edit Probing Operations ...");

            bool keepFirstSetupProbingOperation = false;
            bool keepFirstSetupNotchOperation = false;
            bool keepSecondSetupProbingOperation = false;

            if (m_enableProbing)
            {
                if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN ||
                    m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
                {
                    // Edit First Setup Probing Operation
                    EditFirstSetupProbingOperation();
                    keepFirstSetupProbingOperation = true;

                    // Notch Probing and Second Setup Probing will occur only if there is no Hole Pattern.
                    // Indeed, for a Stock Hole Pattern pathway, the second setup is in the fixture.
                    // Therefore no probing is needed.
                    if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
                    {
                        // Edit First Setup Notch Operation
                        EditFirstSetupNotchOperation();
                        keepFirstSetupNotchOperation = true;

                        // Edit Second Setup Probing Operation
                        EditSecondSetupProbingOperation();
                        keepSecondSetupProbingOperation = true;
                    }
                }
            }

            // Do we keep the First Setup Probing Operation ?
            if (!keepFirstSetupProbingOperation)
            {
                NXOpen.CAM.Operation operation = m_assemblyPart.CAMSetup.CAMOperationCollection.ToArray().FirstOrDefault(p => p.Name == "PROBING_OPERATION_1");
                if (operation != null)
                {
                    NCGroup parent = operation.GetParent(CAMSetup.View.ProgramOrder);
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { operation });
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { parent });
                }
            }

            // Do we keep the First Setup Notch Operation ?
            if (!keepFirstSetupNotchOperation)
            {
                FeatureGeometry workpiece = m_mainMcs.GetMembers().FirstOrDefault(p => p.Name == "NOTCH_PROBING") as FeatureGeometry;
                if (workpiece != null)
                {
                    NXOpen.CAM.Operation operation = workpiece.GetMembers().FirstOrDefault() as NXOpen.CAM.Operation;
                    NCGroup parent = operation.GetParent(CAMSetup.View.ProgramOrder);
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { workpiece });
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { parent });
                }
            }

            // Do we keep the Second Setup Probing Operation ?
            if (!keepSecondSetupProbingOperation)
            {
                NXOpen.CAM.Operation operation = m_assemblyPart.CAMSetup.CAMOperationCollection.ToArray().FirstOrDefault(p => p.Name == "PROBING_OPERATION_2");
                if (operation != null)
                {
                    NCGroup parent = operation.GetParent(CAMSetup.View.ProgramOrder);
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { operation });
                    Utilities.RemoteSession.NXSession.UpdateManager.AddObjectsToDeleteList(new TaggedObject[] { parent });
                }
            }

            // Update Session
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, "Delete_Probing_Objects");
            Utilities.RemoteSession.NXSession.UpdateManager.DoUpdate(undoMark);
            Utilities.RemoteSession.NXSession.DeleteUndoMark(undoMark, "Delete_Probing_Objects");
        }


        private void EditFirstSetupProbingOperation()
        {
            m_manager.LogFile.AddMessage("Edit First Setup Probing Operation ...");

            // Retrieve the Blank body
            Body blankBody = Utilities.NXOpenUtils.GetComponentBodies(m_blankComponent).First();

            // Compute the Blank bounding box along the clamping csys
            CartesianCoordinateSystem clampingCsys = m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetClampingCsys();
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(blankBody, clampingCsys);

            // Retrieve the length of the bounding box along X, which reperesents the blank width
            double blankWidth = boundingBox.XLength;

            // Retrieve the length of the bounding box along Z, which reperesents the blank height
            double blankHeight = boundingBox.ZLength;

            // Retrieve the probing distance
            double probingDistance = m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetViseProbingDistance(out Unit unit);

            // Retrieve the safe clearance 
            string safeClearanceStr = ConfigurationManager.AppSettings["MISUMI_PROBING_SAFE_CLEARANCE"];
            if (safeClearanceStr == null || !double.TryParse(safeClearanceStr, out double safeClearance) || safeClearance < 0.0)
            {
                safeClearance = 0.2;
            }
            double factor = m_assemblyPart.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1;
            safeClearance *= factor;

            // Define total width and height
            double totalWidth = blankWidth;
            double totalHeight = blankHeight + probingDistance + safeClearance;

            // Retrieve probing operation in the first setup
            NXOpen.CAM.Operation probingOperation = m_assemblyPart.CAMSetup.CAMOperationCollection.ToArray().FirstOrDefault(p => p.Name == "PROBING_OPERATION_1");
            if (probingOperation == null)
                throw new Exception("Unable to retrieve the probing operation in the first setup");

            // Retrieve probing template for the first setup
            string probingTemplate = System.IO.Path.Combine(Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM"),
                                                           "probing_operation",
                                                           "probing_operation_1.SPF");
            if (File.Exists(probingTemplate))
            {
                // Read template file and replace %WIDTH% and %HEIGHT% by correct values
                string[] lines = File.ReadAllLines(probingTemplate).Select(p => p.Trim())
                                                                   .Where(q => !String.IsNullOrEmpty(q) && !q.StartsWith(";"))
                                                                   .Select(r => r.Replace("%WIDTH%", totalWidth.ToString()))
                                                                   .Select(r => r.Replace("%HEIGHT%", totalHeight.ToString()))
                                                                   .ToArray();

                if (lines.Length > 0)
                {
                    // Create the operation builder handler
                    Utilities.CAMOperationBuilderHandler handler = new Utilities.CAMOperationBuilderHandler(probingOperation);

                    // For each line, add User Defined Event to the probing operation                     
                    foreach (string line in lines)
                    {
                        handler.AddUserDefinedEvent(line);
                    }

                    // Release Create the operation builder handler
                    handler.Release();
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the probing template for the first setup");
            }
        }


        private void EditFirstSetupNotchOperation()
        {
            m_manager.LogFile.AddMessage("Editing First Setup Notch Operation ...");

            // Retrieve Probing Notch curves
            Curve[] probingNotchCurves = m_manager.BlankPart.Curves.ToArray().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "NOTCH_CURVE", out bool value) && value)
                                                                             .Select(q => m_blankComponent.FindOccurrence(q) as Curve)
                                                                             .ToArray();

            // Retrieve Probing Notch face
            Face probingNotchFace = m_manager.BlankPart.Bodies.ToArray().First().GetFaces().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "NOTCH_FACE", out bool value) && value)
                                                                                           .Select(q => m_blankComponent.FindOccurrence(q) as Face)
                                                                                           .FirstOrDefault();

            // Retrieve Notch Depth
            string notchDepthStr = ConfigurationManager.AppSettings["MISUMI_NOTCH_DEPTH"];
            if (notchDepthStr != null && Double.TryParse(notchDepthStr, out double notchDepth))
            {
                if (notchDepth > 0.0)
                {
                    double factor = m_assemblyPart.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1;
                    notchDepth *= factor;
                }
                else
                {
                    throw new Exception("Invalid notch depth value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the notch depth");
            }

            // Retrieve Notch Probing Operation
            NXOpen.CAM.Operation notchProbingOperation = m_notchProbingWorkpiece.GetMembers().FirstOrDefault() as NXOpen.CAM.Operation;
            if (notchProbingOperation != null)
            {
                // Create the operation builder handler
                Utilities.CAMOperationBuilderHandler handler = new Utilities.CAMOperationBuilderHandler(notchProbingOperation);

                // Set Trimming Curves
                handler.SetTrimmingCurves(probingNotchCurves);

                // Set Cut Level
                handler.SetCutLevel(probingNotchFace, notchDepth);

                // Release Create the operation builder handler
                handler.Release();
            }
            else
            {
                throw new Exception("Unable to retrieve the notch probing operation");
            }
        }


        private void EditSecondSetupProbingOperation()
        {
            m_manager.LogFile.AddMessage("Editing Second Setup Probing Operation ...");

            // Conversion factor
            double factor = m_assemblyPart.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1;

            // Retrieve the safe clearance
            string safeClearanceStr = ConfigurationManager.AppSettings["MISUMI_PROBING_SAFE_CLEARANCE_2"];
            if (safeClearanceStr == null || !double.TryParse(safeClearanceStr, out double safeClearance) || safeClearance < 0.0)
            {
                safeClearance = 0.2;
            }
            safeClearance *= factor;

            // Retrieve the probing diameter
            string probingDiameterStr = ConfigurationManager.AppSettings["MISUMI_PROBING_DIAMETER"];
            if (probingDiameterStr != null && double.TryParse(probingDiameterStr, out double probingDiameter))
            {
                if (probingDiameter > 0.0)
                {
                    probingDiameter *= factor;
                }
                else
                {
                    throw new Exception("Invalid probing diameter value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the probing diameter");
            }

            // Retrieve Notch Width
            string notchWidthStr = ConfigurationManager.AppSettings["MISUMI_NOTCH_WIDTH"];
            if (notchWidthStr != null && Double.TryParse(notchWidthStr, out double notchWidth))
            {
                if (notchWidth > 0.0)
                {
                    notchWidth *= factor;
                }
                else
                {
                    throw new Exception("Invalid notch width value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the notch width");
            }

            // Retrieve Notch Depth
            string notchDepthStr = ConfigurationManager.AppSettings["MISUMI_NOTCH_DEPTH"];
            if (notchDepthStr != null && Double.TryParse(notchDepthStr, out double notchDepth))
            {
                if (notchDepth > 0.0)
                {
                    notchDepth *= factor;
                }
                else
                {
                    throw new Exception("Invalid notch depth value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the notch depth");
            }

            // Compute CYCLE977_DFA and CYCLE977_TSA
            double CYCLE977_DFA = (notchWidth - probingDiameter) / 2.0;
            double CYCLE977_TSA = notchWidth / 2.0;

            // Retrieve the relevant points
            Point notchCenter = m_manager.BlankPart.Points.ToArray().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "NOTCH_CENTER", out bool value) && value)
                                                                    .Select(q => m_blankComponent.FindOccurrence(q) as Point)
                                                                    .FirstOrDefault();

            Point notchTop = m_manager.BlankPart.Points.ToArray().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "NOTCH_TOP", out bool value) && value)
                                                                 .Select(q => m_blankComponent.FindOccurrence(q) as Point)
                                                                 .FirstOrDefault();

            Point probingTop = m_manager.BlankPart.Points.ToArray().Where(p => Utilities.NXOpenUtils.GetAttribute(p, "MISUMI", "PROBING_TOP", out bool value) && value)
                                                                   .Select(q => m_blankComponent.FindOccurrence(q) as Point)
                                                                   .FirstOrDefault();

            // Those points are expressed in the Absolute Coordinate System
            // We need to find their coordinates in the MCS
            Point3d notchCenterInMcs = Utilities.NXOpenUtils.MapPointFromAbsoluteToCsys(notchCenter.Coordinates, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveMcs());
            Point3d notchTopInMcs = Utilities.NXOpenUtils.MapPointFromAbsoluteToCsys(notchTop.Coordinates, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveMcs());
            Point3d probingTopInMcs = Utilities.NXOpenUtils.MapPointFromAbsoluteToCsys(probingTop.Coordinates, m_manager.CAMFixtureHandler.GetActiveFixtureHandler().GetActiveMcs());

            // Compute releavant parameters
            double xValue = notchTopInMcs.X - (probingDiameter / 6.0);
            double yValue = notchCenterInMcs.Y;
            double zValue = probingTopInMcs.Z;
            double zSafeValue = zValue + safeClearance;

            // Retrieve probing operation in the second setup
            NXOpen.CAM.Operation probingOperation = m_assemblyPart.CAMSetup.CAMOperationCollection.ToArray().FirstOrDefault(p => p.Name == "PROBING_OPERATION_2");
            if (probingOperation == null)
                throw new Exception("Unable to retrieve the probing operation in the second setup");

            // Retrieve probing template for the second setup
            string probingTemplate = System.IO.Path.Combine(Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM"),
                                                           "probing_operation",
                                                           "probing_operation_2.SPF");
            if (File.Exists(probingTemplate))
            {
                // Read template file and replace %WIDTH% and %HEIGHT% by correct values
                string[] lines = File.ReadAllLines(probingTemplate).Select(p => p.Trim())
                                                                   .Where(q => !String.IsNullOrEmpty(q) && !q.StartsWith(";"))
                                                                   .Select(r => r.Replace("%X_VALUE%", xValue.ToString()))
                                                                   .Select(r => r.Replace("%Y_VALUE%", yValue.ToString()))
                                                                   .Select(r => r.Replace("%HEIGHT_2%", zValue.ToString()))
                                                                   .Select(r => r.Replace("%HEIGHT_SAFE_2%", zSafeValue.ToString()))
                                                                   .Select(r => r.Replace("%NOTCH_WIDTH%", notchWidth.ToString()))
                                                                   .Select(r => r.Replace("%NOTCH_DEPTH%", notchDepth.ToString()))
                                                                   .Select(r => r.Replace("%CYCLE977_DFA%", CYCLE977_DFA.ToString()))
                                                                   .Select(r => r.Replace("%CYCLE977_TSA%", CYCLE977_TSA.ToString()))
                                                                   .ToArray();

                if (lines.Length > 0)
                {
                    // Create the operation builder handler
                    Utilities.CAMOperationBuilderHandler handler = new Utilities.CAMOperationBuilderHandler(probingOperation);

                    // For each line, add User Defined Event to the probing operation                     
                    foreach (string line in lines)
                    {
                        handler.AddUserDefinedEvent(line);
                    }

                    // Release Create the operation builder handler
                    handler.Release();
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the probing template for the second setup");
            }
        }


        private void ProcessMultipleSetups()
        {
            m_manager.LogFile.AddMessage("Processing Multiple Setups ...");

            // Create the Multiple Setups Manager
            CAMMultipleSetupHandler multipleSetupHandler = new CAMMultipleSetupHandler(m_manager.GetAutomationAlgorithm(), 
                                                                                       m_manager.Material,
                                                                                       m_mainMcs, 
                                                                                       m_mainWorkpiece,
                                                                                       m_notchProbingWorkpiece,
                                                                                       m_blankComponent, 
                                                                                       m_cadComponent, 
                                                                                       m_machinedFeatures, 
                                                                                       m_manager.CAMFixtureHandler,
                                                                                       m_manager.CAMClampingConfigurator);

            // Find the optimal setups
            m_bestRun = multipleSetupHandler.FindOptimalSetups(out string errorMsg);

            if (m_bestRun != null)
            {
                // Get Warnings messages
                string[] warnings = multipleSetupHandler.GetWarnings();

                // Get the Used and Unused Features
                multipleSetupHandler.GetFeaturesStatus(out CAMFeature[] usedFeatures, out CAMFeature[] unusedFeatures);

                // Report Setups
                ReportSetups();

                // Report Warnings
                ReportWarnings(warnings);

                // Report Feature Status
                ReportFeaturesStatus(usedFeatures, unusedFeatures);

                // Report Operations Type
                ReportOperationsType();

                // Report Tools
                ReportTools();

                // Is optimal run found ?
                if (unusedFeatures.Length == 0)
                {
                    m_manager.LogFile.AddMessage("All features have been machined in " + m_bestRun.NumSetups.ToString() + " setup(s)");
                    m_manager.CAMReport.SetStatus(CAMReport.Status.SUCCESS);

                    // Raise a warning if we have a 5-axis machine and more than 2 setups are needed
                    if (m_manager.Machine.Type == Machine.MachineType.AXIS_5 && m_bestRun.NumSetups > 2)
                    {
                        m_manager.LogFile.AddWarning("More than 2 setups were needed with the 5-axis machine");
                        m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
                    }
                }
                else
                {
                    throw new Exception("There is no setups combination that allows to process all the features");
                }
            }
            else
            {
                throw new Exception("An error occurred while finding the optimal combination of setups: " + errorMsg);
            }
        }


        private void ReportSetups()
        {
            m_manager.LogFile.AddMessage("Report Setups ...");

            for (int i = 0; i < m_bestRun.NumSetups; ++i)
            {
                CAMSingleSetup setup = m_bestRun.GetSetup(i);

                if (setup != null)
                {
                    m_manager.CAMReport.AddSetup(setup.Id, setup.Name, setup.OpCode, setup.CsysName);
                }
            }
        }


        private void ReportWarnings(string[] warnings)
        {
            m_manager.LogFile.AddMessage("Report Warnings ...");

            foreach (string warning in warnings)
            {
                m_manager.LogFile.AddWarning(warning);
            }
        }


        private void ReportFeaturesStatus(CAMFeature[] usedFeatures, CAMFeature[] unusedFeatures)
        {
            m_manager.LogFile.AddMessage("Report Features Status ...");

            Func<string, string> Trim = (s) => s.TrimEnd('_','0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

            // Used Features
            foreach (CAMFeature feature in usedFeatures)
            {
                string msg = "Feature " + Trim(feature.Name) + " was successfully machined";
                m_manager.LogFile.AddMessage(msg);
            }

            // Unused Features
            foreach (CAMFeature feature in unusedFeatures)
            {
                string msg = "Feature " + Trim(feature.Name) + " was not machined";
                m_manager.LogFile.AddWarning(msg);
            }

            // Populate Features Type
            m_manager.CAMReport.AddMachinedFeatures(usedFeatures.Select(p => p.Type).ToArray());
            m_manager.CAMReport.AddNotMachinedFeatures(unusedFeatures.Select(p => p.Type).ToArray());
        }


        private void ReportOperationsType()
        {
            m_manager.LogFile.AddMessage("Report Operations Type ...");

            // Populate list of CAMOperations 
            m_manager.CAMReport.AddOperationTypes(m_assemblyPart.CAMSetup.CAMOperationCollection.ToArray().Select(p => p.GetType().Name).ToArray());
        }


        private void ReportTools()
        {
            m_manager.LogFile.AddMessage("Report Tools ...");

            for (int i = 0; i < m_bestRun.NumSetups; ++i)
            {
                CAMSingleSetup setup = m_bestRun.GetSetup(i);

                if (setup != null)
                {
                    Tool[] tools = setup.GetTools();

                    m_manager.CAMReport.AddTools(tools.Select(p => p.Name).ToArray());
                }
            }          
        }


        private void PostProcess()
        {
            m_manager.LogFile.AddMessage("Post Processing ...");

            // Create a directory for G-Code files
            string outputDirectory = m_manager.OutputDirectory + "\\" + "G-Code";
            Directory.CreateDirectory(outputDirectory);

            // Loop through all the Setups of the Best Run and Generate Tool Path and G-code for each.
            // ToolPath is regenerated because sometimes, it may be out of date after feature cleaning.
            for (int i = 0; i < m_bestRun.NumSetups; ++i)
            {
                CAMSingleSetup setup = m_bestRun.GetSetup(i);

                if (setup != null)
                {
                    setup.GenerateGCode(outputDirectory, m_manager.Machine.MachineID);
                }
            }
        }


        private void ComputeManufacturingTime()
        {
            m_manager.LogFile.AddMessage("Computing Manufacturing Time ...");

            // Get the Tool Change time
            NCGroup genericMachine = m_assemblyPart.CAMSetup.CAMGroupCollection.FindObject("GENERIC_MACHINE");
            MachineGroupBuilder builder = m_assemblyPart.CAMSetup.CAMGroupCollection.CreateMachineGroupBuilder(genericMachine);
            double toolChangeTime = builder.ToolChangeTime.Value;
            builder.Destroy();

            // Loop through all the Setups of the Best Run and compute the manufacturing time for each.
            for (int i = 0; i < m_bestRun.NumSetups; ++i)
            {
                CAMSingleSetup setup = m_bestRun.GetSetup(i);

                if (setup != null)
                {
                    // Consider only setups that have operations
                    if (setup.HasOperations())
                    {
                        // Retrieve time for the setup
                        double time = setup.ComputeManufacturingTime(toolChangeTime);

                        // Add the time to the CAM Report
                        m_manager.CAMReport.AddManufacturingTime(setup.Id, time);
                    }
                }
            }
        }


        private void SimulateToolPath()
        {
            m_manager.LogFile.AddMessage("Simulating Tool Path ...");

            // Retrieve the Simulation Mode / Driver Type
            IsvControlPanelBuilder.VisualizationType driverType;
            string simulationModeStr = ConfigurationManager.AppSettings["MISUMI_MACHINE_CODE_BASED_SIMULATION"];
            if (simulationModeStr != null && Int32.TryParse(simulationModeStr, out int simulationMode) && simulationMode == 1)
            {
                driverType = IsvControlPanelBuilder.VisualizationType.ToolPathSimulation;
            }
            else
            {
                driverType = IsvControlPanelBuilder.VisualizationType.ToolPathSimulation;
            }

            // Set the speed
            int speed = 10;

            for (int i = 0; i < m_bestRun.NumSetups; ++i)
            {
                CAMSingleSetup setup = m_bestRun.GetSetup(i);

                if (setup != null)
                {
                    Utilities.NXOpenUtils.SimulateToolPath(m_assemblyPart, setup.Program, driverType, speed, out int numCollisions, out string msg);

                    m_manager.CAMReport.AddCollision(setup.Id, numCollisions);
                    if (numCollisions > 0)
                    {
                        msg = "A collision has been detected in setup \"" + setup.Name + "\": " + msg;
                        m_manager.LogFile.AddWarning(msg);
                        m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
                    }
                }
            }
        }


        private void PerformSuccessCheck()
        {
            m_manager.LogFile.AddMessage("Performing Success Check ...");

            //Add Undo Marker 
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, "Dummy_Operation");

            try
            {
                // Retrieve tha last setup
                CAMSingleSetup lastSetup = m_bestRun.GetSetup(m_bestRun.NumSetups - 1);

                // Create dummy last operation to extract input IPW
                NCGroup noneGroup = m_assemblyPart.CAMSetup.CAMGroupCollection.FindObject("NONE");
                NXOpen.CAM.Operation dummyProbe = m_assemblyPart.CAMSetup
                                                                .CAMOperationCollection
                                                                .Create(
                                                                          lastSetup.Program,
                                                                          noneGroup,
                                                                          noneGroup,
                                                                          lastSetup.Workpiece,
                                                                          "probing",
                                                                          "MILL_PART_PROBING",
                                                                          OperationCollection.UseDefaultName.True,
                                                                          "MILL_PART_PROBING"
                                                                        );

                // Retrieve the Facet Body of the IPW
                FacetedBody ipwBody = dummyProbe.GetInputIpw() as FacetedBody;
                if (ipwBody == null)
                {
                    throw new Exception("Unable to retrieve the last IPW body");
                }

                // Get the CAD body of the last setup 
                Body cadBody = Utilities.NXOpenUtils.GetComponentBodies(lastSetup.Component).FirstOrDefault();
                if (cadBody == null)
                {
                    throw new Exception("Unable to retrieve the CAD Body from component of last setup");
                }

                // Wave link the CAD body in the Assembly Part
                Body waveLinkCADBody = Utilities.NXOpenUtils.CreateWaveLink(cadBody, m_assemblyPart);
                if (waveLinkCADBody == null)
                {
                    throw new Exception("Unable to wave link the CAD Body in the assembly part");
                }

                // Is Weight check enabled ?
                string enableWeightCheckStr = ConfigurationManager.AppSettings["MISUMI_ENABLE_WEIGHT_CHECK"];
                if (enableWeightCheckStr != null && int.TryParse(enableWeightCheckStr, out int enableWeightCheck) && enableWeightCheck == 1)
                {
                    PerformWeightDeviationCheck(cadBody, ipwBody);
                }
                else
                {
                    m_manager.LogFile.AddMessage("Weight Check has been disabled");
                }

                // Is Geometry check enabled ?
                string enableGeometryCheckStr = ConfigurationManager.AppSettings["MISUMI_ENABLE_GEOMETRY_CHECK"];
                if (enableGeometryCheckStr != null && int.TryParse(enableGeometryCheckStr, out int enableGeometryCheck) && enableGeometryCheck == 1)
                {
                    PerformGlobalGeometryDeviationCheck(waveLinkCADBody, ipwBody);

                    foreach (CAMFeature camFeature in m_machinedFeatures)
                    {
                        Face[] featureFaces = camFeature.GetFaces().Select(p => lastSetup.Component.FindOccurrence(p.Prototype as Face))
                                                                   .Where(p => p != null)
                                                                   .Select(p => Utilities.NXOpenUtils.GetLinkedFace(waveLinkCADBody.GetFeatures().FirstOrDefault(), p as Face))
                                                                   .ToArray();

                        PerformFeatureGeometryDeviationCheck(camFeature.Name, featureFaces, ipwBody);
                    }
                }
                else
                {
                    m_manager.LogFile.AddMessage("Geometry Check has been disabled");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Success Check : " + ex.Message);
            }
            finally
            {
                // Undo the operation created
                Utilities.RemoteSession.NXSession.UndoToMark(undoMark, "Dummy_Operation");
            }
        }


        private void PerformWeightDeviationCheck(Body sourceBody, FacetedBody targetBody)
        {
            m_manager.LogFile.AddMessage("Performing Weight Deviation Check ...");

            // Retrieve variable MISUMI_SUCCESS_WEIGHT_RATIO"
            string limitWeightDeviationStr = ConfigurationManager.AppSettings["MISUMI_SUCCESS_WEIGHT_RATIO"];
            if (limitWeightDeviationStr == null || !double.TryParse(limitWeightDeviationStr, out double limitWeightDeviation) || limitWeightDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_SUCCESS_WEIGHT_RATIO");
            }

            // Retrieve variable "MISUMI_PARTIAL_SUCCESS_WEIGHT_RATIO"
            string criticalWeightDeviationStr = ConfigurationManager.AppSettings["MISUMI_PARTIAL_SUCCESS_WEIGHT_RATIO"];
            if (criticalWeightDeviationStr == null || !double.TryParse(criticalWeightDeviationStr, out double criticalWeightDeviation) || criticalWeightDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_PARTIAL_SUCCESS_WEIGHT_RATIO");
            }

            // Create the Volume Deviation class
            VolumeDeviation volumeDeviation = new VolumeDeviation(sourceBody, targetBody);

            // We will compute the volume deviation instead of the weight deviation
            // Indeed, we suppose the CAD and BLANK have the same density
            bool success = volumeDeviation.ComputeDeviation(out double volumeDeviationValue, out Unit unit);
            if (success)
            {
                // Weight Deviation = Volume Deviation
                double weightDeviationValue = volumeDeviationValue;

                // Populate the actual ratio in the CAM Report
                m_manager.CAMReport.AddWeightDeviation(weightDeviationValue, "%");

                if (weightDeviationValue <= limitWeightDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.SUCCESS);
                    m_manager.LogFile.AddMessage("Success Check : Weight ratio within limits");
                }
                else if (weightDeviationValue <= criticalWeightDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
                    m_manager.LogFile.AddWarning("Success Check : Weight ratio above Success limit ! " +
                                                 "Success ratio : " + limitWeightDeviation + " " +
                                                 "Partial Success ratio : " + criticalWeightDeviation + " " +
                                                 "Actual deviation : " + weightDeviationValue);
                }
                else
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.FAILURE);
                    m_manager.LogFile.AddError("Weight ratio above Partial Success limit! " +
                                               "Partial Success ratio : " + criticalWeightDeviation + " " +
                                               "Actual deviation  : " + weightDeviationValue);
                }
            }
            else
            {
                throw new Exception("Error while performing Weight Deviation check");
            }
        }


        private void PerformGlobalGeometryDeviationCheck(Body sourceBody, FacetedBody targetBody)
        {
            m_manager.LogFile.AddMessage("Performing Global Geometry Deviation Check ...");

            // Retrieve variable "MISUMI_SUCCESS_GEOMETRY_TOLERANCE"
            string limitGeometryDeviationStr = ConfigurationManager.AppSettings["MISUMI_SUCCESS_GEOMETRY_TOLERANCE"];
            if (limitGeometryDeviationStr == null || !double.TryParse(limitGeometryDeviationStr, out double limitGeometryDeviation) || limitGeometryDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_SUCCESS_GEOMETRY_TOLERANCE");
            }

            // Retrieve variable "MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE""
            string criticalGeometryDeviationStr = ConfigurationManager.AppSettings["MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE"];
            if (criticalGeometryDeviationStr == null || !double.TryParse(criticalGeometryDeviationStr, out double criticalGeometryDeviation) || criticalGeometryDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE");
            }

            // Retrieve variable "MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_GLOBAL""
            string maxCheckingDistanceStr = ConfigurationManager.AppSettings["MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_GLOBAL"];
            if (maxCheckingDistanceStr == null || !double.TryParse(maxCheckingDistanceStr, out double maxCheckingDistance) || maxCheckingDistance <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_GLOBAL");
            }

            // Retrieve variable "MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_GLOBAL""
            string maxCheckingAngleStr = ConfigurationManager.AppSettings["MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_GLOBAL"];
            if (maxCheckingAngleStr == null || !double.TryParse(maxCheckingAngleStr, out double maxCheckingAngle) || maxCheckingAngle <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_GLOBAL");
            }

            // Create the Geometry Deviation class
            GeometryDeviation geometryDeviation = new GeometryDeviation(sourceBody.GetFaces(), targetBody, maxCheckingDistance, maxCheckingAngle);

            bool success = geometryDeviation.ComputeDeviation(out double geometryDeviationValue, out Unit unit);
            if (success)
            {
                // Convert limit and critical values (if necessary)
                double factor = unit.Symbol == "mm" ? 25.4 : 1.0;
                limitGeometryDeviation *= factor;
                criticalGeometryDeviation *= factor;

                // Populate the max deviation in the CAM Report
                m_manager.CAMReport.AddGeometryDeviation(geometryDeviationValue, unit.Symbol);

                // Compare maxDeviation to critical values
                if (geometryDeviationValue <= limitGeometryDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.SUCCESS);
                    m_manager.LogFile.AddMessage("Success Check : Global geometry deviation within tolerance");
                }
                else if (geometryDeviationValue <= criticalGeometryDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
                    m_manager.LogFile.AddWarning("Success Check : Part is not machined within Success tolerance ! " +
                                                 "Success tolerance : " + limitGeometryDeviation + " " +
                                                 "Partial Success tolerance : " + criticalGeometryDeviation + " " +
                                                 "Actual deviation : " + geometryDeviationValue);
                }
                else
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.FAILURE);
                    m_manager.LogFile.AddError("Material left beyond Partial Success deviation tolerance ! " +
                                               "Partial Success tolerance : " + criticalGeometryDeviation + " " +
                                               "Actual deviation : " + geometryDeviationValue);
                }
            }
            else
            {
                throw new Exception("Error while performing Global Geometry Deviation check");
            }
        }

        private void PerformFeatureGeometryDeviationCheck(string featureName, Face[] featureFaces, FacetedBody targetBody)
        {
            m_manager.LogFile.AddMessage("Performing Feature Geometry Deviation Check ... " +
                                         "Feature Name: " + featureName);

            // Retrieve variable "MISUMI_SUCCESS_GEOMETRY_TOLERANCE"
            string limitGeometryDeviationStr = ConfigurationManager.AppSettings["MISUMI_SUCCESS_GEOMETRY_TOLERANCE"];
            if (limitGeometryDeviationStr == null || !double.TryParse(limitGeometryDeviationStr, out double limitGeometryDeviation) || limitGeometryDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_SUCCESS_GEOMETRY_TOLERANCE");
            }

            // Retrieve variable "MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE""
            string criticalGeometryDeviationStr = ConfigurationManager.AppSettings["MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE"];
            if (criticalGeometryDeviationStr == null || !double.TryParse(criticalGeometryDeviationStr, out double criticalGeometryDeviation) || criticalGeometryDeviation <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_PARTIAL_SUCCESS_GEOMETRY_TOLERANCE");
            }

            // Retrieve variable "MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_PER_FEATURE""
            string maxCheckingDistanceStr = ConfigurationManager.AppSettings["MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_PER_FEATURE"];
            if (maxCheckingDistanceStr == null || !double.TryParse(maxCheckingDistanceStr, out double maxCheckingDistance) || maxCheckingDistance <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_DISTANCE_PER_FEATURE");
            }

            // Retrieve variable "MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_PER_FEATURE""
            string maxCheckingAngleStr = ConfigurationManager.AppSettings["MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_PER_FEATURE"];
            if (maxCheckingAngleStr == null || !double.TryParse(maxCheckingAngleStr, out double maxCheckingAngle) || maxCheckingAngle <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_GEOMETRICAL_DEVIATION_MAX_CHECKING_ANGLE_PER_FEATURE");
            }

            // Retrieve the Global Deviation value.
            // The maximum checking distance would be the lowest value between the global deviation and the one specified in the configuration file
            double globalDeviation = m_manager.CAMReport.GetGeometryDeviation(out string globalDeviationUnit);
            double globalDeviationFactor = globalDeviationUnit == "mm" ? 1.0 / 25.4 : 1.0;
            double smallestMaxCheckingDistance = Math.Min(globalDeviation * globalDeviationFactor, maxCheckingDistance);

            // Create the Geometry Deviation class
            GeometryDeviation geometryDeviation = new GeometryDeviation(featureFaces, targetBody, smallestMaxCheckingDistance, maxCheckingAngle);

            bool success = geometryDeviation.ComputeDeviation(out double geometryDeviationValue, out Unit unit);
            if (success)
            {
                // Convert limit and critical values (if necessary)
                double factor = unit.Symbol == "mm" ? 25.4 : 1.0;
                limitGeometryDeviation *= factor;
                criticalGeometryDeviation *= factor;

                // Populate the max deviation in the CAM Report
                m_manager.CAMReport.AddFeatureGeometryDeviation(featureName, geometryDeviationValue, unit.Symbol);

                // Compare maxDeviation to critical values
                if (geometryDeviationValue <= limitGeometryDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.SUCCESS);
                    m_manager.LogFile.AddMessage("Success Check : Feature geometry deviation within tolerance");
                }
                else if (geometryDeviationValue <= criticalGeometryDeviation)
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.PARTIALSUCCESS);
                    m_manager.LogFile.AddWarning("Success Check : Feature is not machined within Success tolerance ! " +
                                                 "Success tolerance : " + limitGeometryDeviation + " " +
                                                 "Partial Success tolerance : " + criticalGeometryDeviation + " " +
                                                 "Actual deviation : " + geometryDeviationValue);
                }
                else
                {
                    m_manager.CAMReport.SetStatus(CAMReport.Status.FAILURE);
                    m_manager.LogFile.AddError("Material left beyond Partial Success deviation tolerance ! " +
                                               "Partial Success tolerance : " + criticalGeometryDeviation + " " +
                                               "Actual deviation : " + geometryDeviationValue);
                }
            }
            else
            {
                throw new Exception("Error while performing Feature Geometry Deviation check");
            }
        }


        private void FindAndCreateProbePoints()
        {
            m_manager.LogFile.AddMessage("Finding and Creating Probing Points ...");

            CAMProbePointCreator probePointCreator = new CAMProbePointCreator(m_blankComponent, m_cadComponent);

            if (!probePointCreator.FindAndCreateProbePoints(out Point3d[] probePoints, out string msg))
            {
                throw new Exception("Unable to retrieve and create the probing points: " + msg);
            }
        }


        private void SaveAssembly()
        {
            m_manager.LogFile.AddMessage("Saving Assembly ...");

            m_assemblyPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        [DllImport("user32")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);


        private Part m_assemblyPart;

        private Component m_blankComponent;
        private Component m_cadComponent;

        private OrientGeometry m_mainMcs;
        private FeatureGeometry m_mainWorkpiece;
        private FeatureGeometry m_notchProbingWorkpiece;

        private CAMFeature[] m_blankFeatures;
        private CAMFeature[] m_cadFeatures;
        private CAMFeature[] m_machinedFeatures;

        private CAMRun m_bestRun;

        private CAMAutomationManager m_manager;

        private bool m_enableProbing;
    }
}
