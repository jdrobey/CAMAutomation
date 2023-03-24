using System;
using System.Linq;

using NXOpen;

namespace CAMAutomation
{
    public class BlankCoordinateSystemsCreator : CoordinateSystemsCreator
    {
        public BlankCoordinateSystemsCreator(Part part) : base(part)
        {
            // empty
        }


        ~BlankCoordinateSystemsCreator()
        {
            // empty
        }


        protected override void DetectHolePatterns()
        {
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                // Call Base Method
                base.DetectHolePatterns();
            }
        }


        protected override void CheckState()
        {
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                // One, and only one hole pattern should be found in the Blank, since one hole pattern has been found in the CAD
                // If it is not the case, alignment will be impossible
                if (m_holePatterns.Length == 0)
                {
                    // No Hole patterns were found.
                    throw new Exception("No Hole Pattern was found in the Blank");
                }
                else if (m_holePatterns.Length == 1)
                {
                    // A valid hole pattern has been found.
                    m_manager.LogFile.AddMessage("A valid Hole Pattern was found in the Blank", m_context);
                }
                else
                {
                    // Multiple Hole patterns were found. Raise a warning
                    throw new Exception("Multiple Hole Patterns were found in the Blank");
                }
            }
        }


        protected override void CreateCoordinateSystems()
        {
            Point3d origin;
            Vector3d x, y, z;

            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN ||
                m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                // Create BLANK_CSYS_CLAMPING
                ComputeClampingCsys(out origin, out x, out y, out z, out double clampingThickness, out double clampingWidth);
                m_clampingDatumCsys = Utilities.NXOpenUtils.CreateDatumCsys(origin, x, y, "BLANK_CSYS_CLAMPING");

                // Add clamping thickness and clamping width attribute to the BLANK_CSYS_CLAMPING
                Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsys, "MISUMI", "CLAMPING_THICKNESS", clampingThickness);
                Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsys, "MISUMI", "CLAMPING_WIDTH", clampingWidth);

                if (m_manager.AllowNearNetBlank)
                {
                    // Create BLANK_CSYS_CLAMPING_NEAR_NET_BLANK
                    m_clampingDatumCsysNearNetBlank = Utilities.NXOpenUtils.CreateDatumCsys(origin, x, y, "BLANK_CSYS_CLAMPING_NEAR_NET_BLANK");

                    // Add clamping thickness and clamping width attribute to the BLANK_CSYS_CLAMPING_NEAR_NET_BLANK
                    Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsysNearNetBlank, "MISUMI", "CLAMPING_THICKNESS", clampingThickness);
                    Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsysNearNetBlank, "MISUMI", "CLAMPING_WIDTH", clampingWidth);
                }
            }
            else if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                // Create BLANK_CSYS_FIXTURE from Hole Pattern
                ComputeFixtureCsysFromHolePattern(m_holePatterns.First(), out origin, out x, out y, out z);
                m_fixtureDatumCsys = Utilities.NXOpenUtils.CreateDatumCsys(origin, x, y, "BLANK_CSYS_FIXTURE");
            }
        }


        private void ComputeClampingCsys(out Point3d origin, out Vector3d x, out Vector3d y, out Vector3d z, out double clampingThickness, out double clampingWidth)
        {
            // Create a temporary csys that will be used to compute the bounding box
            Point3d tmpOrigin = new Point3d();
            Vector3d tmpX = new Vector3d(1.0, 0.0, 0.0);
            Vector3d tmpY = new Vector3d(0.0, 1.0, 0.0);
            CartesianCoordinateSystem tmpCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(tmpOrigin, tmpX, tmpY);

            // Compute the bounding box of the Blank
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(m_body, tmpCsys);

            if (boundingBox != null)
            {
                // Define the Blank origin as well as X, Y and Z dimensions
                Point3d blankOrigin = new Point3d();
                double X = boundingBox.XLength;
                double Y = boundingBox.YLength;
                double Z = boundingBox.ZLength;

                // Define the Blank Clamping CSYS origin, orientation
                // We need to create the Blank Clamping CSYS at the bottom face of the extra material  
                origin = Utilities.MathUtils.Add(blankOrigin, new Point3d(X / 2.0, Y / 2.0, 0.0));
                x = new Vector3d(1.0, 0.0, 0.0);
                y = new Vector3d(0.0, 1.0, 0.0);
                z = new Vector3d(0.0, 0.0, 1.0);
                clampingThickness = Y;
                clampingWidth = X;
            }
            else
            {
                throw new Exception("Unable to compute the Blank bounding box");
            }
        }    
    }
}
