using System;
using System.Collections.Generic;
using System.Linq;


namespace CAMAutomation
{
    public class CAMReport
    {       
        public enum Status { UNSET = -1, SUCCESS = 0, PARTIALSUCCESS = 1, FAILURE = 2 }

        public class SetupInfo
        {
            public SetupInfo(int id, string name, string opCode, string csysName)
            {
                Id = id;

                Name = name;
                OpCode = opCode;
                CsysName = csysName;

                ManufacturingTime = 0.0;
                NumCollisions = 0;
            }

            public int Id { get; }
            public string Name { get; }
            public string OpCode { get;  }
            public string CsysName { get;  }

            public double ManufacturingTime { get; set; }
            public int NumCollisions { get; set; }
        }


        public CAMReport()
        {
            m_status = Status.UNSET;

            m_bestCandidateStock = new Tuple<Stock, string>(null, String.Empty);

            m_setups = new List<SetupInfo>();

            m_totalFeaturesToMachine = new List<string>();
            m_machinedFeatures = new List<string>();
            m_notMachinedFeatures = new List<string>();

            m_operationTypes = new List<string>();

            m_tools = new List<string>();

            m_weightDeviation = new Tuple<double, string>(0.0, String.Empty);
            m_geometryDeviation = new Tuple<double, string>(0.0, String.Empty);
            m_featureGeometryDeviations = new Dictionary<string, Tuple<double, string>>();

            m_nearNetBlankArea = new Tuple<double, string>(0.0, String.Empty);
            m_nearNetBlankPerimeter = new Tuple<double, string>(0.0, String.Empty);
        }


        ~CAMReport()
        {
            // empty
        }


        public Status GetStatus()
        {
            return m_status == Status.UNSET ? Status.FAILURE : m_status;
        }


        public void SetStatus(Status status)
        {
            switch (m_status)
            {
                case Status.UNSET:
                    {
                        m_status = status;
                        break;
                    }
                case Status.SUCCESS:
                    {
                        m_status = status;
                        break;
                    }

                case Status.PARTIALSUCCESS:
                    {
                        m_status = status != Status.SUCCESS ? status : m_status;
                        break;
                    }
                case Status.FAILURE:
                    {
                        break;
                    }
            }
        }


        public bool HasBestCandidateStock()
        {
            return m_bestCandidateStock.Item1 != null;
        }


        public Stock GetBestCandidateStock(out string unit)
        {
            unit = m_bestCandidateStock.Item2;

            return m_bestCandidateStock.Item1;
        }


        public void AddBestCandidateStock(Stock stock, string unit)
        {
            m_bestCandidateStock = new Tuple<Stock, string>(stock, unit);
        }


        public bool HasSelectedMachine()
        {
            return m_selectedMachine != null;
        }


        public Machine GetSelectedMachine()
        {
            return m_selectedMachine;
        }


        public void SetSelectedMachine(Machine candidateMachine)
        {
            m_selectedMachine = candidateMachine;
        }


        public int GetNumSetups()
        {
            return m_setups.Count;
        }


        public SetupInfo[] GetSetups()
        {
            return m_setups.ToArray();
        }


        public void AddSetup(int id, string name, string opCode, string csysName)
        {
            if (!m_setups.Any(p => p.Id == id))
            {
                m_setups.Add(new SetupInfo(id, name, opCode, csysName));
            }
        }


        public void AddManufacturingTime(int id, double time)
        {
            SetupInfo setup = m_setups.FirstOrDefault(p => p.Id == id);
            if (setup != null)
            {
                setup.ManufacturingTime = time;
            }
        }


        public int GetNumCollisions()
        {
            return m_setups.Sum(p => p.NumCollisions);
        }


        public void AddCollision(int id, int numCollisions)
        {
            SetupInfo setup = m_setups.FirstOrDefault(p => p.Id == id);
            if (setup != null)
            {
                setup.NumCollisions = numCollisions;
            }
        }


