using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml;

namespace CAMAutomation
{
    public class InputFileParser
    {
        public InputFileParser()
        {
            m_manager = CAMAutomationManager.GetInstance();
        }


        ~InputFileParser()
        {
            // empty
        }


        public bool Parse()
        {
            try
            {
                // Parse the file
                ParseFile();

                // Validate the data
                ValidateData();
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                return false;
            }

            return true;
        }


        private void ParseFile()
        {
            // Load the document
            XmlDocument doc = new XmlDocument();
            doc.Load(m_manager.InputFile);

            // Retrieve the root
            XmlElement root = doc.DocumentElement;

            // Get the Request ID
            XmlNode requestIDNode = root.SelectSingleNode("Request//RequestID");
            if (requestIDNode != null)
            {
                m_manager.RequestID = requestIDNode.InnerText;
            }
            else
            {
                throw new Exception("<RequestID> node was not found");
            }

            // Get the Quote ID
            XmlNode quoteIDNode = root.SelectSingleNode("Request//QuoteID");
            if (quoteIDNode != null)
            {
                m_manager.QuoteID = quoteIDNode.InnerText;
            }
            else
            {
                throw new Exception("<QuoteID> node was not found");
            }

            // Get the Fixture Path
            XmlNode fixturePathNode = root.SelectSingleNode("Fixture//LocalPath");
            if (fixturePathNode != null)
            {
                m_manager.FixturePath = fixturePathNode.InnerText;
            }
            else
            {
                throw new Exception("<Fixture.LocalPath> node was not found");
            }

            // Get the Fixture Type
            XmlNode fixtureTypeNode = root.SelectSingleNode("Fixture//FixtureType");
            if (fixtureTypeNode != null)
            {
                m_manager.FixtureType = fixtureTypeNode.InnerText;
            }
            else
            {
                throw new Exception("<Fixture.FixtureType> node was not found");
            }

            // Get the Blank Path (optional)
            XmlNode blankPathNode = root.SelectSingleNode("Blank//LocalPath");
            if (blankPathNode != null)
            {
                m_manager.BlankPath = blankPathNode.InnerText;
            }

            // Get the CAD Path
            XmlNode cadPathNode = root.SelectSingleNode("Part//CAD//LocalPath");
            if (cadPathNode != null)
            {
                m_manager.CADPath = cadPathNode.InnerText;
            }
            else
            {
                throw new Exception("<CAD.LocalPath> node was not found");
            }

            // Get the envs path
            try
            {
                XmlNode envsPathNode = root.SelectSingleNode("Part//CAD//EnvPath");
                if (envsPathNode != null)
                {
                    m_manager.EnvsPath = envsPathNode.InnerText;
                    m_manager.Json = true;
                }
            }
            catch
            {
                m_manager.LogFile.AddMessage("Envs path not provided...");
                m_manager.Json = false;
            }

            // Get the CAD File Format
            XmlNode cadFileFormatNode = root.SelectSingleNode("Part//CAD//FileFormat");
            if (cadFileFormatNode != null)
            {

                m_manager.CADFileFormat = cadFileFormatNode.InnerText;
            }
            else
            {
                throw new Exception("<CAD.FileFormat> node was not found");
            }

            // Get the Mirror CAD option
            XmlNode mirrorCADNode = root.SelectSingleNode("Part//AsShown");
            if (mirrorCADNode != null)
            {
                m_manager.AsShown = mirrorCADNode.InnerText;
            }
            else
            {
                throw new Exception("<AsShown> node was not found");
            }

            // Get Material
            XmlNode materialNode = root.SelectSingleNode("Part//Material");
            if (materialNode != null)
            {
                m_manager.Material = materialNode.InnerText;
            }
            else
            {
                throw new Exception("<Material> node was not found");
            }

            // Get the AllowNearNetBlank Option (optional)
            XmlNode allowNearNetBlankNode = root.SelectSingleNode("Options//AllowNearNetBlank");
            if (allowNearNetBlankNode != null)
            {
                if (allowNearNetBlankNode.InnerText == "YES")
                {
                    m_manager.AllowNearNetBlank = true;
                }
            }

            // Get the selected setup
            foreach (XmlNode node in root.SelectNodes("Setup//Select"))
            {
                XmlAttribute setupAttribute = node.Attributes["setup"];
                XmlAttribute priorityAttribute = node.Attributes["priority"];

                if (setupAttribute != null &&
                    priorityAttribute != null &&
                    int.TryParse(setupAttribute.Value, out int setup) && 
                    int.TryParse(priorityAttribute.Value, out int priority))
                {
                    m_manager.SetupSelection.Add(setup, priority);
                }
            }
        }


        private void ValidateData()
        {
            // Validate Fixture File
            if (Path.GetExtension(m_manager.FixturePath) != ".prt")
            {
                throw new Exception("Fixture file should be a .prt file");
            }

            // Validate Fixture Type
            Utilities.CAMSingleFixtureHandler.FixtureType fixtureType = Utilities.CAMSingleFixtureHandler.GetFixtureTypeFromString(m_manager.FixtureType);
            if (fixtureType == Utilities.CAMSingleFixtureHandler.FixtureType.INVALID)
            {
                throw new Exception("Invalid fixture type");
            }

            // Validate Blank File
            if (!String.IsNullOrEmpty(m_manager.BlankPath) && Path.GetExtension(m_manager.BlankPath) != ".prt")
            {
                throw new Exception("Blank file should be a .prt file");
            }

            // Validate CAD File
            string extension = Path.GetExtension(m_manager.CADPath);
            Importer.Type type = Importer.GetTypeFromString(m_manager.CADFileFormat);
            if (type == Importer.Type.NX)
            {
                if (extension.ToLower() != ".prt")
                {
                    throw new Exception("NX Format is specified for the CAD file. File extension should be .prt");
                }
            }
            else if (type == Importer.Type.IGES)
            {
                if (extension.ToLower() != ".igs" && extension.ToLower() != ".iges")
                {
                    throw new Exception("IGES Format is specified for the CAD file. File extension should be .igs or .iges");
                }
            }
            else if (type == Importer.Type.STEP203 || type == Importer.Type.STEP214 || type == Importer.Type.STEP242)
            {
                if (extension.ToLower() != ".stp" && extension.ToLower() != ".step")
                {
                    throw new Exception("STEP Format is specified for the CAD file. File extension should be .stp or .step");
                }
            }
            else if (type == Importer.Type.SAT)
            {
                if (extension.ToLower() != ".sat")
                {
                    throw new Exception("SAT Format is specified for the CAD file. File extension should be .sat");
                }
            }
            else if (type == Importer.Type.SLDPRT)
            {
                if (extension.ToLower() != ".sldprt")
                {
                    throw new Exception("Solidworks Format is specified for the CAD file. File extension should be .sldprt");
                }
            }
            else if (type == Importer.Type.PAR)
            {
                if (extension.ToLower() != ".par")
                {
                    throw new Exception("Solid Edge Format is specified for the CAD file. File extension should be .par");
                }
            }
            else if (type == Importer.Type.INVALID)
            {
                throw new Exception("Invalid format for the CAD file");
            }

            // Validate AsShown
            if (m_manager.AsShown != "OPP" && m_manager.AsShown != "SHN")
            {
                throw new Exception("Invalid value for node <AsShown>. Supported values are \"OPP\" and \"SHN\"");
            }
        }


        private CAMAutomationManager m_manager;
    }
}
