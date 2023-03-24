using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;


namespace CAMAutomation
{
    public class ReportWriter
    {
        public ReportWriter()
        {
            m_manager = CAMAutomationManager.GetInstance();
            m_filePath = System.IO.Path.Combine(m_manager.OutputDirectory, "NXCAM_report.xml");
        }

        ~ReportWriter()
        {
            // empty
        }


        public bool Write()
        {
            try
            {
                m_XmlDocument = new XmlDocument();

                // Create the root
                XmlElement root = m_XmlDocument.CreateElement("Misumi");
                m_XmlDocument.AppendChild(root);

                // Status
                root.AppendChild(StatusInfo());

                // Request ID
                root.AppendChild(RequestIDInfo());

                // Hole Pattern Info
                root.AppendChild(HolePatternInfo());

                // Add Raw Material Type Info
                root.AppendChild(RawMaterialTypeInfo());

                // Add Blank Stock info (if defined)
                if (m_manager.CAMReport.HasBestCandidateStock())
                {
                    root.AppendChild(BlankStockInfo());
                }

                // Add Selected Machine info (if found)
                if (m_manager.CAMReport.HasSelectedMachine())
                {
                    root.AppendChild(MachineInfo());
                }

                // Add Manufacturing Time info
                root.AppendChild(ManufacturingTimeInfo());

                // Add CAM operations info
                root.AppendChild(OperationInfo());

                // Add features to be machined info
                root.AppendChild(FeatureInfo("FeaturesToBeMachined", m_manager.CAMReport.GetTotalFeaturesToMachine()));

                // Add machined features info
                root.AppendChild(FeatureInfo("MachinedFeatures", m_manager.CAMReport.GetMachinedFeatures()));

                // Add not machined features info
                root.AppendChild(FeatureInfo("NotMachinedFeatures", m_manager.CAMReport.GetNotMachinedFeatures()));

                // Add tools Info
                root.AppendChild(ToolsInfo());

                // Add collisions info
                root.AppendChild(CollisionsInfo());

                // Add Deviation Between CAD and Machined part
                root.AppendChild(DeviationInfo());

                // Add Deviation Between CAD features and Machined features
                root.AppendChild(FeatureDeviationInfo());

                // Add Opcode Info
                root.AppendChild(OpCodesInfo());

                // Save the file
                m_XmlDocument.Save(m_filePath);
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                return false;
            }

            return true;
        }


        private XmlElement StatusInfo()
        {
            XmlElement statusNode = m_XmlDocument.CreateElement("Status");
            statusNode.InnerText = m_manager.CAMReport.GetStatus().ToString();

            return statusNode;
        }


        private XmlElement RequestIDInfo()
        {
            XmlElement requestIDNode = m_XmlDocument.CreateElement("RequestID");
            requestIDNode.InnerText = m_manager.RequestID;

            return requestIDNode;
        }


        private XmlElement HolePatternInfo()
        {
            XmlElement holePatternNode = m_XmlDocument.CreateElement("HolePatternDetected");
            holePatternNode.InnerText = m_manager.HasCADValidHolePattern ? "YES" : "NO";

            return holePatternNode;
        }


        private XmlElement RawMaterialTypeInfo()
        {
            XmlElement rawMaterialTypeNode = m_XmlDocument.CreateElement("RawMaterialType");
            rawMaterialTypeNode.InnerText = m_manager.IsBlankFromStock ? "Stock" : "Blank";

            return rawMaterialTypeNode;
        }


