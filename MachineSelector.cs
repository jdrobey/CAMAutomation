using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

using NXOpen;

namespace CAMAutomation
{
    public class MachineSelector
    {
        public MachineSelector(Part blankPart)
        {
            m_blankPart = blankPart;
            m_units = blankPart.PartUnits;

            m_manager = CAMAutomationManager.GetInstance();

            m_machines = new List<Machine>();
        }


        ~MachineSelector()
        {
            // empty
        }


        public Machine SelectMachine()
        {
            try
            {
                // Parse Input File
                ParseInputFile();

                // Retrieve Candidate Machine
                RetrieveCandidateMachine();

                return m_candidateMachine;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                return null;
            }
        }


        private void ParseInputFile()
        {
            // Clear the machines library
            m_machines.Clear();

            // Load the document
            XmlDocument doc = new XmlDocument();
            doc.Load(m_manager.InputFile);

            // Retrieve the XML root node
            XmlElement root = doc.DocumentElement;

            // Parse the entire machine library
            ParseMachineLibrary(root);
        }


        private void ParseMachineLibrary(XmlNode root)
        {
            foreach (XmlNode millingNode in root.SelectNodes("Machine//Library//Milling"))
            {
                // Create a new Machine
                Machine mach = new Machine();

                // Parse the machine attributes
                ParseMachineAttributes(mach, millingNode);

                // Add the machine to the library
                m_machines.Add(mach);
            }

            // Is there any machine in the library ?
            if (m_machines.Count == 0)
            {
                throw new Exception("No machine was defined in the library");
            }
        }


        private void ParseMachineAttributes(Machine mach, XmlNode millingNode)
        {
            // Machine ID
            XmlNode machineIDNode = millingNode.SelectSingleNode("ID");
            if (machineIDNode != null)
            {
                mach.MachineID = machineIDNode.InnerText;
            }
            else
            {
                throw new Exception("Missing <ID> node");
            }

            // Machine Type
            XmlNode machineType = millingNode.SelectSingleNode("MachineType");
            if (machineType != null)
            {              
                Machine.MachineType type = Machine.GetMachineTypeFromString(machineType.InnerText);
                if (mach.Type != Machine.MachineType.INVALID)
                {
                    mach.Type = type;
                }
                else
                {
                    throw new Exception("Value for <MachineType> is invalid");
                }
            }
            else
            {
                throw new Exception("Missing <MachineType> node");
            }

            // Support Pre-Finished Blank
            XmlNode supportsPreFinishedBlank = millingNode.SelectSingleNode("SupportPreFinishedBlank");
            if (supportsPreFinishedBlank != null)
            {
                if (supportsPreFinishedBlank.InnerText == "YES")
                {
                    mach.SupportsPreFinishedBlank = true;
                }
                else if (supportsPreFinishedBlank.InnerText == "NO")
                {
                    mach.SupportsPreFinishedBlank = false;
                }
                else
                {
                    throw new Exception("Value for <SupportPreFinishedBlank> is invalid");
                }
            }
            else
            {
                throw new Exception("Missing <SupportPreFinishedBlank> node");
            }

            // Status
            XmlNode status = millingNode.SelectSingleNode("Status");
            if (status != null)
            {
                if (status.InnerText == "Available")
                {
                    mach.IsAvailable = true;
                }
                else if (status.InnerText == "Unavailable")
                {
                    mach.IsAvailable = false;
                }
                else
                {
                    throw new Exception("Value for <Status> is invalid");
                }
            }
            else
            {
                throw new Exception("Missing <Status> node");
            }

            // Supported Materials
            XmlNode materialNode = millingNode.SelectSingleNode("Material");
            if (materialNode != null)
            {
                if (!String.IsNullOrEmpty(materialNode.InnerText))
                {
                    string[] materials = materialNode.InnerText.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                    foreach (string material in materials)
                    {
                        mach.AddSupportedMaterial(material);
                    }
                }
                else
                {
                    mach.AllMaterialsSupported = true;
                }
            }
            else
            {
                mach.AllMaterialsSupported = true;
            }

            // Max Vise Opening
            XmlNode maxViseOpeningNode = millingNode.SelectSingleNode("MaxViseOpening");
            if (maxViseOpeningNode != null)
            {
                ParseDimension(maxViseOpeningNode, out double value);
                mach.MaxViseOpening = value;
            }
            else
            {
                mach.AllViseOpeningSupported = true;
                mach.MaxViseOpening = -1.0;
            }

            // Max Weight
            XmlNode maxWeightNode = millingNode.SelectSingleNode("MaxWeight");
            if (maxWeightNode != null)
            {
                ParseMaxWeight(maxWeightNode, out double value);
                mach.MaxWeight = value;
            }
            else
            {
                mach.AllWeightSupported = true;
                mach.MaxWeight = -1.0;
            }

            // Min Work Envelope
            XmlNode minWorkEnvelopeNode = millingNode.SelectSingleNode("MinWorkEnvelope");
            if (minWorkEnvelopeNode != null)
            {
                ParseEnvelope(minWorkEnvelopeNode, out Machine.Envelope minEnvelope);
                mach.MinWorkEnvelope = minEnvelope;
            }
            else
            {
                throw new Exception("Missing <MinWorkEnvelope> node");
            }

            // Max Work Envelope
            XmlNode maxWorkEnvelopeNode = millingNode.SelectSingleNode("MaxWorkEnvelope");
            if (maxWorkEnvelopeNode != null)
            {
                ParseEnvelope(maxWorkEnvelopeNode, out Machine.Envelope maxEnvelope);
                mach.MaxWorkEnvelope = maxEnvelope;
            }
            else
            {
                throw new Exception("Missing <MaxWorkEnvelope> node");
            }
        }


