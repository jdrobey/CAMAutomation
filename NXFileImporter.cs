using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

using NXOpen;
using NXOpen.Assemblies;


namespace CAMAutomation
{
    public static class ImporterFactory
    {
        public static Importer CreateImporter(string file, string fileFormat = "")
        {
            var m_manager = CAMAutomationManager.GetInstance();
            string extension = Path.GetExtension(file);     
            
            if (extension.ToLower() == ".prt")
            {
                return new NXImporter(file);
            }
            else if (extension.ToLower() == ".iges" || extension.ToLower() == ".igs")
            {
                return new IGESImporter(file);
            }
            else if (extension.ToLower() == ".stp" || extension.ToLower() == ".step")
            {
                // Check the type specified by the user
                Importer.Type type = Importer.GetTypeFromString(fileFormat);
                m_manager.LogFile.AddMessage($"Step type: {type}");
                if (type == Importer.Type.STEP203)
                {
                    return new STEP203Importer(file);
                }
                else if (type == Importer.Type.STEP214)
                {
                    return new STEP214Importer(file);
                }
                else if (type == Importer.Type.STEP242)
                {
                    return new STEP242Importer(file);
                }
            }
            else if (extension.ToLower() == ".sat")
            {
                return new SATImporter(file);
            }
            else if (extension.ToLower() == ".sldprt")
            {
                return new SLDPRTImporter(file);
            }
            else if (extension.ToLower() == ".par")
            {
                return new PARImporter(file);
            }

            return null;
        }
    }


    public abstract class Importer
    {
        public enum Type { INVALID = -1, NX = 0, IGES = 1, STEP203 = 2 , STEP214 = 3, STEP242 = 4, SAT = 5, SLDPRT = 6, PAR = 7}

        public Importer(Type type, string file)
        {
            m_type = type;
            m_file = file;

            m_manager = CAMAutomationManager.GetInstance();
        }

        ~Importer()
        {
            // empty
        }

        public abstract Part Import();

        public static Type GetTypeFromString(string type)
        {
            if (type == "NX")
            {
                return Type.NX;
            }
            else if (type == "IGES")
            {
                return Type.IGES;
            }
            else if (type == "STEP203")
            {
                return Type.STEP203;
            }
            else if (type == "STEP214")
            {
                return Type.STEP214;
            }
            else if (type == "STEP242")
            {
                return Type.STEP242;
            }
            else if (type == "SAT")
            {
                return Type.SAT;
            }
            else if (type == "SLDPRT")
            {
                return Type.SLDPRT;
            }
            else if (type == "PAR")
            {
                return Type.PAR;
            }
            else
            {
                return Type.INVALID;
            }
        }


        protected string GetNewFileName()
        {
            string outputDirectory = Path.GetFullPath(m_manager.OutputDirectory);
            string newFileName = outputDirectory + "\\" + Path.GetFileNameWithoutExtension(m_file) + ".prt";

            return newFileName;
        }

        protected Type m_type;
        protected string m_file;

        protected CAMAutomationManager m_manager;
    }

    public abstract class NativeImporter : Importer
    {
        public NativeImporter(Type type, string file) : base(type, file)
        {
            // empty
        }

        ~NativeImporter()
        {
            // empty
        }

        public override abstract Part Import();
    }

    public abstract class OtherImporter : Importer
    {
        public OtherImporter(Type type, string file) : base(type, file)
        {
            // empty
        }

        ~OtherImporter()
        {
            // empty
        }

        public override Part Import()
        {
            Part newPart = null;

            try
            {
                // Get the the new file name with a .prt extension
                string newFileName = GetNewFileName();

                // Create the part
                newPart = Utilities.NXOpenUtils.CreateNewCADPart(newFileName, false, BasePart.Units.Millimeters);

                if (newPart != null)
                {
                    // Import the file as part
                    // If success, save the part
                    if (ImportAsPart())
                    {
                        newPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
                    }
                    else
                    {
                        throw new Exception("Unable to import the part");
                    }
                }
                else
                {
                    throw new Exception("Unable to create a new part");
                }
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                newPart = null;
            }

            return newPart;
        }

        protected abstract bool ImportAsPart();
    }

    public class NXImporter : NativeImporter
    {
        public NXImporter(string file) : base(Importer.Type.NX, file)
        {
            // empty
        }

        ~NXImporter()
        {
            // empty
        }

        public override Part Import()
        {
            Part newPart = null;

            try
            {
                if (Utilities.NXOpenUtils.ClonePart(m_file, m_manager.OutputDirectory))
                {
                    // Get the new name of the part 
                    string newFileName = GetNewFileName();

                    newPart = Utilities.RemoteSession.NXSession.Parts.OpenDisplay(newFileName, out PartLoadStatus status);
                }
                else
                {
                    throw new Exception("Unable to import the part");
                }
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);
                return null;
            }