        private XmlElement BlankStockInfo()
        {
            // Retrieve the best candidate stock
            Stock bestCandidate = m_manager.CAMReport.GetBestCandidateStock(out string stockUnit);
            double factor = stockUnit == "mm" ? 1.0 / 25.4 : 1.0;

            // Create <Stock> main node
            XmlElement stockNode = m_XmlDocument.CreateElement("Stock");

            // ID
            XmlElement idNode = m_XmlDocument.CreateElement("ID");
            idNode.InnerText = bestCandidate.Id;
            stockNode.AppendChild(idNode);

            // Type
            XmlElement typeNode = m_XmlDocument.CreateElement("Type");
            typeNode.InnerText = bestCandidate.Type.ToString();
            stockNode.AppendChild(typeNode);


            if (bestCandidate is StockBlock)
            {
                // Nothing else to add
            }
            else if (bestCandidate is StockBar)
            {
                StockBar bar = bestCandidate as StockBar;

                // CutLength (in)
                XmlElement cutLengthNode = m_XmlDocument.CreateElement("CutLength");
                cutLengthNode.SetAttribute("unit", "in");
                cutLengthNode.SetAttribute("value", (factor * bar.GetCutLength(true)).ToString("0.000"));
                cutLengthNode.SetAttribute("minstkrm", (factor * bar.GetCutLengthStkRm()).ToString("0.000"));
                stockNode.AppendChild(cutLengthNode);
            }
            else if (bestCandidate is StockPlate)
            {
                StockPlate plate = bestCandidate as StockPlate;

                // Thickness (in)
                XmlElement thicknessNode = m_XmlDocument.CreateElement("Thickness");
                thicknessNode.SetAttribute("unit", "in");
                thicknessNode.SetAttribute("value", (factor * plate.GetThickness(true)).ToString("0.000"));
                thicknessNode.SetAttribute("minstkrm", (factor * plate.GetThicknessMinStkRm()).ToString("0.000"));
                stockNode.AppendChild(thicknessNode);
            }

            return stockNode;
        }


        private XmlElement MachineInfo()
        {
            Machine selectedMachine = m_manager.CAMReport.GetSelectedMachine();

            XmlElement selectedMachineNode = m_XmlDocument.CreateElement("Machine");
            selectedMachineNode.InnerText = selectedMachine.MachineID;

            return selectedMachineNode;
        }


        private XmlElement ManufacturingTimeInfo()
        {         
            // Create the Manufacturing Time node
            XmlElement manufacturingTimeNode = m_XmlDocument.CreateElement("ManufacturingTime");

            foreach (CAMReport.SetupInfo setup in m_manager.CAMReport.GetSetups())
            {
                // Define the Setup Neutral Name
                string setupNeutralName = "Setup_" + setup.Id.ToString();

                // Create the node for the setup 
                XmlElement setupNode = m_XmlDocument.CreateElement(setupNeutralName);

                // Add the opcode of the setup as an attribute
                setupNode.SetAttribute("name", setup.OpCode);

                // Add the time as the node value
                setupNode.InnerText = setup.ManufacturingTime.ToString("0.00");

                // Add the setup node as a child to the ManufacturingTime node
                manufacturingTimeNode.AppendChild(setupNode);
            }

            return manufacturingTimeNode;
        }


        private XmlElement OperationInfo()
        {
            string[] camOperations = m_manager.CAMReport.GetOperationTypes();

            XmlElement camOperationsNode = m_XmlDocument.CreateElement("ManufacturingOperations");

            // Add total number of CAM Operations
            camOperationsNode.SetAttribute("quantity", camOperations.Length.ToString());

            // Add number of Operations by operation type
            SortedDictionary<string, int> operationTypes =
               new SortedDictionary<string, int>(camOperations.GroupBy(p => p).ToDictionary(q => q.Key, q => q.Count()));

            foreach (KeyValuePair<string, int> pair in operationTypes)
            {
                XmlElement operationTypeNode = m_XmlDocument.CreateElement("OperationType");
                operationTypeNode.SetAttribute("name", pair.Key);
                operationTypeNode.SetAttribute("quantity", pair.Value.ToString());

                camOperationsNode.AppendChild(operationTypeNode);
            }

            return camOperationsNode;
        }


