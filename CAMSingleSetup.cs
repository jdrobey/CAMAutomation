using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;

using NXOpen;
using NXOpen.Assemblies;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class CAMSingleSetup
    {
        public CAMSingleSetup(int id, OrientGeometry mcs, FeatureGeometry workpiece, FeatureGeometry notchProbingWorkpiece, Component component)
        {
            Id = id;
            Mcs = mcs;
            Workpiece = workpiece;
            NotchProbingWorkpiece = notchProbingWorkpiece;
            Component = component;

            // Create the setup folder in the Program Navigator
            // The folder will be created under the main PROGRAM
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;
            NCGroup parent = part.CAMSetup.CAMGroupCollection.ToArray().Where(p => p.Name == "PROGRAM").First();
            if (parent != null)
            {
                Program = part.CAMSetup.CAMGroupCollection.CreateProgram(parent, "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, Mcs.Name);
            }
        }


        ~CAMSingleSetup()
        {
            // empty
        }


        public string Name
        {
            get
            {
                return Mcs.Name;
            }
        }


        public string OpCode
        {
            get
            {
                return Workpiece.Name;
            }
        }


        public string CsysName
        {
            get
            {
                if (Utilities.NXOpenUtils.GetAttribute(Mcs, "MISUMI", "CSYS", out string csysName))
                {
                    return csysName;
                }
                else
                {
                    return String.Empty;
                }
            }
        }


        public string McsName
        {
            get
            {
                if (Utilities.NXOpenUtils.GetAttribute(Mcs, "MISUMI", "MCS", out string csysName))
                {
                    return csysName;
                }
                else
                {
                    return String.Empty;
                }
            }
        }


        public bool HasOperations()
        {
            return GetOperations().Length > 0 ? true : false;
        }


        public NXOpen.CAM.Operation[] GetOperations()
        {
            return Utilities.NXOpenUtils.GetOperations(Mcs).ToArray();
        }


        public Tool[] GetTools()
        {
            List<Tool> tools = new List<Tool>();

            NXOpen.CAM.Operation[] operations = GetOperations();
            foreach (NXOpen.CAM.Operation operation in operations)
            {
                Tool tool = Utilities.NXOpenUtils.GetTool(operation);

                if (tool != null)
                {
                    tools.Add(tool);
                }
            }

            return tools.ToArray();
        }


        public bool DoesIncludeNotchProbing()
        {
            return NotchProbingWorkpiece != null;
        }


        public void GenerateOperations(CAMFeature[] features)
        {
            // Create Program Group Folders
            CreateProgramGroupFolders();

            // Create Feature Process
            CreateFeatureProcess(features);

            // Update Operations Inheritance Status
            UpdateOperationsInheritanceStatus();

            // Reorganize operations
            ReorganizeOperations();

            // Clean Program Group Folders
            CleanProgramGroupFolders();
        }


        private void CreateProgramGroupFolders()
        {
            // Create MILL_ROUGH, MILL_SEMI_FINISH and MILL_FINISH folders
            // So NX will put the generated operations inside those folders
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;
            NCGroup parent = part.CAMSetup.CAMGroupCollection.ToArray().Where(p => p.Name == "PROGRAM").First();
            if (parent != null)
            {
                m_rough = part.CAMSetup.CAMGroupCollection.CreateProgram(parent, "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "MILL_ROUGH");
                m_semifinish = part.CAMSetup.CAMGroupCollection.CreateProgram(parent, "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "MILL_SEMI_FINISH");
                m_finish = part.CAMSetup.CAMGroupCollection.CreateProgram(parent, "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "MILL_FINISH");
            }
        }


        private void CreateFeatureProcess(CAMFeature[] features)
        {
            // Create the builder
            FeatureProcessBuilder builder = Utilities.RemoteSession.NXSession.Parts.Work.CAMSetup.CreateFeatureProcessBuilder();

            try
            {
                // Options
                builder.Type = FeatureProcessBuilder.FeatureProcessType.RuleBased;
                builder.SetGeometryLocation(Workpiece.Name);
                builder.FeatureGrouping = FeatureProcessBuilder.FeatureGroupingType.PerFeature;

                // Get available Rules Libraries
                string[] ruleLibraries = null;
                builder.GetRuleLibraries(out ruleLibraries);

                // Override the Rule Libraries with the content of the Rule Library text file
                string customDir = Environment.GetEnvironmentVariable("UGII_CAM_CUSTOM_DIR");
                if (customDir != null)
                {
                    string ruleLibraryFile = customDir + "\\machining_knowledge\\RuleLibrary.rl";

                    if (File.Exists(ruleLibraryFile))
                    {
                        // Parse the file
                        ruleLibraries = File.ReadAllLines(ruleLibraryFile);

                        // Remove comments (i.e., line starting with #)
                        ruleLibraries = ruleLibraries.Where(str => !str.StartsWith("#")).ToArray();
                    }
                }

                // Set the Rule Libraries
                builder.SetRuleLibraries(ruleLibraries);

                // Process features
                FeatureProcessBuilderStatus status = null;
                builder.CreateFeatureProcesses(features, out status);

                // Destroy the builder
                builder.Destroy();
            }
            catch (Exception)
            {
                // Destroy the builder
                builder.Destroy();

                throw;
            }
        }


        private void UpdateOperationsInheritanceStatus()
        {
            NXOpen.CAM.Operation[] operations = GetOperations();
            foreach (NXOpen.CAM.Operation operation in operations)
            {
                // Create the operation builder handler
                Utilities.CAMOperationBuilderHandler handler = new Utilities.CAMOperationBuilderHandler(operation);

                // Update the operation Inheritance Status
                handler.UpdateInheritanceStatus();

                // Release Create the operation builder handler
                handler.Release();
            }
        }


        private void ReorganizeOperations()
        {
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;

            // Retrieve the main PROGRAM group
            NCGroup mainParent = part.CAMSetup.CAMGroupCollection.ToArray().Where(p => p.Name == "PROGRAM").First();

            if (mainParent != null)
            {
                // Retrieve the operations
                NXOpen.CAM.Operation[] operations = GetOperations();

                // Sort the operations in the chronological order
                Array.Sort(operations, new CAMOperationOrderComparer());

                // Divide operations/objects in 2 categories: notch probing and non notch probing
                List<CAMObject> nonNotchProbingObjects = new List<CAMObject>();
                List<CAMObject> notchProbingObjects = new List<CAMObject>();
                foreach (NXOpen.CAM.Operation operation in operations)
                {
                    // Select the relevant list depending if it is a notch probing operation or not
                    bool isNotchProbingOperation = operation.GetParent(CAMSetup.View.Geometry) == NotchProbingWorkpiece;
                    List<CAMObject> currentList = isNotchProbingOperation ? notchProbingObjects : nonNotchProbingObjects;

                    // For each operation, find the parent that is directly under program.
                    NCGroup parent = operation.GetParent(CAMSetup.View.ProgramOrder);
                    do
                    {
                        if (parent == mainParent)
                        {
                            currentList.Add(operation);
                            break;
                        }
                        else if (parent.GetParent() == mainParent)
                        {
                            currentList.Add(parent);
                            break;
                        }
                        parent = parent.GetParent();
                    }
                    while (parent != null);
                }

                // Add notch probing objects after non notch probing objects and remove duplicate
                List<CAMObject> objectsToMove = nonNotchProbingObjects.Concat(notchProbingObjects).Distinct().ToList();

                // Update the name of each parent to move.
                // This will avoid NX creating new operations in those folders.
                objectsToMove.ForEach(p => p.SetName(Id.ToString() + "_" + p.Name));

                // Move all the parent folders inside Program
                part.CAMSetup.MoveObjects(CAMSetup.View.ProgramOrder, objectsToMove.ToArray(), Program, CAMSetup.Paste.Inside);
            }
        }


        private void CleanProgramGroupFolders()
        {
            // MILL_ROUGH folder
            if (m_rough.GetMembers().Length == 0)
            {
                Utilities.NXOpenUtils.DeleteNXObject(m_rough);
                m_rough = null;
            }

            // MILL_SEMI_FINISH folder
            if (m_semifinish.GetMembers().Length == 0)
            {
                Utilities.NXOpenUtils.DeleteNXObject(m_semifinish);
                m_semifinish = null;
            }

            // MILL_FINISH folder
            if (m_finish.GetMembers().Length == 0)
            {
                Utilities.NXOpenUtils.DeleteNXObject(m_finish);
                m_finish = null;
            }
        }


        public void GenerateToolPath()
        {
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;

            // Make sure to update the Blank IPW of the Workpiece before generating the ToolPath
            CAMWorkpieceHandler workpieceHandler = new CAMWorkpieceHandler(Workpiece);
            workpieceHandler.UpdateBlankIPW();

            // Generate the ToolPath
            part.CAMSetup.GenerateToolPath(new CAMObject[] { Program });

            // Toolpath of notch probing operation is not generated at this stage
            // IPW of notch probing workpiece should be updated first cause it depends on the main workpiece
            // Then, toolpath of notch probing operation is generated
            if (NotchProbingWorkpiece != null)
            {
                CAMWorkpieceHandler notchProbingWorkpieceHandler = new CAMWorkpieceHandler(NotchProbingWorkpiece);
                notchProbingWorkpieceHandler.UpdateBlankIPW();

                part.CAMSetup.GenerateToolPath(new CAMObject[] { NotchProbingWorkpiece });
            }
        }


        public void GenerateGCode(string outputDirectory, string postprocessorName)
        {
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;

            // Define the outputFile full path
            m_GCodeFileFullName = outputDirectory + "\\" + "Rank" + Id + "_" + OpCode + ".ptp";

            // Generate G-Code
            part.CAMSetup.OutputBallCenter = false;
            part.CAMSetup.PostprocessWithSetting(new CAMObject[] { Program }, postprocessorName, m_GCodeFileFullName,
                                                 CAMSetup.OutputUnits.PostDefined,
                                                 CAMSetup.PostprocessSettingsOutputWarning.PostDefined,
                                                 CAMSetup.PostprocessSettingsReviewTool.PostDefined);
        }


        public double ComputeManufacturingTime(double toolChangeTime)
        {
            // Compute the total time for the operations

            double totalTime;

            if (!ComputeManufacturingTimeFromGCode(out totalTime) || totalTime <= 0.0)
            {
                int nbToolChange = 0;
                Tool currentTool = null;
                double operationTime = 0.0;

                // Retrieve all the operation under the program (operations are already sorted)
                List<NXOpen.CAM.Operation> operations = Utilities.NXOpenUtils.GetOperations(Program);

                foreach (NXOpen.CAM.Operation operation in operations)
                {
                    // Add the operation time to the global time
                    operationTime += 60.0 * operation.GetToolpathTime();     // Time in seconds

                    // Get the operation tool and check if there is a tool change
                    Tool tool = Utilities.NXOpenUtils.GetTool(operation);
                    if (tool != currentTool)
                    {
                        // We have a tool change here
                        ++nbToolChange;
                        currentTool = tool;
                    }
                }

                // Compute the Total Tool time
                totalTime = nbToolChange * toolChangeTime + operationTime;
            }

            // Return the Total Manufacturing time
            return totalTime;
        }


        private bool ComputeManufacturingTimeFromGCode(out double time)
        {
            //Retrieve total time from generated GCode file
            bool found = false;
            const string searchStr = "(TOTAL TIME:";
            time = 0.0;
            if ((!String.IsNullOrEmpty(m_GCodeFileFullName) && File.Exists(m_GCodeFileFullName)))
            {
                string[] resultContent = File.ReadAllLines(m_GCodeFileFullName);
                resultContent = resultContent
                               .Where(s => s.Contains(searchStr))
                               .Select(s => s.Split(':').Last().Replace(")", String.Empty).Trim())
                               .Where(s => s != String.Empty && s.Replace(".", String.Empty).All(char.IsDigit)).ToArray();

                if (resultContent.Length == 1)
                {
                    found = true;
                    time = Convert.ToDouble(resultContent[0]) * 60.0;
                }
            }

            return found;
        }


        public int Id { get; }
        public NCGroup Program { get; }
        public OrientGeometry Mcs { get; }
        public FeatureGeometry Workpiece { get; }
        public FeatureGeometry NotchProbingWorkpiece { get; }
        public Component Component { get; }

        private NCGroup m_rough;
        private NCGroup m_semifinish;
        private NCGroup m_finish;

        private string m_GCodeFileFullName;
    }
}
