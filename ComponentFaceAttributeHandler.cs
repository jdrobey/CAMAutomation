using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;
using static System.Math;
using static CAMAutomation.CAMFaceAnalyzer;


namespace CAMAutomation
{
    public class ComponentFaceAttributeHandler
    {


        public void BuildAttributes(PLANE face )
        {
            Face aFace = face.Face;

            WriteAttribute(aFace, face.Dir_I, "i");
            WriteAttribute(aFace, face.Dir_J, "j");
            WriteAttribute(aFace, face.Dir_K, "k");

            if (face.Dir_K == 1)
            {
                WriteAttribute(aFace, "Floor", "FeatureClass");
                // Color Face "Deep Sky" - light blue
                aFace.Color = 205;
                aFace.RedisplayObject();

            }
            if (face.Dir_K == -1)
            {
                string setup = "2";
                WriteAttribute(aFace, "Floor", "FeatureClass");
                WriteAttribute(aFace, setup, "Setup");
                // Color Face "Deep Sky" - light Blue
                aFace.Color = 205;
                aFace.RedisplayObject();
            }
            if (face.Dir_K == 0)
            {
                WriteAttribute(aFace, "Wall", "FeatureClass");
                // Color Face "Medium Pistachio" - yellowish/green 
                aFace.Color = 58;
                aFace.RedisplayObject();
            }
        }

        public void BuildAttributes(CYLINDER face)
        {
            Face aFace = face.Face;

            WriteAttribute(aFace, face.Dir_I, "i");
            WriteAttribute(aFace, face.Dir_J, "j");
            WriteAttribute(aFace, face.Dir_K, "k");

            if (face.Dir_K == 1)
            {
                WriteAttribute(aFace, "Floor", "FeatureClass");
                // Color Face "Deep Sky" - light blue
                aFace.Color = 205;
                aFace.RedisplayObject();

            }
            if (face.Dir_K == -1)
            {
                string setup = "2";
                WriteAttribute(aFace, "Floor", "FeatureClass");
                WriteAttribute(aFace, setup, "Setup");
                // Color Face "Deep Sky" - light Blue
                aFace.Color = 205;
                aFace.RedisplayObject();
            }
            if (face.Dir_K == 0)
            {
                WriteAttribute(aFace, "Wall", "FeatureClass");
                // Color Face "Medium Pistachio" - yellowish/green 
                aFace.Color = 58;
                aFace.RedisplayObject();
            }
        }



        public void WriteAttribute(Face aFace, double value, string title)
        {
            aFace.SetUserAttribute(title, -1, value, Update.Option.Now);
        }
        public void WriteAttribute(Face aFace, string value, string title)
        {
            aFace.SetUserAttribute(title, -1, value, Update.Option.Now);
        }
    }
}