        private XmlElement FeatureInfo(string elementName, string[] features)
        {
            // Number of Detected Features Machined
            XmlElement numberOfMachinedFeatNode = m_XmlDocument.CreateElement(elementName);
            numberOfMachinedFeatNode.SetAttribute("quantity", features.Count().ToString());

            // Create the Feature Map
            CAMFeatureTypeMap camFeatureTypeMap = new CAMFeatureTypeMap();

            // Try to build the map
            string error;
            if (camFeatureTypeMap.BuildMap(out error))
            {
                SortedDictionary<string, int> featureTypes =
                    new SortedDictionary<string, int>(features.GroupBy(p => camFeatureTypeMap.GetMISUMIFeatureType(p)).ToDictionary(q => q.Key, q => q.Count()));

                foreach (KeyValuePair<string, int> pair in featureTypes)
                {
                    XmlElement featureTypeNode = m_XmlDocument.CreateElement("FeatureType");
                    featureTypeNode.SetAttribute("name", pair.Key);
                    featureTypeNode.SetAttribute("quantity", pair.Value.ToString());

                    numberOfMachinedFeatNode.AppendChild(featureTypeNode);
                }
            }
            else
            {
                m_manager.LogFile.AddError(error);
            }

            return numberOfMachinedFeatNode;
        }


        private XmlElement ToolsInfo()
        {
            string[] tools = m_manager.CAMReport.GetTools();

            XmlElement toolsNode = m_XmlDocument.CreateElement("Tools");

            // Add total number of tools
            toolsNode.SetAttribute("quantity", tools.Length.ToString());

            // Add tools name and number of time each tool is used
            SortedDictionary<string, int> toolsMap = 
                new SortedDictionary <string, int>(tools.GroupBy(p => p).ToDictionary(q => q.Key, q => q.Count()));

            foreach (KeyValuePair<string, int> pair in toolsMap)
            {
                XmlElement toolNode = m_XmlDocument.CreateElement("Tool");
                toolNode.SetAttribute("name", pair.Key);
                toolNode.SetAttribute("quantity", pair.Value.ToString());

                toolsNode.AppendChild(toolNode);
            }

            return toolsNode;
        }


        private XmlElement CollisionsInfo()
        {
            // Create the Collision node
            XmlElement collisionsNode = m_XmlDocument.CreateElement("Collisions");

            // Add the number of collisions
            collisionsNode.SetAttribute("quantity", m_manager.CAMReport.GetNumCollisions().ToString());

            foreach (CAMReport.SetupInfo setup in m_manager.CAMReport.GetSetups())
            {
                // Define the Setup Neutral Name
                string setupNeutralName = "Setup_" + setup.Id.ToString();

                // Create the node for the setup 
                XmlElement setupNode = m_XmlDocument.CreateElement(setupNeutralName);

                // Add the opcode of the setup as an attribute
                setupNode.SetAttribute("name", setup.OpCode);

                // Add the number of collision as the node value
                setupNode.InnerText = setup.NumCollisions.ToString();

                // Add the setup node as a child to the Collision node
                collisionsNode.AppendChild(setupNode);
            }

            return collisionsNode;
        }


        private XmlElement DeviationInfo()
        {
            XmlElement deviationNode = m_XmlDocument.CreateElement("DeviationBetweenCADAndMachinedPart");

            // WeightDeviation (unitless)
            double weightDeviationValue = m_manager.CAMReport.GetWeightDeviation(out string weightDeviationUnit);
            double weightDeviationFactor = 1.0;
            XmlElement weightDeviationNode = m_XmlDocument.CreateElement("WeightDeviation");
            weightDeviationNode.SetAttribute("unit", "%");
            weightDeviationNode.SetAttribute("value", (weightDeviationFactor * weightDeviationValue).ToString("0.000"));


            deviationNode.AppendChild(weightDeviationNode);

            // Geometry Deviation (in)
            double geometryDeviationValue = m_manager.CAMReport.GetGeometryDeviation(out string geometryDeviationUnit);
            double geometryDeviationFactor = geometryDeviationUnit == "mm" ? 1.0 / 25.4 : 1.0;
            XmlElement geometryDeviationNode = m_XmlDocument.CreateElement("GeometryDeviation");
            geometryDeviationNode.SetAttribute("unit", "in");
            geometryDeviationNode.SetAttribute("value", (geometryDeviationFactor * geometryDeviationValue).ToString("0.000"));

            deviationNode.AppendChild(geometryDeviationNode);

            return deviationNode;
        }


