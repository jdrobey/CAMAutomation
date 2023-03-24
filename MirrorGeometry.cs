using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NXOpen;
using NXOpen.Features;

namespace CAMAutomation
{
    public class MirrorGeometry
    {
        public enum MirrorPlane { XY = 0, XZ = 1, YZ = 2 }

        public MirrorGeometry(Part geometry)
        {
            m_geometry = geometry;
        }

        ~MirrorGeometry()
        {
            // empty
        }

        public bool Mirror(MirrorPlane plane)
        {
            m_mirrorPlane = plane;

            try
            {
                // Set the Geometry as the displayed part
                MakeGeometryDisplayPart();

                // Retrieve Initial Bodies
                RetrieveInitialBodies();

                // Mirror the Bodies in the Geometry
                MirrorBodies();

                // Delete Initial Bodies
                DeleteInitialBodies();

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


        private void RetrieveInitialBodies()
        {
            m_initialBodies = m_geometry.Bodies.ToArray();
        }


        private void MirrorBodies()
        {
            GeomcopyBuilder builder = m_geometry.Features.CreateGeomcopyBuilder(null);

            try
            {
                // Create the Builder
                builder = m_geometry.Features.CreateGeomcopyBuilder(null);

                // Type
                builder.Type = GeomcopyBuilder.TransformTypes.Mirror;

                // Specify the bodies
                builder.GeometryToInstance.Add(m_initialBodies);

                // Specify the plane
                Point3d origin = new Point3d();
                Vector3d normal = GetPlaneNormal();
                builder.MirrorPlane = m_geometry.Planes.CreatePlane(origin, normal, SmartObject.UpdateOption.WithinModeling);

                // Options
                builder.Associative = false;
                builder.CopyThreads = false;
                builder.HideOriginal = true;

                // Commit the builder
                builder.CommitFeature();

                // Destroy the builder
                builder.Destroy();
            }
            catch (Exception)
            {
                // Destroy the builder
                builder.Destroy();

                throw;
            }
        }


        private void DeleteInitialBodies()
        {
            Utilities.NXOpenUtils.DeleteNXObjects(m_initialBodies);
        }


        private void SaveGeometry()
        {
            // Save the Geometry
            m_geometry.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private Vector3d GetPlaneNormal()
        {
            if (m_mirrorPlane == MirrorPlane.XY)
            {
                return new Vector3d(0.0, 0.0, 1.0);
            }
            else if (m_mirrorPlane == MirrorPlane.XZ)
            {
                return new Vector3d(0.0, 1.0, 0.0);
            }
            else
            {
                return new Vector3d(1.0, 0.0, 0.0);
            }
        }


        private Part m_geometry;
        private MirrorPlane m_mirrorPlane;
        private Body[] m_initialBodies;

    }
}
