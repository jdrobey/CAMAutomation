using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.Assemblies;

namespace CAMAutomation
{
    public class CAMProbePointCreator
    {
        public CAMProbePointCreator(Component blankComponent, Component cadComponent)
        {
            m_blankComponent = blankComponent;
            m_cadComponent = cadComponent;
            m_cadPart = cadComponent.Prototype as Part;
        }


        public bool FindAndCreateProbePoints(out Point3d[] probePoints, out string msg)
        {
            try
            {
                // Find the coordinate of the probe points
                FindProbePoints();

                // Create the probe points
                CreateProbePoints();

                probePoints = m_probePoints;
                msg = String.Empty;

                return true;
            }
            catch(Exception ex)
            {
                probePoints = null;
                msg = ex.Message;

                return false;
            }
        }


        private void FindProbePoints()
        {
            // Retrieve the body in the Blank component
            Body blankBody = Utilities.NXOpenUtils.GetComponentBodies(m_blankComponent).First();

            // Find all edge vertices of the body
            Point3d[] pointsOnEdge = GetPointsOnEdge(blankBody);

            // Compute the bounding box of the body
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(blankBody);

            // Keep only points that are on the bounding box
            // Apply transformation to those points, so there coordinates are expressed in the CAD absolute coordinate system
            m_probePoints = pointsOnEdge.Where(p => boundingBox.IsPointInBox(p)).Select(p => ChangeCoordinateSystems(p)).ToArray();

            // Assembly part (i.e., m_blankComponent.OwningPart) and CAD part may be in different unit system 
            // We need to retrieve the conversion factor and convert the probe points in CAD unit system
            double factor = Utilities.NXOpenUtils.GetConversionFactor(m_blankComponent.OwningPart, m_cadPart);
            m_probePoints = m_probePoints.Select(p => Utilities.MathUtils.Multiply(p, factor)).ToArray();
        }


        private Point3d[] GetPointsOnEdge(Body body)
        {
            List<Point3d> pointsOnEdge = new List<Point3d>();

            foreach (Face face in body.GetFaces())
            {
                foreach (Edge edge in face.GetEdges())
                {
                    // We only consider linear edges
                    if (edge.SolidEdgeType == Edge.EdgeType.Linear)
                    {
                        edge.GetVertices(out Point3d p1, out Point3d p2);

                        // Check if p1 already exist
                        bool p1Exists = pointsOnEdge.Any(p => Utilities.MathUtils.AreCoincident(p, p1));                    
                        if (!p1Exists)
                        {
                            pointsOnEdge.Add(p1);
                        }

                        // Check if p2 already exist
                        bool p2Exists = pointsOnEdge.Any(p => Utilities.MathUtils.AreCoincident(p, p2)); ;
                        if (!p2Exists)
                        {
                            pointsOnEdge.Add(p2);
                        }
                    }
                }
            }

            return pointsOnEdge.ToArray();
        }


        private Point3d ChangeCoordinateSystems(Point3d initialPoint)
        {
            m_cadComponent.GetPosition(out Point3d cadPosition, out Matrix3x3 cadOrientation);

            return Utilities.MathUtils.Multiply(cadOrientation, Utilities.MathUtils.Substract(initialPoint, cadPosition));
        }


        private void CreateProbePoints()
        {
            // Load CAD fully
            m_cadPart.LoadFully();

            // Create the probe points in the CAD
            foreach (Point3d coordinates in m_probePoints)
            {
                // Create the point
                Point point = m_cadPart.Points.CreatePoint(coordinates);

                // Make the point visible
                point.SetVisibility(SmartObject.VisibilityOption.Visible);

                // Set attribute
                Utilities.NXOpenUtils.SetAttribute(point, "MISUMI", "Probe_Point", true);
            }
        }


        private Component m_blankComponent;
        private Component m_cadComponent;
        private Part m_cadPart;

        private Point3d[] m_probePoints;
    }
}
