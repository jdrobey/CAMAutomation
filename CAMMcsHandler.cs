using System;

using NXOpen;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class CAMMcsHandler
    {
        public static OrientGeometry CreateMcs(string name, CartesianCoordinateSystem mcsCsys, int offset, bool allAxes)
        {
            Part part = Utilities.RemoteSession.NXSession.Parts.Work;

            // Does the Mcs already exist (in the Roughing Template for example) ?
            OrientGeometry newMcs = Utilities.NXOpenUtils.GetMcsByName(part, name);

            // Create a new Mcs if not found
            if (newMcs == null)
            {
                NCGroup parent = part.CAMSetup.CAMGroupCollection.FindObject("GEOMETRY");

                NCGroup newGeometry = part.CAMSetup.CAMGroupCollection.CreateGeometry(parent, "mill_contour", "MCS", NCGroupCollection.UseDefaultName.False, name);
                newMcs = newGeometry as OrientGeometry;
            }

            // Set Mcs csys, offset and allAxes option
            if (newMcs != null)
            {
                EditMcs(newMcs, mcsCsys, offset, allAxes);
            }

            return newMcs;
        }


        public static void EditMcs(OrientGeometry mcs, CartesianCoordinateSystem mcsCsys, int offset, bool allAxes)
        {
            CAMMcsHandler mcsHandler = new CAMMcsHandler(mcs);

            mcsHandler.SetCsys(mcsCsys);
            mcsHandler.SetOffset(offset);
            mcsHandler.SetAllAxes(allAxes);
        }


        public CAMMcsHandler(OrientGeometry mcs)
        {
            m_mcs = mcs;
            m_workPart = mcs.OwningPart as Part;
        }


        ~CAMMcsHandler()
        {
            // empty
        }


        public void SetCsys(CartesianCoordinateSystem csys)
        {
            // Create the Builder
            OrientGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(m_mcs);

            try
            {
                // Copy csys
                // Csys should be copying using Xform
                // Creating csys with traditional methods does not work (NX BUG)
                Point3d origin = csys.Origin;
                Vector3d xVector = new Vector3d(csys.Orientation.Element.Xx, csys.Orientation.Element.Xy, csys.Orientation.Element.Xz);
                Vector3d yVector = new Vector3d(csys.Orientation.Element.Yx, csys.Orientation.Element.Yy, csys.Orientation.Element.Yz);
                Xform xform = m_workPart.Xforms.CreateXform(origin, xVector, yVector, SmartObject.UpdateOption.WithinModeling, 1.0);
                CartesianCoordinateSystem copyCsys = m_workPart.CoordinateSystems.CreateCoordinateSystem(xform, SmartObject.UpdateOption.AfterModeling);
                
                // Set the copy csys
                builder.Mcs = copyCsys;

                // Commit the builder
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


        public void SetOffset(int offset)
        {
            // Create the Builder
            OrientGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(m_mcs);

            try
            {
                // Set the Csys
                builder.FixtureOffsetBuilder.Value = offset;

                // Commit the builder
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


        public void SetAllAxes(bool allAxes)
        {
            // Create the Builder
            OrientGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(m_mcs);

            try
            {
                // Set Tool Axis Mode
                if (allAxes)
                {
                    builder.SetToolAxisMode(OrientGeomBuilder.ToolAxisModes.AllAxes);
                }
                else
                {
                    builder.SetToolAxisMode(OrientGeomBuilder.ToolAxisModes.PositiveZOfMcs);
                }

                // Commit the builder
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


        private OrientGeometry m_mcs;
        private Part m_workPart;
    }
}