        private void ParseEnvelope(XmlNode workEnvelopeNode, out Machine.Envelope workEnvelope)
        {
            workEnvelope = new Machine.Envelope();

            XmlNode dim1 = workEnvelopeNode.SelectSingleNode("Dim1");
            XmlNode dim2 = workEnvelopeNode.SelectSingleNode("Dim2");
            XmlNode dim3 = workEnvelopeNode.SelectSingleNode("Dim3");

            // Dimension 1
            if (dim1 != null)
            {
                ParseDimension(dim1, out double value);
                workEnvelope.X = value;
            }
            else
            {
                throw new Exception("Missing dimension node <Dim1>");
            }

            // Dimension 2
            if (dim2 != null)
            {
                ParseDimension(dim2, out double value);
                workEnvelope.Y = value;
            }
            else
            {
                throw new Exception("Missing dimension node <Dim2>");
            }

            // Dimension 3
            if (dim3 != null)
            {
                ParseDimension(dim3, out double value);
                workEnvelope.Z = value;
            }
            else
            {
                throw new Exception("Missing dimension node <Dim3>");
            }
        }


        private void ParseDimension(XmlNode node, out double value)
        {
            try
            {
                // Unit
                double factor = 1.0;
                XmlAttribute unitAttribute = node.Attributes["Unit"];
                if (unitAttribute != null)
                {
                    if (unitAttribute.Value == "in" && m_units == BasePart.Units.Millimeters)
                    {
                        factor = 25.4;
                    }
                    else if (unitAttribute.Value == "mm" && m_units == BasePart.Units.Inches)
                    {
                        factor = 1.0 / 25.4;
                    }
                }

                // Value
                XmlAttribute valueAttribute = node.Attributes["Value"];
                if (valueAttribute != null)
                {
                    if (double.TryParse(valueAttribute.Value, out value))
                    {
                        if (value >= 0.0)
                        {
                            value *= factor;
                        }
                        else
                        {
                            throw new Exception("Negative Value found in node <" + node.Name + ">");
                        }
                    }
                    else
                    {
                        throw new Exception("Invalid Value in node <" + node.Name + ">");
                    }
                }
                else
                {
                    throw new Exception("Value not found in node <" + node.Name + ">");
                }
            }
            catch (Exception ex)
            {
                value = 0.0;
                throw ex;
            }
        }


        private void ParseMaxWeight(XmlNode node, out double value)
        {
            try
            {
                // Unit
                // We always convert and store max weight in pounds
                double factor = 1.0;
                XmlAttribute unitAttribute = node.Attributes["Unit"];
                if (unitAttribute != null)
                {
                    if (unitAttribute.Value == "g")
                    {
                        factor = 0.00220462;
                    }
                    else if (unitAttribute.Value == "kg")
                    {
                        factor = 2.20462;
                    }
                    else if (unitAttribute.Value == "lb")
                    {
                        factor = 1.0;
                    }
                    else
                    {
                        throw new Exception("Invalid Unit in node <" + node.Name + ">");
                    }
                }

                // Value
                XmlAttribute valueAttribute = node.Attributes["Value"];
                if (valueAttribute != null)
                {
                    if (double.TryParse(valueAttribute.Value, out value))
                    {
                        if (value > 0.0)
                        {
                            value *= factor;
                        }
                        else
                        {
                            throw new Exception("Negative Value found in node <" + node.Name + ">");
                        }
                    }
                    else
                    {
                        throw new Exception("Invalid Value in node <" + node.Name + ">");
                    }
                }
                else
                {
                    throw new Exception("Value not found in node <" + node.Name + ">");
                }
            }
            catch (Exception ex)
            {
                value = 0.0;
                throw ex;
            }
        }


