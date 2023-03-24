using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;

namespace CAMAutomation
{
    public class CAMFeatureTypeMap
    {
        public CAMFeatureTypeMap()
        {
            m_FeatureTypeMap = new Dictionary<string, string>();
        }

        ~CAMFeatureTypeMap()
        {
            // empty
        }

        public bool BuildMap(out string error)
        {
            bool success = false;
            error = string.Empty;
            string featureTypeMapPath = string.Empty;

            string customDir = Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM");

            if (string.IsNullOrEmpty(customDir) || !Directory.Exists(customDir))
            {
                error = @" Directory 'custom' cannot be found";
                return success;
            }
            else
            {
                featureTypeMapPath = Path.Combine(customDir, @"feature_type_map\feature_type_map.xml");
                if (!File.Exists(featureTypeMapPath))
                {
                    error = @" File 'feature_type_map.xml' not found in folder '...\custom\feature_type_map\'";
                    return success;
                }
            }

            XDocument xmlDocument = null;
            try
            {
                xmlDocument = XDocument.Load(featureTypeMapPath);
            }
            catch (Exception)
            {
                return success;
            }

            // Check integrity of XML document
            if (xmlDocument.Descendants("FEATURE").Count() == 0)
            {
                error = @" File 'feature_type_map.xml' doesn't have element 'FEATURE'";
                return success;
            }

            IEnumerable<XElement> featureElements = xmlDocument
                                                   .Descendants("FEATURE")
                                                   .Where(
                                                           k => k.Attribute("type") != null &&
                                                           k.Attribute("type").Value.ToString() != String.Empty
                                                         );

            if (featureElements.Count() == 0)
            {
                error = @" File 'feature_type_map.xml' doesn't have a valid attribute 'type' for any element 'FEATURE'";
                return success;
            }

            featureElements = featureElements
                             .Where(
                                     k => k.Parent.Attribute("class") != null &&
                                     k.Parent.Attribute("class").Value.ToString() != String.Empty
                                   );

            if (featureElements.Count() == 0)
            {
                error = @" No element 'FEATURE' in  file 'feature_type_map.xml' has a parent with valid attribute 'class'";
                return success;
            }

            // populate feature map dictionary if XML is found and has appropriate elements and attributes
            m_FeatureTypeMap = featureElements.ToDictionary(
                                                             k => k.Attribute("type").Value.ToString(),
                                                             v => v.Parent.Attribute("class").Value.ToString()
                                                           );

            success = true;
            return success;
        }

        public string GetMISUMIFeatureType(string nxFeatureType)
        {
            return m_FeatureTypeMap.ContainsKey(nxFeatureType) ? m_FeatureTypeMap[nxFeatureType] : "OTHER";
        }

        public int GetNumMISUMIFeatureType()
        {
            return m_FeatureTypeMap.Count;
        }

        private Dictionary<string, string> m_FeatureTypeMap;

    }
}
