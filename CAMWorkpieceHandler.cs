using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.CAM;
using NXOpen.Assemblies;


namespace CAMAutomation
{
    public class CAMWorkpieceHandler
    {
        public static FeatureGeometry CreateWorkpiece(string name, OrientGeometry parentMcs, FeatureGeometry sourceWorkpiece, Component component, Body[] checkGeometry)
        {
            // Does the workpiece already exist (in the Roughing Template for example) ?
            FeatureGeometry newWorkpiece = parentMcs.GetMembers().FirstOrDefault() as FeatureGeometry;

            // If found, set the workpiece name
            // If not, create a new workpiece
            if (newWorkpiece != null)
            {
                if (newWorkpiece.Name != name)
                {
                    newWorkpiece.SetName(name);
                }
            }
            else
            {
                // Create the new workpiece under the same parent Mcs
                Part part = Utilities.RemoteSession.NXSession.Parts.Work;
                NCGroup newGeometry = part.CAMSetup.CAMGroupCollection.CreateGeometry(parentMcs, "mill_contour", "WORKPIECE", NCGroupCollection.UseDefaultName.False, name);
                newWorkpiece = newGeometry as FeatureGeometry;
            }

            if (newWorkpiece != null)
            {
                EditWorkpiece(newWorkpiece, sourceWorkpiece, component, checkGeometry);               
            }

            return newWorkpiece;
        }


        public static void EditWorkpiece(FeatureGeometry workpiece, Component blank, Component component, Body[] checkGeometry)
        {
            // Create a Handler for the new  workpiece
            CAMWorkpieceHandler workpieceHandler = new CAMWorkpieceHandler(workpiece);

            // Assign Blank objects
            workpieceHandler.SetBlankBodies(Utilities.NXOpenUtils.GetComponentBodies(blank)); ;

            // Assign CAD objects
            workpieceHandler.SetCADBodies(Utilities.NXOpenUtils.GetComponentBodies(component));

            // Assign Fixture objects
            workpieceHandler.SetCheckBodies(checkGeometry);
        }


        public static void EditWorkpiece(FeatureGeometry workpiece, FeatureGeometry sourceWorkpiece, Component component, Body[] checkGeometry)
        {
            // Create a Handler for the new  workpiece
            CAMWorkpieceHandler workpieceHandler = new CAMWorkpieceHandler(workpiece);

            // Assign CAD objects
            workpieceHandler.SetCADBodies(Utilities.NXOpenUtils.GetComponentBodies(component));

            // Assign Fixture objects
            workpieceHandler.SetCheckBodies(checkGeometry);

            // Set the Blank as IPW
            workpieceHandler.SetBlankIPW(sourceWorkpiece);

            // Update the Blank IPW
            workpieceHandler.UpdateBlankIPW();
        }


        public CAMWorkpieceHandler(FeatureGeometry workpiece)
        {
            m_workpiece = workpiece;
            m_workPart = workpiece.OwningPart as Part;
        }


        ~CAMWorkpieceHandler()
        {
            // empty
        }


        public void SetCADBodies(Body[] bodies)
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                SetBodies(builder.PartGeometry, bodies);

                //  Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public void SetBlankBodies(Body[] bodies)
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Specify Blank Definition
                builder.BlankGeometry.BlankDefinitionType = GeometryGroup.BlankDefinitionTypes.FromGeometry;

                SetBodies(builder.BlankGeometry, bodies);

                //  Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public void SetCheckBodies(Body[] bodies)
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                SetBodies(builder.CheckGeometry, bodies);

                //  Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        private void SetBodies(Geometry geometry, Body[] bodies)
        {
            // Delete all the GeometrySet
            geometry.GeometryList.Clear(ObjectList.DeleteOption.Delete);

            // Create a unique GeometrySet
            GeometrySet geometrySet = geometry.CreateGeometrySet();
            geometry.GeometryList.Append(geometrySet);

            // Create the body rules and set it in the ScCollector
            // A rule will be created for each body separately
            // Create one rule for all bodies result in a Bug in NX
            List<SelectionIntentRule> rules = new List<SelectionIntentRule>();
            foreach (Body body in bodies)
            {
                BodyDumbRule rule = (m_workPart as BasePart).ScRuleFactory.CreateRuleBodyDumb(new Body[] { body }, true);
                rules.Add(rule);
            }
            geometrySet.ScCollector.ReplaceRules(rules.ToArray(), false);
        }


        public Body[] GetCADBodies()
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Get the Bodies
                Body[] bodies = GetBodies(builder.PartGeometry);

                return bodies;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public Body[] GetBlankBodies()
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Get the Bodies
                Body[] bodies = GetBodies(builder.BlankGeometry);

                return bodies;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public Body[] GetCheckBodies()
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Get the Bodies
                Body[] bodies = GetBodies(builder.CheckGeometry);

                return bodies;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        private Body[] GetBodies(Geometry geometry)
        {
            List<Body> bodies = new List<Body>();

            for (int i = 0; i < geometry.GeometryList.Length; ++i)
            {
                TaggedObject[] objects = geometry.GeometryList.FindItem(i).GetItems();
                foreach (TaggedObject obj in objects)
                {
                    Body body = obj as Body;
                    if (body != null)
                    {
                        bodies.Add(body);
                    }
                }
            }

            return bodies.ToArray();
        }


        public void SetBlankIPW(FeatureGeometry sourceWorkpiece)
        {
            Part sourcePart = sourceWorkpiece.OwningPart as Part;

            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Set the Blank as IPW
                builder.BlankGeometry.BlankDefinitionType = GeometryGroup.BlankDefinitionTypes.Ipw;
                builder.BlankGeometry.BlankIpw.SetSource(sourcePart.Name, sourceWorkpiece.Name);

                // Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public void SetBlankIPWCsys(CartesianCoordinateSystem csys)
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                // Make a temporary copy of the cartesian csys so the workpiece will use that copy
                CartesianCoordinateSystem copyCsys = m_workPart.CoordinateSystems.CreateCoordinateSystem(csys.Origin, csys.Orientation, true);

                // Set the copy csys
                builder.BlankGeometry.IpwPositionType = GeometryGroup.PositionTypes.Coordinate;
                builder.BlankGeometry.Csys = copyCsys;
                builder.BlankGeometry.IpwPositionCsys = copyCsys;

                // Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        public void UpdateBlankIPW()
        {
            // Create the Builder
            MillGeomBuilder builder = m_workPart.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(m_workpiece);

            try
            {
                if (builder.BlankGeometry.BlankDefinitionType == GeometryGroup.BlankDefinitionTypes.Ipw)
                {
                    builder.BlankGeometry.BlankIpw.Update();

                    // Commit the Builder
                    builder.Commit();
                }
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                // Destroy the Builder
                builder.Destroy();
            }
        }


        private FeatureGeometry m_workpiece;
        private Part m_workPart;
    }
}