        private void RetrieveCandidateMachine()
        {
            m_machines = m_machines.OrderBy(p => p.Type).ThenBy(p => p.GetVolume()).ToList();

            // Retrieve Material
            string material = m_manager.Material;

            // Retrieve machine type from fixture type
            Machine.MachineType machineType = Machine.GetMachineTypeFromString(m_manager.FixtureType);

            // Compute Blank Bounding Box
            BoundingBox blankBoundingBox = BoundingBox.ComputeBodyBoundingBox(m_blankPart.Bodies.ToArray().First());

            double blankWeight = ComputeBlankWeightInPounds();
            double maxClampingThickness = Math.Max(blankBoundingBox.XLength, Math.Max(blankBoundingBox.YLength, blankBoundingBox.ZLength));

            // Retrieve the best candidate machine
            if (!m_manager.IsBlankFromStock)
            {
                m_candidateMachine = m_machines.Where(p => p.IsAvailable)
                                                        .Where(p => p.Type == machineType)
                                                        .Where(p => p.SupportsPreFinishedBlank)
                                                        .Where(p => p.IsMaterialSupported(material))
                                                        .Where(p => p.IsWeightSupported(blankWeight))
                                                        .Where(p => p.IsViseOpeningSupported(maxClampingThickness))
                                                        .Where(p => p.DoesPartFitIn(blankBoundingBox.XLength, blankBoundingBox.YLength, blankBoundingBox.ZLength))
                                                        .Where(p => p.IsPartTooSmallFor(blankBoundingBox.XLength, blankBoundingBox.YLength, blankBoundingBox.ZLength))
                                                        .FirstOrDefault();
            }
            else
            {
                m_candidateMachine = m_machines.Where(p => p.IsAvailable)
                                                        .Where(p => p.Type == machineType)
                                                        .Where(p => p.IsMaterialSupported(material))
                                                        .Where(p => p.IsWeightSupported(blankWeight))
                                                        .Where(p => p.IsViseOpeningSupported(maxClampingThickness))
                                                        .Where(p => p.DoesPartFitIn(blankBoundingBox.XLength, blankBoundingBox.YLength, blankBoundingBox.ZLength))
                                                        .Where(p => p.IsPartTooSmallFor(blankBoundingBox.XLength, blankBoundingBox.YLength, blankBoundingBox.ZLength))
                                                        .FirstOrDefault();
            }

            // Is there any candidate machine ?
            if (m_candidateMachine != null)
            {
                m_manager.CAMReport.SetSelectedMachine(m_candidateMachine);
            }
            else
            {
                throw new Exception("There is no machine able to process the request");
            }
        }


        private double ComputeBlankWeightInPounds()
        {
            // Compute Volume in (in^3) (since density is stored in lb/in^3 and we need the weight in pound)
            double factor = m_units == BasePart.Units.Millimeters ? 1.0 / Math.Pow(25.4, 3.0) : 1.0;
            double blankVolume = factor * Utilities.NXOpenUtils.GetVolume(m_blankPart.Bodies.ToArray().First());
            
            // Get Material Density
            string customDir = Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM");

            if (customDir != null && Directory.Exists(customDir))
            {
                string materialLibrary = Path.Combine(customDir, "material_library", "material_library.xml");

                if (File.Exists(materialLibrary))
                {
                    // Load the document
                    XmlDocument doc = new XmlDocument();
                    doc.Load(materialLibrary);

                    // Find the density
                    // Density is specified in lb/in^3
                    foreach (XmlNode node in doc.DocumentElement.SelectNodes("Materials//Material"))
                    {
                        if (node.Attributes["name"] != null && node.Attributes["name"].Value == m_manager.Material)
                        {
                            if (node.Attributes["density"] != null && double.TryParse(node.Attributes["density"].Value, out double density) && density > 0.0)
                            {
                                double blankWeight = blankVolume * density;
                                return blankWeight;
                            }
                        }
                    }

                    throw new Exception("Unable to retrieve the material density for: " + m_manager.Material);
                }
                else
                {
                    throw new Exception("Unable to retrieve the material library file");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the custom folder");
            }
        }


        private Part m_blankPart;
        private BasePart.Units m_units;

        private List<Machine> m_machines;
        private Machine m_candidateMachine;

        private CAMAutomationManager m_manager;
    }
}
