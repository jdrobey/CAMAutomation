using System;
using System.IO;
using static CAMAutomation.JsonFeatures;
using NXOpen;
using NXOpen.Assemblies;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace CAMAutomation
{
    public class CAMAutomationManager
    {
        public enum AutomationAlgorithm{ STOCK_HOLE_PATTERN = 0, STOCK_NO_HOLE_PATTERN = 1, PFB_HOLE_PATTERN = 2, PFB_NO_HOLE_PATTERN = 3}

        private static CAMAutomationManager m_instance;


        public static CAMAutomationManager GetInstance()
        {
            if (m_instance == null)
            {
                m_instance = new CAMAutomationManager();
            }

            return m_instance;
        }

 
        public static void Release()
        {
            if (m_instance != null)
            {
                m_instance = null;
            }
        }


        private CAMAutomationManager()
        {
            m_success = true;

            LogFile = new Utilities.LogFile("LogFile");

            CAMReport = new CAMReport();

            AllowNearNetBlank = false;

            IsBlankFromStock = false;
            HasCADValidHolePattern = false;
            IsNearNetBlank = false;

            SetupSelection = new Dictionary<int, int>();
            Json = false;
        }

        ~CAMAutomationManager()
        {
            // empty
        }


        public bool Execute(string inputFile, string outputDirectory)
        {
            InputFile = inputFile;
            OutputDirectory = outputDirectory;

            // Empty the output Directory
            EmptyOutputDirectory();

            // Parse Input File
            ParseInputFile();

            // Create Objective Function Container
            CreateObjectiveFunctionContainer();

            // Create CAD Assembly
            CreateCADAssembly();

            // Import NX Files
            ImportNXFile();

            // Write Json PMI
            WriteJsonPMI();

            // Optimize CAD Geometry
            OptimizeCADGeometry();

            // Mirror CAD Geometry
            MirrorCADGeometry();

            // Create CAD Coordinate Systems
            CreateCADCoordinateSystems();

            // Create Blank from Stock
            CreateBlankFromStock();

            // Create Blank Coordinate Systems
            CreateBlankCoordinateSystems();

            // Create Blank Notch
            CreateBlankNotch();

            // Select Machine
            SelectMachine();

            // Run CAM Operations
            RunCAMOperations();

            // CMM Model Preparation
            RunCMMModelPreparation();

            // Write Report
            WriteReport();

            // Clean Assembly
            CleanAssembly();

            // Save the assembly
            SaveAssembly();

            // Close all Parts
            CloseAllParts();

            return m_success;
        }


        private void EmptyOutputDirectory()
        {
            LogFile.AddMessage("Cleaning output directory ...");

            // Delete all folders
            foreach (string folder in Directory.GetDirectories(OutputDirectory, "*.*", SearchOption.AllDirectories))
            {
                Directory.Delete(folder, true);
            }

            // Delete all files
            foreach (string file in Directory.GetFiles(OutputDirectory, "*.*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }


        private void ParseInputFile()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Parsing input file ...");

            InputFileParser parser = new InputFileParser();
            m_success = parser.Parse();
        }


        private void CreateCADAssembly()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Creating CAD Assembly ...");

            CADAssemblyCreator assemblyCreator = new CADAssemblyCreator();
            AssemblyPart = assemblyCreator.CreateAssembly();

            m_success = AssemblyPart != null ? true : false;
        }


        private void ImportNXFile()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Importing NX Files ...");

            // Import the Blank File (if specified)
            if (!String.IsNullOrEmpty(BlankPath))
            {
                LogFile.AddMessage($"Importing Blank File: {BlankPath}");
                Importer blankFileImporter = ImporterFactory.CreateImporter(BlankPath);
                BlankPart = blankFileImporter.Import();
            }
            else
            {
                IsBlankFromStock = true;
            }

            // Import the CAD File
            LogFile.AddMessage($"Importing CAD File: {CADPath}");
            LogFile.AddMessage($"CAD File Format: {CADFileFormat}");
            Importer cadFileImporter = ImporterFactory.CreateImporter(CADPath, CADFileFormat);
            CADPart = cadFileImporter.Import();

            m_success = ((IsBlankFromStock || BlankPart != null) && CADPart != null) ? true : false;
        }
        private void WriteJsonPMI()
        {
            if (!Json)
                return;

            LogFile.AddMessage("Writing Json PMI to CAD ...");

            // Parse the json files and get list of features
            try
            {
                JsonFeatures jsonfeatures = new JsonFeatures();
                JsonFeaturesList = jsonfeatures.GetFeatures(EnvsPath);
            }
            catch
            {
                LogFile.AddMessage("Error in Json Features");
            }
            
            
            // Decode each feature and apply PMI to CAD part
            foreach (JsonDecoder.Feature f in JsonFeaturesList)
            {
                try { f.Decode(); }
                catch { LogFile.AddMessage("Error decoding"); }
            }
        }




        private void OptimizeCADGeometry()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Optimizing CAD Geometry ...");

            OptimizeGeometry optimizeGeometry = new OptimizeGeometry(CADPart);
            m_success = optimizeGeometry.Optimize();
        }


        private void MirrorCADGeometry()
        {
            if (!m_success)
                return;

            if (AsShown == "OPP")
            {
                LogFile.AddMessage("Mirroring CAD Geometry ...");

                MirrorGeometry mirrorGeometry = new MirrorGeometry(CADPart);
                m_success = mirrorGeometry.Mirror(MirrorGeometry.MirrorPlane.XZ);
            }
        }


        private void CreateObjectiveFunctionContainer()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Creating Objective Function Container ...");

            ObjectiveFunctions = new ObjectiveFunctionContainer(OutputDirectory);
            m_success = ObjectiveFunctions.IsInitialized;
        }


        private void CreateCADCoordinateSystems()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Creating CAD Coordinate Systems ...");

            CADCoordinateSystemsCreator coordinateSystemsCreator = new CADCoordinateSystemsCreator(CADPart);
            m_success = coordinateSystemsCreator.Execute();
        }


        private void CreateBlankFromStock()
        {
            if (!m_success)
                return;

            if (IsBlankFromStock)
            {
                LogFile.AddMessage("Creating Blank From Stock ...");

                BlankPreprocessor blankPreprocessor = new BlankPreprocessor(CADPart);
                BlankPart = blankPreprocessor.CreateBlank();

                m_success = BlankPart != null ? true : false;
            }
        }


        private void CreateBlankCoordinateSystems()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Creating BLANK Coordinate Systems ...");
            BlankCoordinateSystemsCreator coordinateSystemsCreator = new BlankCoordinateSystemsCreator(BlankPart);
            m_success = coordinateSystemsCreator.Execute();
        }


        private void CreateBlankNotch()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Creating Blank Notch ...");

            BlankProbingNotchCreator blankProbingNotchCreator = new BlankProbingNotchCreator(BlankPart);
            m_success = blankProbingNotchCreator.Execute();
        }


        private void SelectMachine()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Select Machine ...");

            MachineSelector machineSelector = new MachineSelector(BlankPart);
            Machine = machineSelector.SelectMachine();

            m_success = Machine != null ? true : false;
        }


        private void RunCAMOperations()
        {
            if (!m_success)
                return;

            LogFile.AddMessage("Run CAM Operations ...");

            CAMOperation camOperation = new CAMOperation(AssemblyPart);
            m_success = camOperation.Run();
        }


        private void RunCMMModelPreparation()
        {
            if (!m_success)
                return;

            if (HasCADValidHolePattern)
            {
                LogFile.AddMessage("Run CMM Model Preparation ...");

                CMMModelPreparation cmmModelPreparation = new CMMModelPreparation(CADPart);
                m_success = cmmModelPreparation.PrepareCMMModel();
            }
        }


        private void WriteReport()
        {
            if (AssemblyPart != null)
            {
                LogFile.AddMessage("Writing Report  ...");

                ReportWriter report = new ReportWriter();
                m_success = report.Write();
            }
        }


        private void CleanAssembly()
        {
            if (AssemblyPart != null)
            {
                LogFile.AddMessage("Cleaning Assembly ...");

                // Make Assembly Part the displayed part
                Utilities.RemoteSession.NXSession.Parts.SetDisplay(AssemblyPart, false, true, out PartLoadStatus status);

                // Blank all prototype csys
                foreach (CoordinateSystem csys in AssemblyPart.CoordinateSystems.ToArray())
                {
                    csys.Blank();
                }

                // Blank all occurrence csys
                foreach (Component component in AssemblyPart.ComponentAssembly.RootComponent.GetChildren())
                {
                    bool isComponentBlank = component.IsBlanked;

                    // Make sure to unblank the component
                    // If not, the csys will be displayed again if component is redisplayed by the user (NX BUG)
                    component.Unblank();

                    // Retrieve and Blank all the component csys
                    CoordinateSystem[] componentCsys = Utilities.NXOpenUtils.GetComponentCoordinateSystems(component);
                    foreach (CoordinateSystem csys in componentCsys)
                    {
                        csys.Blank();
                    }

                    // Blank the component again if it was originally blanked
                    if (isComponentBlank)
                    {
                        component.Blank();
                    }
                }
            }
        }


        private void SaveAssembly()
        {
            if (AssemblyPart != null)
            {
                LogFile.AddMessage("Saving Part ...");
                AssemblyPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
            }
        }


        private void CloseAllParts()
        {
            LogFile.AddMessage("Closing All Parts ...");

            PartCloseResponses response = null;
            Utilities.RemoteSession.NXSession.Parts.CloseAll(BasePart.CloseModified.CloseModified, response);
        }


        public AutomationAlgorithm GetAutomationAlgorithm()
        {
            if (IsBlankFromStock)
            {
                if (HasCADValidHolePattern)
                {
                    return AutomationAlgorithm.STOCK_HOLE_PATTERN;
                }
                else
                {
                    return AutomationAlgorithm.STOCK_NO_HOLE_PATTERN;
                }
            }
            else
            {
                if (HasCADValidHolePattern)
                {
                    return AutomationAlgorithm.PFB_HOLE_PATTERN;
                }
                else
                {
                    return AutomationAlgorithm.PFB_NO_HOLE_PATTERN;
                }
            }
        }


        private bool m_success;

        public Utilities.LogFile LogFile { get; }

        public string InputFile { get; set; }
        public string OutputDirectory { get; set; }

        public string RequestID { get; set; }
        public string QuoteID { get; set; }

        public string FixturePath { get; set; }
        public string FixtureType { get; set; }
        public string BlankPath { get; set; }
        public string CADPath { get; set; }
        public string EnvsPath { get; set; }
        public string CADFileFormat { get; set; }

        public string AsShown { get; set; }
        public string Material { get; set; }

        public bool AllowNearNetBlank { get; set; }

        public Machine Machine { get; set; }

        public Part BlankPart { get; set; }
        public Part CADPart { get; set; }
        public List<JsonDecoder.Feature> JsonFeaturesList { get; set; }

        public Part AssemblyPart { get; set; }

        public Utilities.CAMFixtureHandler CAMFixtureHandler { get; set; }
        public CAMClampingConfigurator CAMClampingConfigurator { get; set; }

        public CAMReport CAMReport { get; }

        public bool IsBlankFromStock { get; set; }
        public bool HasCADValidHolePattern { get; set; }
        public bool IsNearNetBlank { get; set; }

        public ObjectiveFunctionContainer ObjectiveFunctions { get; set; }
        public Dictionary<int, int> SetupSelection { get; set; }

        public bool Json { get; set; }
    }
}
