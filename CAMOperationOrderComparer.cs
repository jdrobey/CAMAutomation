using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NXOpen;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class CAMOperationOrderComparer : IComparer<NXOpen.CAM.Operation>
    {
        public CAMOperationOrderComparer()
        {
            Part workPart = Utilities.RemoteSession.NXSession.Parts.Work;
            if (workPart != null)
            {
                m_operations = workPart.CAMSetup.CAMOperationCollection.ToArray();
            }
        }

        ~CAMOperationOrderComparer()
        {
            // empty
        }


        public int Compare(NXOpen.CAM.Operation lhs, NXOpen.CAM.Operation rhs)
        {
            if (m_operations != null)
            {
                int lhsIdx = Array.IndexOf(m_operations, lhs);
                int rhsIdx = Array.IndexOf(m_operations, rhs);

                if (lhsIdx < rhsIdx)
                {
                    return -1;
                }
                else if (lhsIdx > rhsIdx)
                {
                    return 1;
                }
                else
                {
                    return 0;
                }
            }

            return 0;
        }


        private NXOpen.CAM.Operation[] m_operations;
    }
}
