using System;
using System.Configuration;
using System.Linq;

using NXOpen;
using NXOpen.Features;

namespace CAMAutomation
{
    public abstract class CoordinateSystemsCreator
    {
        public CoordinateSystemsCreator(Part part)
        {
            m_part = part;
            m_manager = CAMAutomationManager.GetInstance();

            m_context = GetType().Name;
        }


        ~CoordinateSystemsCreator()
        {
            // empty
        }


        public virtual bool Execute()
        {
            try
            {
                // Retrieve Clamping Extra Material
                RetrieveClampingExtraMaterial();

                // Displayed part
                DisplayPart();

                // Detect Body in part
                DetectBody();

                // Detect Hole Pattern
                DetectHolePatterns();

                // Check State
                CheckState();

                // Color Holes
                ColorHoles();

                // Create Coordinate Systems
                CreateCoordinateSystems();

                // Save the part
                Save();

                return true;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message, m_context);
                return false;
            }
        }


        protected virtual void RetrieveClampingExtraMaterial()
        {
            string clampingExtraMaterialStr = ConfigurationManager.AppSettings["MISUMI_CLAMPING_EXTRA_MATERIAL"];
            if (clampingExtraMaterialStr != null && double.TryParse(clampingExtraMaterialStr, out m_clampingExtraMaterial) && m_clampingExtraMaterial > 0.0)
            {
                // The value provided is in INCH. Make the conversion if necessary
                if (m_part.PartUnits == BasePart.Units.Millimeters)
                {
                    m_clampingExtraMaterial *= 25.4;
                }
            }
            else
            {
                m_clampingExtraMaterial = 0.0;
            }
        }
        

        protected virtual void DisplayPart()
        {
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_part, false, true, out PartLoadStatus status);
        }


        protected virtual void DetectBody()
        {
            Body[] bodies = m_part.Bodies.ToArray();
            if (bodies.Length != 1)
            {
                throw new Exception("Multiple bodies found in the part");
            }

            m_body = bodies.First();
        }


        protected virtual void DetectHolePatterns()
        {
            // Hole Distance in MM
            string holeDistanceStr = ConfigurationManager.AppSettings["MISUMI_HOLE_PATTERN_DISTANCE"];
            if (holeDistanceStr == null || !double.TryParse(holeDistanceStr, out double holeDistance) || holeDistance <= 0.0)
            {
                holeDistance = 15.0/25.4;  // Default value
            }
            
            // Compute the conversion factor
            double factor = m_part.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1.0;

            // Create the HolePatternsDetector object
            HolePatternsDetector holePatternsDetector = new HolePatternsDetector(m_part, holeDistance * factor);

            // Compute the hole patterns
            if (!holePatternsDetector.ComputePatterns(out string msg, out m_holePatterns))
            {
                throw new Exception(msg);
            }
        }


        protected abstract void CheckState();


        protected virtual void ColorHoles()
        {
            if (m_holePatterns != null && m_holePatterns.Length == 1)
            {
                foreach (Hole hole in m_holePatterns.First())
                {
                    if (hole.NXFace != null)
                    {
                        hole.NXFace.Color = 75;
                    }
                }
            }
        }


        protected abstract void CreateCoordinateSystems();


        protected virtual void Save()
        {
            m_part.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        protected virtual void ComputeFixtureCsysFromHolePattern(HolePattern holePattern, out Point3d origin, out Vector3d x, out Vector3d y, out Vector3d z)
        {
            // Retrive the first and last hole line in the hole pattern
            Utilities.Line firstHoleLine = holePattern.First().GetCenterLine();
            Utilities.Line lastHoleLine = holePattern.Last().GetCenterLine();

            // Define and create a temporary csys
            Vector3d tmpZ = firstHoleLine.Axis;
            Vector3d tmpX = Utilities.MathUtils.GetVector(firstHoleLine.Origin, lastHoleLine.Origin);
            Vector3d tmpY = Utilities.MathUtils.Cross(tmpZ, tmpX);
            CartesianCoordinateSystem tmpCSys = Utilities.NXOpenUtils.CreateTemporaryCsys(firstHoleLine.Origin, tmpY, tmpZ);

            // Compute the bounding box along that CSYS
            BoundingBox box = BoundingBox.ComputeBodyBoundingBox(m_body, tmpCSys);

            // Retrieve the centroid of the Body
            Point3d centroid = Utilities.NXOpenUtils.GetCentroid(m_body);

            // Compute origin location
            // Choose the origin to be on the the farthest hole axis from the centroid that intersect the closest box boundary to the centroid    
            // Start by finding the farthest hole axis from the centroid
            Utilities.Line originHoleLine = holePattern.OrderBy(p => Utilities.MathUtils.GetDistance(centroid, p.GetCenterLine())).Last().GetCenterLine();
            Utilities.Plane[] originPlaneCandidates = box.IntersectingBoundaries(originHoleLine);
            Utilities.Plane originPlane = originPlaneCandidates.OrderBy(p => Utilities.MathUtils.GetDistance(centroid, p)).First();
            Utilities.MathUtils.GetIntersection(originHoleLine, originPlane, out origin);

            // Define axis direction
            z = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Projection(Utilities.MathUtils.GetVector(centroid, origin), tmpZ));
            x = Utilities.MathUtils.UnitVector(Utilities.MathUtils.Projection(Utilities.MathUtils.GetVector(origin, centroid), tmpX));
            y = Utilities.MathUtils.Cross(z, x);
        }


        protected Part m_part;
        protected CAMAutomationManager m_manager;

        protected string m_context;

        protected Body m_body;

        protected HolePattern[] m_holePatterns;
      
        protected DatumCsys m_fixtureDatumCsys;
        protected DatumCsys m_clampingDatumCsys;
        protected DatumCsys m_clampingDatumCsys2;
        protected DatumCsys m_clampingDatumCsysNearNetBlank;

        protected double m_clampingExtraMaterial;
    }
}