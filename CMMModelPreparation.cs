using System;
using System.Linq;
using System.IO;

using NXOpen;

namespace CAMAutomation
{
    public class CMMModelPreparation
    {
        public CMMModelPreparation(Part cadPart)
        {
            m_cadPart = cadPart;
            m_manager = CAMAutomationManager.GetInstance();           
        }


        ~CMMModelPreparation()
        {
            // empty
        }


        public bool PrepareCMMModel()
        {
            // Display Part
            DisplayPart();

            // Add an undo mark
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, "PrepareCMMModel");

            try
            {
                // Retrieve CAD Body
                RetrieveCADBody();

                // Move CAD Body
                MoveCADBody();
          
                // Create STEP file
                CreateSTEPFile();

                return true;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                return false;
            }
            finally
            {
                // Undo the CMM Model Preparation
                Utilities.RemoteSession.NXSession.UndoToMark(undoMark, "PrepareCMMModel");
            }
        }


        private void DisplayPart()
        {
            Utilities.RemoteSession.NXSession.Parts.SetDisplay(m_cadPart, false, true, out PartLoadStatus status);
        }

   
        private void RetrieveCADBody()
        {
            Body[] bodies = m_cadPart.Bodies.ToArray();
            if (bodies.Length != 1)
            {
                throw new Exception("Multiple bodies found in the CAD part");
            }

            m_cadBody = bodies.First();
        }


        private void MoveCADBody()
        {
            // Get Source and Target Csys
            // Source Csys is the CAD_CSYS_FIXTURE
            // Target Csys is the absolute coordinate system
            CartesianCoordinateSystem sourceCsys = Utilities.NXOpenUtils.GetCsysByName(m_cadPart, "CAD_CSYS_FIXTURE");
            CartesianCoordinateSystem targetCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(new Point3d(), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0));

            if (!Utilities.NXOpenUtils.MoveBody(m_cadBody, sourceCsys, targetCsys))
            {
                throw new Exception("Unable to move the CAD body");
            }
        }


        private void CreateSTEPFile()
        {
            StepCreator builder = Utilities.RemoteSession.NXSession.DexManager.CreateStepCreator();

            try
            {
                // Options
                builder.ExportAs = StepCreator.ExportAsOption.Ap214;
                builder.ExportFrom = StepCreator.ExportFromOption.DisplayPart;
                builder.OutputFile = Path.Combine(Path.GetDirectoryName(m_cadPart.FullPath), m_cadPart.Leaf + "_CMM.stp");            
                builder.SettingsFile = Path.Combine(Environment.GetEnvironmentVariable("UGII_BASE_DIR"), "step214ug", "ugstep214.def");
                builder.ProcessHoldFlag = true;

                // Objects to export
                builder.ObjectTypes.Surfaces = true;
                builder.ObjectTypes.Solids = true;
                builder.ObjectTypes.Curves = true;
                builder.ObjectTypes.Csys = true;
                builder.ObjectTypes.ProductData = true;
                builder.ObjectTypes.PmiData = true;

                // Commit the Builder
                builder.Commit();
            }
            catch (Exception)
            {
                throw new Exception("Unable to create STEP File");
            }
            finally
            {
                // Destroy the builder
                builder.Destroy();
            }
        }


        private Part m_cadPart;
        private Body m_cadBody;

        private CAMAutomationManager m_manager;
    }
}
