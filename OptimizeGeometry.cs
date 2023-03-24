using System;
using System.Linq;
using System.Configuration;

using NXOpen;
using NXOpen.Features;

namespace CAMAutomation
{
    public class OptimizeGeometry
    {
        public OptimizeGeometry(Part geometry)
        {
            m_geometry = geometry;
        }


        ~OptimizeGeometry()
        {
            // empty
        }


        public bool Optimize()
        {
            try
            {
                // Set the Geometry as the displayed part
                MakeGeometryDisplayPart();

                // Optimize Faces
                OptimizeFaces();

                // Save Geometry
                SaveGeometry();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        private void MakeGeometryDisplayPart()
        {
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_geometry, false, true, out PartLoadStatus status);
        }


        private void OptimizeFaces()
        {
            OptimizeFaceBuilder builder = m_geometry.Features.CreateOptimizeFaceBuilder();

            try
            {
                // Set the faces
                Face[] faces = m_geometry.Bodies.ToArray().SelectMany(p => p.GetFaces()).ToArray();
                FaceDumbRule rule = (m_geometry as BasePart).ScRuleFactory.CreateRuleFaceDumb(faces);
                builder.FacesToOptimize.ReplaceRules(new SelectionIntentRule[] { rule }, false);

                // Tolerance
                string toleranceStr = ConfigurationManager.AppSettings["MISUMI_OPTIMIZE_GEOMETRY_TOLERANCE"];
                if (toleranceStr == null || !Double.TryParse(toleranceStr, out double tolerance))
                {
                    tolerance = 0.002;
                }
                double factor = m_geometry.PartUnits == BasePart.Units.Inches ? 1.0 : 25.4;
                builder.DistanceTolerance = tolerance * factor;

                // Options
                builder.CleanBody = true;
                builder.Report = false;

                // Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the builder
                builder.Destroy();
            }
        }


        private void SaveGeometry()
        {
            // Save the Geometry
            m_geometry.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private Part m_geometry;
    }
}