        private XmlElement FeatureDeviationInfo()
        {
            XmlElement featureDeviationNode = m_XmlDocument.CreateElement("DeviationBetweenCADAndMachinedFeatures");

            Dictionary<string,Tuple<double,string>> geometryDeviations = m_manager.CAMReport.GetFeatureGeometryDeviations();
            foreach(KeyValuePair<string,Tuple<double,string>> feature in geometryDeviations)
            {
                double geometryDeviationFactor = feature.Value.Item2 == "mm" ? 1.0 / 25.4 : 1.0;
                XmlElement geometryDeviationNode = m_XmlDocument.CreateElement("GeometryDeviation");
                geometryDeviationNode.SetAttribute("name", feature.Key);
                geometryDeviationNode.SetAttribute("unit", "in");
                geometryDeviationNode.SetAttribute("value", (geometryDeviationFactor * feature.Value.Item1).ToString("0.000"));

                featureDeviationNode.AppendChild(geometryDeviationNode);
            }

            return featureDeviationNode;
        }


        private XmlElement OpCodesInfo()
        {
            int rank = 0;

            XmlElement opCodesNode = m_XmlDocument.CreateElement("Operations");

            // NUM SETUPS
            XmlElement numSetupsNode = m_XmlDocument.CreateElement("NumSetups");
            opCodesNode.AppendChild(numSetupsNode);
            numSetupsNode.InnerText = m_manager.CAMReport.GetNumSetups().ToString();

            // CAM CHECK 
            if (m_manager.CAMReport.GetStatus() != CAMReport.Status.SUCCESS)
            {
                XmlElement opCodeNode = CreateOpCodeNode(rank++, "CAM CHECK");
                opCodesNode.AppendChild(opCodeNode);
            }
          
            if (m_manager.CAMReport.GetStatus() != CAMReport.Status.FAILURE)
            {
                // CUT
                if (m_manager.CAMReport.HasBestCandidateStock())
                {
                    XmlElement opCodeNode = CreateOpCodeNode(rank++, "CUT");
                    opCodesNode.AppendChild(opCodeNode);

                    Stock stock = m_manager.CAMReport.GetBestCandidateStock(out string stockUnit);
                    double factor = stockUnit == "mm" ? 1.0 / 25.4 : 1.0;

                    if (stock is StockBlock)
                    {
                        // Nothing to add
                    }
                    else if (stock is StockBar)
                    {
                        StockBar bar = stock as StockBar;

                        // CutLength (in)
                        XmlElement cutLengthNode = m_XmlDocument.CreateElement("CutLength");
                        cutLengthNode.SetAttribute("unit", "in");
                        cutLengthNode.SetAttribute("value", (factor * bar.GetCutLength(true)).ToString("0.000"));
                        opCodeNode.AppendChild(cutLengthNode);
                    }
                    else if (stock is StockPlate)
                    {
                        StockPlate plate = stock as StockPlate;

                        // CutLength1 (in)
                        XmlElement cutLengthNode1 = m_XmlDocument.CreateElement("CutLength1");
                        cutLengthNode1.SetAttribute("unit", "in");
                        cutLengthNode1.SetAttribute("value", (factor * plate.GetCutLength1(true)).ToString("0.000"));
                        opCodeNode.AppendChild(cutLengthNode1);

                        // CutLength2 (in)
                        XmlElement cutLengthNode2 = m_XmlDocument.CreateElement("CutLength2");
                        cutLengthNode2.SetAttribute("unit", "in");
                        cutLengthNode2.SetAttribute("value", (factor * plate.GetCutLength2(true)).ToString("0.000"));
                        opCodeNode.AppendChild(cutLengthNode2);
                    }
                }

                // ROUTING
                if (m_manager.CAMReport.IsNearNetBlankUsed())
                {
                    XmlElement opCodeNode = CreateOpCodeNode(rank++, "ROUTING");
                    opCodesNode.AppendChild(opCodeNode);

                    // DXF File Path
                    XmlElement localPathNode = m_XmlDocument.CreateElement("LocalPath");
                    localPathNode.InnerText = m_manager.CAMReport.GetDXFFilePath();
                    opCodeNode.AppendChild(localPathNode);

                    // Area (in2)
                    double area = m_manager.CAMReport.GetNearNetBlankArea(out string areaUnit);
                    double areaFactor = areaUnit == "mm2" ? 1.0 / Math.Pow(25.4, 2.0) : 1.0;
                    XmlElement areaNode = m_XmlDocument.CreateElement("Area");
                    areaNode.SetAttribute("unit", "in2");
                    areaNode.SetAttribute("value", (areaFactor * area).ToString("0.000"));
                    opCodeNode.AppendChild(areaNode);

                    // Perimeter (in)
                    double perimeter = m_manager.CAMReport.GetNearNetBlankPerimeter(out string perimeterUnit);
                    double perimeterFactor = perimeterUnit == "mm" ? 1.0 / 25.4 : 1.0;
                    XmlElement perimeterNode = m_XmlDocument.CreateElement("Perimeter");
                    perimeterNode.SetAttribute("unit", "in");
                    perimeterNode.SetAttribute("value", (perimeterFactor * perimeter).ToString("0.000"));
                    opCodeNode.AppendChild(perimeterNode);
                }

                // SURFACE FINISHING
                if (m_manager.CAMReport.IsNearNetBlankUsed())
                {
                    XmlElement opCodeNode = CreateOpCodeNode(rank++, "SURFACE FINISHING");
                    opCodesNode.AppendChild(opCodeNode);
                }

                // MACHINING
                foreach (CAMReport.SetupInfo setup in m_manager.CAMReport.GetSetups())
                {
                    XmlElement opCodeNode = CreateOpCodeNode(rank++, "MACHINING");
                    opCodesNode.AppendChild(opCodeNode);

                    // Machine
                    XmlElement machineNode = m_XmlDocument.CreateElement("Machine");
                    machineNode.InnerText = m_manager.Machine.MachineID;
                    opCodeNode.AppendChild(machineNode);

                    // Setup 
                    XmlElement setupNode = m_XmlDocument.CreateElement("Setup");
                    XmlElement nameNode = m_XmlDocument.CreateElement("Name");
                    XmlElement csysNode = m_XmlDocument.CreateElement("CSYS");
                    nameNode.InnerText = setup.CsysName;
                    csysNode.InnerText = setup.OpCode;
                    setupNode.AppendChild(nameNode);
                    setupNode.AppendChild(csysNode);                 
                    opCodeNode.AppendChild(setupNode);

                    XmlElement manufacturingTimeNode = m_XmlDocument.CreateElement("ManufacturingTime");
                    manufacturingTimeNode.InnerText = setup.ManufacturingTime.ToString("0.00");
                    opCodeNode.AppendChild(manufacturingTimeNode);
                }

                // SURFACE DRILL
                if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN)
                {
                    XmlElement opCodeNode = CreateOpCodeNode(rank++, "DRILL");
                    opCodesNode.AppendChild(opCodeNode);

                    // Hole
                    XmlElement holeNode = m_XmlDocument.CreateElement("Hole");
                    holeNode.InnerText = "CLEARANCE HOLE 001";
                    opCodeNode.AppendChild(holeNode);
                }
            }

            return opCodesNode;
        }


        private XmlElement CreateOpCodeNode(int rank, string type)
        {
            XmlElement opCodeNode = m_XmlDocument.CreateElement("Operation");
            XmlElement typeNode = m_XmlDocument.CreateElement("Type");
            opCodeNode.AppendChild(typeNode);

            opCodeNode.SetAttribute("Rank", rank.ToString());
            typeNode.InnerText = type;

            return opCodeNode;
        }


        private CAMAutomationManager m_manager;
        private string m_filePath;

        private XmlDocument m_XmlDocument;
    }
}