            return newPart;
        }
    }


    public class SLDPRTImporter : NativeImporter
    {
        public SLDPRTImporter(string file) : base(Importer.Type.SLDPRT, file)
        {
            // empty
        }

        ~SLDPRTImporter()
        {
            // empty
        }

        public override Part Import()
        {
            Part newPart = null;

            try
            {
                // Open the SLDPRT file 
                Part part = Utilities.RemoteSession.NXSession.Parts.Open(m_file, out PartLoadStatus loadStatus);

                // Get the new name of the part 
                string cadFileName = GetNewFileName();

                // Is it an assembly ?
                Component rootComponent = part.ComponentAssembly.RootComponent;
                if (rootComponent != null)
                {
                    // Select the component that contains one single body
                    Component solidComponent = rootComponent.GetChildren().Where(p => Utilities.NXOpenUtils.GetComponentBodies(p).Length == 1).FirstOrDefault();

                    if (solidComponent != null)
                    {
                        // Retrieve the solid part and save it
                        Part solidPart = solidComponent.Prototype as Part;
                        solidPart.SaveAs(cadFileName);

                        // Close the opened part
                        part.Close(BasePart.CloseWholeTree.True, BasePart.CloseModified.CloseModified, null);

                        // Reopen the single part
                        newPart = Utilities.RemoteSession.NXSession.Parts.OpenDisplay(cadFileName, out PartLoadStatus status);
                    }
                }
                else
                {
                    part.SaveAs(cadFileName);
                    newPart = part;
                }
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);
                return null;
            }

            return newPart;
        }
    }

    public class PARImporter : NativeImporter
    {
        public PARImporter(string file) : base(Importer.Type.PAR, file)
        {
            // empty
        }

        ~PARImporter()
        {
            // empty
        }

        public override Part Import()
        {
            Part newPart = null;

            try
            {
                newPart = Utilities.RemoteSession.NXSession.Parts.OpenDisplay(m_file, out PartLoadStatus status);

                if (newPart != null)
                {
                    string cadFileName = GetNewFileName();
                    newPart.SaveAs(cadFileName);
                }
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);
                return null;
            }

            return newPart;
        }
    }

    public class IGESImporter : OtherImporter
    {
        public IGESImporter(string file) : base(Importer.Type.IGES, file)
        {
            // empty
        }

        ~IGESImporter()
        {
            // empty
        }

        protected override bool ImportAsPart()
        {
            bool success = false;
            IgesImporter importer = Utilities.RemoteSession.NXSession.DexManager.CreateIgesImporter();

            try
            {
                // Options
                importer.ImportTo = IgesImporter.ImportToEnum.WorkPart;
                importer.SettingsFile = Environment.GetEnvironmentVariable("UGII_BASE_DIR") + "\\iges\\igesimport.def";
                importer.FileOpenFlag = false;
                importer.CopiousData = IgesImporter.CopiousDataEnum.LinearNURBSpline;
                importer.SimplifyGeometry = true;
                importer.SewSurfaces = true;
                importer.Optimize = true;
                importer.SmoothBSurf = true;
                importer.LayerDefault = 1;
                importer.LayerMask = "0-99999";

                // What to import
                importer.ObjectTypes.Curves = true;
                importer.ObjectTypes.Surfaces = true;
                importer.ObjectTypes.Solids = true;
                importer.ObjectTypes.Annotations = true;
                importer.ObjectTypes.Structures = true;
                importer.ObjectTypes.Csys = true;

                // Input/Output file
                importer.InputFile = m_file;
                importer.OutputFile = GetNewFileName();

                // Commit the builder
                importer.Commit();

                success = true;
            }
            catch (Exception)
            {
                success = false;
            }

            // Destroy the builder
            importer.Destroy();

            return success;
        }
    }


    public class STEP203Importer : OtherImporter
    {
        public STEP203Importer(string file) : base(Importer.Type.STEP203, file)
        {
            // empty
        }

        ~STEP203Importer()
        {
            // empty
        }

        protected override bool ImportAsPart()
        {
            bool success = false;
            Step203Importer importer = Utilities.RemoteSession.NXSession.DexManager.CreateStep203Importer();

            try
            {
                // Options
                importer.ImportTo = Step203Importer.ImportToOption.WorkPart;
                importer.SettingsFile = Environment.GetEnvironmentVariable("UGII_BASE_DIR") + "\\step203ug\\step203ug.def";
                importer.FileOpenFlag = false;
                importer.SimplifyGeometry = true;
                importer.SewSurfaces = true;
                importer.SmoothBSurfaces = true;
                importer.LayerDefault = 1;

                // What to import
                importer.ObjectTypes.Curves = false;
                importer.ObjectTypes.Surfaces = false;
                importer.ObjectTypes.Solids = true;
                importer.ObjectTypes.Csys = false;
                importer.ObjectTypes.ProductData = false;
                importer.ObjectTypes.PmiData = false;

                // Input/Output file
                importer.InputFile = m_file;
                importer.OutputFile = GetNewFileName();

                // Commit the builder
                importer.Commit();

                success = true;
            }
            catch (Exception)
            {
                success = false;
            }

            // Destroy the builder
            importer.Destroy();

            return success;
        }
    }


    public class STEP214Importer : OtherImporter
    {
        public STEP214Importer(string file) : base(Importer.Type.STEP214, file)
        {
            // empty
        }

        ~STEP214Importer()
        {
            // empty
        }

        protected override bool ImportAsPart()
        {
            bool success = false;
            Step214Importer importer = Utilities.RemoteSession.NXSession.DexManager.CreateStep214Importer();

            try
            {
                // Options
                importer.ImportTo = Step214Importer.ImportToOption.WorkPart;
                importer.SettingsFile = Environment.GetEnvironmentVariable("UGII_BASE_DIR") + "\\step214ug\\step214ug.def";
                importer.FileOpenFlag = false;
                importer.SimplifyGeometry = true;
                importer.SewSurfaces = true;
                importer.SmoothBSurfaces = true;
                importer.LayerDefault = 1;

                // What to import
                importer.ObjectTypes.Curves = false;
                importer.ObjectTypes.Surfaces = false;
                importer.ObjectTypes.Solids = true;
                importer.ObjectTypes.Csys = false;
                importer.ObjectTypes.ProductData = false;
                importer.ObjectTypes.PmiData = false;

                // Input/Output file
                importer.InputFile = m_file;
                importer.OutputFile = GetNewFileName();

                // Commit the builder
                importer.Commit();

                success = true;
            }
            catch (Exception)
            {
                success = false;
            }

            // Destroy the builder
            importer.Destroy();

            return success;
        }
    }


    public class STEP242Importer : OtherImporter
    {
        public STEP242Importer(string file) : base(Importer.Type.STEP242, file)
        {
            // empty
        }

        ~STEP242Importer()
        {
            // empty
        }

        protected override bool ImportAsPart()
        {
            bool success = false;
            Step242Importer importer = Utilities.RemoteSession.NXSession.DexManager.CreateStep242Importer();

            try
            {
                // Options
                importer.ImportTo = Step242Importer.ImportToOption.WorkPart;
                importer.SettingsFile = Environment.GetEnvironmentVariable("UGII_BASE_DIR") + "\\translators\\step242\\step242ug.def";
                importer.FileOpenFlag = false;
                importer.SimplifyGeometry = true;
                importer.SewSurfaces = true;
                importer.SmoothBSurfaces = true;
                importer.Messages = Step242Importer.MessageEnum.Error;

                // What to import
                importer.ObjectTypes.Curves = false;
                importer.ObjectTypes.Surfaces = false;
                importer.ObjectTypes.Solids = true;
                importer.ObjectTypes.Csys = false;
                importer.ObjectTypes.ProductData = false;
                importer.ObjectTypes.PmiData = false;

                // Input/Output file
                importer.InputFile = m_file;
                importer.OutputFile = GetNewFileName();

                // Commit the builder
                importer.Commit();

                success = true;
            }
            catch (Exception)
            {
                success = false;
            }

            // Destroy the builder
            importer.Destroy();

            return success;
        }
    }


    public class SATImporter : OtherImporter
    {
        public SATImporter(string file) : base(Importer.Type.SAT, file)
        {
            // empty
        }

        ~SATImporter()
        {
            // empty
        }

        protected override bool ImportAsPart()
        {
            // Retrieve the current working directory
            // Change the current working directory, so the Importer LogFile will be written in the output directory.
            // This is du to the fact that the AcisImporter does not have the OutputFile property like the other importer
            // The working directory will be restored after import
            string workingDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(m_manager.OutputDirectory);

            bool success = false;
            AcisImporter importer = Utilities.RemoteSession.NXSession.DexManager.CreateAcisImporter();

            try
            {               
                // Options
                importer.SettingsFile = Environment.GetEnvironmentVariable("UGII_BASE_DIR") + "\\translators\\nxacis\\nxacis.def";
                importer.FileOpenFlag = false;
                importer.SimplifyGeometry = true;
                importer.Sew = true;
                importer.HealBodies = true;
                importer.Optimize = true;
                importer.IncludeWires = true;

                // Input file
                importer.InputFile = m_file;

                // Commit the builder
                importer.Commit();

                success = true;
            }
            catch (Exception)
            {
                success = false;
            }

            // Destroy the builder
            importer.Destroy();

            // Restore the output directory
            Directory.SetCurrentDirectory(workingDir);

            return success;
        }
    }
}