        public string[] GetTotalFeaturesToMachine()
        {
            return m_totalFeaturesToMachine.ToArray();
        }


        public void AddTotalFeaturesToMachine(string[] features)
        {
            m_totalFeaturesToMachine.AddRange(features);
        }


        public string[] GetMachinedFeatures()
        {
            return m_machinedFeatures.ToArray();
        }


        public void AddMachinedFeatures(string[] features)
        {
            m_machinedFeatures.AddRange(features);
        }


        public string[] GetNotMachinedFeatures()
        {
            return m_notMachinedFeatures.ToArray();
        }


        public void AddNotMachinedFeatures(string[] features)
        {
            m_notMachinedFeatures.AddRange(features);
        }


        public string[] GetOperationTypes()
        {
            return m_operationTypes.ToArray();
        }


        public void AddOperationTypes(string[] opType)
        {
            m_operationTypes.AddRange(opType);
        }


        public string[] GetTools()
        {
            return m_tools.ToArray();
        }


        public void AddTools(string[] tools)
        {
            m_tools.AddRange(tools);
        }


        public double GetWeightDeviation(out string unit)
        {
            unit = m_weightDeviation.Item2;

            return m_weightDeviation.Item1;
        }


        public void AddWeightDeviation(double value, string unit)
        {
            m_weightDeviation = new Tuple<double, string>(value, unit);
        }


        public double GetGeometryDeviation(out string unit)
        {
            unit = m_geometryDeviation.Item2;

            return m_geometryDeviation.Item1;
        }


        public void AddGeometryDeviation(double value, string unit)
        {
            m_geometryDeviation = new Tuple<double, string>(value, unit);
        }

        public Dictionary<string, Tuple<double,string>> GetFeatureGeometryDeviations()
        {
            return m_featureGeometryDeviations.ToDictionary(p => p.Key, p => p.Value);
        }


        public void AddFeatureGeometryDeviation(string featureName, double value, string unit)
        {
            if (!m_featureGeometryDeviations.ContainsKey(featureName))
            {
                m_featureGeometryDeviations.Add(featureName, new Tuple<double, string>(value, unit));
            }
        }


        public bool IsNearNetBlankUsed()
        {
            return !String.IsNullOrEmpty(m_dxfFilePath);
        }


        public string GetDXFFilePath()
        {
            return m_dxfFilePath;
        }


        public void AddDXFFilePath(string path)
        {
            m_dxfFilePath = path;
        }


        public double GetNearNetBlankArea(out string unit)
        {
            unit = m_nearNetBlankArea.Item2;

            return m_nearNetBlankArea.Item1;
        }


        public void AddNearNetBlankArea(double value, string unit)
        {
            m_nearNetBlankArea = new Tuple<double, string>(value, unit);
        }


        public double GetNearNetBlankPerimeter(out string unit)
        {
            unit = m_nearNetBlankPerimeter.Item2;

            return m_nearNetBlankPerimeter.Item1;
        }


        public void AddNearNetBlankPerimeter(double value, string unit)
        {
            m_nearNetBlankPerimeter = new Tuple<double, string>(value, unit);
        }


        private Status m_status;

        private Tuple<Stock, string> m_bestCandidateStock;

        private Machine m_selectedMachine;

        private List<SetupInfo> m_setups;

        private List<string> m_totalFeaturesToMachine;
        private List<string> m_machinedFeatures;
        private List<string> m_notMachinedFeatures;

        private List<string> m_operationTypes;

        private List<string> m_tools;

        private Tuple<double, string> m_weightDeviation;
        private Tuple<double, string> m_geometryDeviation;
        private Dictionary<string, Tuple<double, string>> m_featureGeometryDeviations;

        private string m_dxfFilePath;
        private Tuple<double, string> m_nearNetBlankArea;
        private Tuple<double, string> m_nearNetBlankPerimeter;
    }
}
