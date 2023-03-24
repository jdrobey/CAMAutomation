using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.Assemblies;
using NXOpen.CAM;


namespace CAMAutomation
{
    public class CAMRun
    {
        public CAMRun()
        {
            m_setups = new List<CAMSingleSetup>();
        }

        ~CAMRun()
        {
            // empty
        }

        
        public int NumSetups
        {
            get
            {
                return m_setups.Count;
            }
        }


        public CAMSingleSetup GetSetup(int index)
        {
            if (index >= 0 && index < NumSetups)
            {
                return m_setups[index];
            }
            else
            {
                return null;
            }
        }


        public CAMSingleSetup CreateSetup(OrientGeometry mcs, FeatureGeometry workpiece, FeatureGeometry probingWorkpiece, Component component)
        {
            int Id = m_setups.Count;

            CAMSingleSetup newSetup = new CAMSingleSetup(Id + 1, mcs, workpiece, probingWorkpiece, component);
            m_setups.Add(newSetup);

            return newSetup;
        }


        public OrientGeometry LastMcs
        {
            get
            {
                return m_setups.Count != 0 ? m_setups.Last().Mcs : null;
            }
        }


        public FeatureGeometry LastWorkpiece
        {
            get
            {
                if (m_setups.Count != 0)
                {
                    CAMSingleSetup lastSetup = m_setups.Last();

                    return lastSetup.DoesIncludeNotchProbing() ? lastSetup.NotchProbingWorkpiece : lastSetup.Workpiece;
                }
                else
                {
                    return null;
                }
            }
        }


        public Component LastComponent
        {
            get
            {
                return m_setups.Count != 0 ? m_setups.Last().Component : null;
            }
        }


        private List<CAMSingleSetup> m_setups;
    }
}
