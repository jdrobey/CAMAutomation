using System;
using System.Configuration;
using System.Linq;

using NXOpen;


namespace CAMAutomation
{
    public class BlankProbingNotchCreator
    {
        public BlankProbingNotchCreator(Part part)
        {
            m_part = part;
            m_manager = CAMAutomationManager.GetInstance();
        }
       

        ~BlankProbingNotchCreator()
        {
            // empty
        }


        public bool Execute()
        {
            try
            {
                if (ShouldCreateNotch())
                {
                    // Retrieve notch parameters
                    RetrieveNotchParameters();

                    // Display Prt
                    DisplayPart();

                    // Compute blank bounding box
                    ComputeBlankBoundingBox();

                    // Create notch profile
                    CreateNotchProfile();

                    // Detect Notch Face
                    DetectNotchFace();

                    // Save the part
                    Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);
                return false;
            }
        }


        private bool ShouldCreateNotch()
        {
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                string enableProbingStr = ConfigurationManager.AppSettings["MISUMI_ENABLE_PROBING"];
                if (enableProbingStr != null && int.TryParse(enableProbingStr, out int enableProbing) && enableProbing == 1)
                {
                    return true;
                }
            }

            return false;
        }


        private void RetrieveNotchParameters()
        {
            // Get Conversion factor
            double factor = m_part.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1.0;

            // Notch Width
            string notchWidthStr = ConfigurationManager.AppSettings["MISUMI_NOTCH_WIDTH"];
            if (notchWidthStr != null && Double.TryParse(notchWidthStr, out m_notchWidth))
            {
                if (m_notchWidth > 0.0)
                {
                    m_notchWidth *= factor;
                }
                else
                {
                    throw new Exception("Invalid notch width value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the notch width");
            }

            // Notch Depth
            string notchDepthStr = ConfigurationManager.AppSettings["MISUMI_NOTCH_DEPTH"];
            if (notchDepthStr != null && Double.TryParse(notchDepthStr, out m_notchDepth))
            {
                if (m_notchDepth > 0.0)
                {
                    m_notchDepth *= factor;
                }
                else
                {
                    throw new Exception("Invalid notch depth value");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the notch depth");
            }
        }


        private void DisplayPart()
        {
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_part, false, true, out PartLoadStatus status);
        }


        private void ComputeBlankBoundingBox()
        {
            // Create a temporary csys that will be used to compute the bounding box
            Point3d tmpOrigin = new Point3d();
            Vector3d tmpX = new Vector3d(1.0, 0.0, 0.0);
            Vector3d tmpY = new Vector3d(0.0, 1.0, 0.0);
            CartesianCoordinateSystem tmpCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(tmpOrigin, tmpX, tmpY);

            // Compute the bounding box of the Blank
            Body blankBody = m_part.Bodies.ToArray().First();
            m_boundingBox = BoundingBox.ComputeBodyBoundingBox(blankBody, tmpCsys);

            if (m_boundingBox == null)
            {
                throw new Exception("Unable to compute the Blank bounding box");
            }
        }


        private void CreateNotchProfile()
        {
            double X = m_boundingBox.XLength;
            double Y = m_boundingBox.YLength;
            double Z = m_boundingBox.ZLength;

            // Define notch points
            Point3d point1 = new Point3d(0.0, (Y + m_notchWidth) / 2.0, Z);
            Point3d point2 = new Point3d(0.0, (Y - m_notchWidth) / 2.0, Z);
            Point3d point3 = new Point3d(0.0, (Y - m_notchWidth) / 2.0, 0.0);
            Point3d point4 = new Point3d(0.0, (Y + m_notchWidth) / 2.0, 0.0);

            // Create notch lines
            Line line1 = m_part.Curves.CreateLine(point1, point2);
            Line line2 = m_part.Curves.CreateLine(point2, point3);
            Line line3 = m_part.Curves.CreateLine(point3, point4);
            Line line4 = m_part.Curves.CreateLine(point4, point1);

            // Add attribute on the created line
            Utilities.NXOpenUtils.SetAttribute(line1, "MISUMI", "NOTCH_CURVE", true);
            Utilities.NXOpenUtils.SetAttribute(line2, "MISUMI", "NOTCH_CURVE", true);
            Utilities.NXOpenUtils.SetAttribute(line3, "MISUMI", "NOTCH_CURVE", true);
            Utilities.NXOpenUtils.SetAttribute(line4, "MISUMI", "NOTCH_CURVE", true);

            // Create relevant points
            Point notchCenter = m_part.Points.CreatePoint(new Point3d(0.0, Y / 2.0, Z / 2.0));
            Point notchDepthCenter = m_part.Points.CreatePoint(new Point3d(m_notchDepth, Y / 2.0, Z / 2.0));
            Point notchTop = m_part.Points.CreatePoint(new Point3d(0.0, Y / 2.0, Z));
            Point probingTop = m_part.Points.CreatePoint(new Point3d(X / 2.0, Y / 2.0, Z));

            // Add attributes on the relevant points
            Utilities.NXOpenUtils.SetAttribute(notchCenter, "MISUMI", "NOTCH_CENTER", true);
            Utilities.NXOpenUtils.SetAttribute(notchDepthCenter, "MISUMI", "NOTCH_DEPTH_CENTER", true);
            Utilities.NXOpenUtils.SetAttribute(notchTop, "MISUMI", "NOTCH_TOP", true);
            Utilities.NXOpenUtils.SetAttribute(probingTop, "MISUMI", "PROBING_TOP", true);

            // Make the relevant points visible
            notchCenter.SetVisibility(SmartObject.VisibilityOption.Visible);
            notchDepthCenter.SetVisibility(SmartObject.VisibilityOption.Visible);
            notchTop.SetVisibility(SmartObject.VisibilityOption.Visible);
            probingTop.SetVisibility(SmartObject.VisibilityOption.Visible);
        }


        private void DetectNotchFace()
        {
            Face[] faces = m_part.Bodies.ToArray().First().GetFaces();
            Face notchFace = faces.Where(p => Utilities.NXOpenUtils.GetFaceVertices(p)
                                                                   .All(q => Utilities.MathUtils.IsNeighbour(q.X, 0.0))).FirstOrDefault();

            // Add attribute on the notch face
            if (notchFace != null)
            {
                Utilities.NXOpenUtils.SetAttribute(notchFace, "MISUMI", "NOTCH_FACE", true);
            }
        }


        private void Save()
        {
            m_part.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private Part m_part;
        private CAMAutomationManager m_manager;

        private double m_notchWidth;
        private double m_notchDepth;

        BoundingBox m_boundingBox;
    }
}
