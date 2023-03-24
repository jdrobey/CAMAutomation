using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;


namespace CAMAutomation
{
    public class CAMRoughingTemplateSelector
    {
        public CAMRoughingTemplateSelector(Machine.MachineType machineType, string machineName, string material, CAMAutomationManager.AutomationAlgorithm algorithm, string unitSystemFolder)
        {
            m_machineType = machineType;
            m_machineName = machineName;
            m_material = material;
            m_algorithm = algorithm;
            m_unitSystemFolder = unitSystemFolder;
        }


        public string SelectRoughingTemplate()
        {
            // Machine Type
            string machineType = Machine.GetMachineTypeAsString(m_machineType);

            //Machine Name
            string machineName = m_machineName;

            // Material
            string material = m_material;

            // Algorithm
            string algorithm = m_algorithm == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN  ||
                               m_algorithm == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN ? "HolePattern" : "Generic";

            // Retrieve Template directory
            string templateDir = Path.Combine(Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM"), "template_part", m_unitSystemFolder);

            // Retrieve template file names
            string[] templates = Directory.GetFiles(templateDir, "*.prt").Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();

            // Properties
            string[] properties = new string[] { machineType.Replace("-", ""), machineName, material, algorithm };

            // Try all 4 properties. 
            // If not found, try the first 3, then the first 2, and so on.
            string templateName = string.Empty;
            bool found = false;
            int i = properties.Length;

            while (i > 0 && !found)
            {
                templateName = string.Join("__", properties.Take(i));
                found = templates.Contains(templateName);
                --i;
            }

            if (found)
            {
                return templateName;
            }
            else
            {
                return "mill_contour";
            }
        }


        private Machine.MachineType m_machineType;
        private string m_machineName;
        private CAMAutomationManager.AutomationAlgorithm m_algorithm;
        private string m_material;
        private string m_unitSystemFolder;
    }
}
