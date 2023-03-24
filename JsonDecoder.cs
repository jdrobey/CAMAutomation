using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NXOpen.Routing;
using System.Security.AccessControl;

namespace CAMAutomation
{
    public class JsonDecoder
    {
        public JsonDecoder()
        {
            ufs = UFSession.GetUFSession(); 
            m_manager = CAMAutomationManager.GetInstance();
            CADpart = m_manager.CADPart;
            bodies = CADpart.Bodies.ToArray();
            body = bodies[0];
        }

        public class Feature
        {
            public Dictionary<string, string> EnvDic { get; set; }
            public string featureType { get; set; }
            public string featureID { get; set; }

            public void Decode()
            {
                JsonDecoder decoder = new JsonDecoder();
                switch (featureType)
                {
                    case "ENV":
                        decoder.ENV(EnvDic);
                        break;

                    case "CLEARANCE":
                        //code
                        break;

                    case "FA_PLATE":
                        //code
                        break;

                    case "GEOMETRICAL_TOLERANCE":
                        //code
                        break;

                    case "HOLE":
                        decoder.HOLE(EnvDic);
                        break;

                    case "LOCATION_TOLERANCE":
                        //code
                        break;

                    case "POCKET":
                        //code
                        break;

                    case "SLOT":
                        decoder.SLOT(EnvDic);
                        break;

                    case "SURFACE_ROUGHNESS":
                        decoder.SURFACE_ROUGHNESS(EnvDic);
                        break;

                }
            }

        }







        // Write Env features to all faces of the CADpaart
        public void ENV(Dictionary<string, string> attributes)
        {
            foreach (NXOpen.Face face in body.GetFaces())
            {
                WriteEnv(attributes, face);
            }
        }



        // Match the Hole feature with the correct face and assign the attributes
        public void HOLE(Dictionary<string, string> attributes)
        {
            foreach (NXOpen.Face face in body.GetFaces())
            {
                bool match = HoleMatch(attributes, face);
                if (match)
                {
                    WriteEnv(attributes, face);
                }
            }
        }


        public void SLOT(Dictionary<string, string> attributes)
        {
            foreach (NXOpen.Face face in body.GetFaces())
            {
                bool match = SlotMatch(attributes, face);
                if (match) { WriteEnv(attributes, face); }
            }
        }

        public void SURFACE_ROUGHNESS(Dictionary<string, string> attributes)
        {
            foreach (NXOpen.Face face in body.GetFaces())
            {
                bool match = SRFaceMatch(attributes, face);
                if (match) { WriteEnv(attributes, face); }
            }
        }











        //*********************************
        //              Functions
        //*********************************


        // Match json Hole feature with CAD geometry
        private bool HoleMatch(Dictionary<string, string> attributes, NXOpen.Face face)
        {
            // Collect Face data 
            double[] axisPoint = new double[3];
            double[] axisVector = new double[3];
            double[] box = new double[6];
            int surfType;
            double r1, r2;
            int flip;
            Tag faceTag = face.Tag;
            ufs.Modl.AskFaceData(faceTag, out surfType, axisPoint, axisVector, box, out r1, out r2, out flip);

            // Check if surface is cylinder or cone and return false if not
            if (surfType != 16 && surfType!= 17)
            {
                return false;
            }

            // Collect Face Parameter
            double ptX = Math.Round(axisPoint[0], 2);
            double ptY = Math.Round(axisPoint[1], 2);
            double ptZ = Math.Round(axisPoint[2], 2);
            double Xmin = Math.Round(box[0], 2);
            double Ymin = Math.Round(box[1], 2);
            double Zmin = Math.Round(box[2], 2);
            double Xmax = Math.Round(box[3], 2);
            double Ymax = Math.Round(box[4], 2);
            double Zmax = Math.Round(box[5], 2);
            //double Rad = Math.Round(r1, 3);


            // Collect Json Hole Parameters
            double xpos = Convert.ToDouble(attributes["HOLE.LOCATION.CENTER.COORDINATE_X"]);
            double ypos = Convert.ToDouble(attributes["HOLE.LOCATION.CENTER.COORDINATE_Y"]);
            double zpos = Convert.ToDouble(attributes["HOLE.LOCATION.CENTER.COORDINATE_Z"]);

            // boolean checks for x, y, & z
            bool Xcheck = PosCheck(ptX, Xmax, Xmin, xpos);
            bool Ycheck = PosCheck(ptY, Ymax, Ymin, ypos);
            bool Zcheck = PosCheck(ptZ, Zmax, Zmin, zpos);

            if (Xcheck && Ycheck && Zcheck)
            {
                return true;
            }

            return false; 
        }

        private bool SlotMatch(Dictionary<string, string> attributes, NXOpen.Face face)
        {
            // Collect Face data 
            double[] axisPoint = new double[3];
            double[] axisVector = new double[3];
            double[] box = new double[6];
            int surfType;
            double r1, r2;
            int flip;
            Tag faceTag = face.Tag;
            ufs.Modl.AskFaceData(faceTag, out surfType, axisPoint, axisVector, box, out r1, out r2, out flip);

            // Check if surface is cylinder, return false if not
            if (surfType != 16)
            {
                return false;
            }


            // Json Data
            string Axis = attributes["@SLOT.WIDTH.DIRECTION"];
            double W = Convert.ToDouble(attributes["@SLOT.WIDTH"]);

            double side1X = Convert.ToDouble(attributes["@SLOT.SIDE.1.COORDINATE_X"]);
            double side1Y = Convert.ToDouble(attributes["@SLOT.SIDE.1.COORDINATE_Y"]);
            double side1Z = Convert.ToDouble(attributes["@SLOT.SIDE.1.COORDINATE_Z"]);

            double side2X = Convert.ToDouble(attributes["@SLOT.SIDE.2.COORDINATE_X"]);
            double side2Y = Convert.ToDouble(attributes["@SLOT.SIDE.2.COORDINATE_Y"]);
            double side2Z = Convert.ToDouble(attributes["@SLOT.SIDE.2.COORDINATE_Z"]);
  

            // Collect Face Parameter

            //double ptX = Math.Round(axisPoint[0], 2);
            //double ptY = Math.Round(axisPoint[1], 2);
            //double ptZ = Math.Round(axisPoint[2], 2);
            double Xmin = Math.Round(box[0], 2);
            double Ymin = Math.Round(box[1], 2);
            double Zmin = Math.Round(box[2], 2);
            double Xmax = Math.Round(box[3], 2);
            double Ymax = Math.Round(box[4], 2);
            double Zmax = Math.Round(box[5], 2);

            double deltaX = Math.Abs(Xmax- Xmin);
            double deltaY = Math.Abs(Ymax- Ymin);
            double deltaZ = Math.Abs(Zmax- Zmin);
            double w;
            double w2;
            double min;
            double max;
            double pt1;
            double pt2;

            switch (Axis)
            {
                case "X":
                    w = Math.Round(deltaX,1);
                    w2 = Math.Round(deltaX * 2, 1);
                    min = Xmin; max = Xmax; pt1 = side1X; pt2 = side2X;
                    //m_manager.LogFile.AddMessage($"w: {w}, w2: {w2}, min: {min}, max: {max}");
                    //m_manager.LogFile.AddMessage($"W: {W}, pt1: {pt1}, pt2: {pt2}");
                    if ((w == W || w2 == W) && (min == pt1 || max == pt2)) { return true; }
                    else { return false; }
                case "Y":
                    w = Math.Round(deltaY, 1);
                    w2 = Math.Round(deltaY * 2, 1);
                    min = Ymin; max = Ymax; pt1 = side1Y; pt2 = side2Y;
                    //m_manager.LogFile.AddMessage($"w: {w}, w2: {w2}, min: {min}, max: {max}");
                    //m_manager.LogFile.AddMessage($"W: {W}, pt1: {pt1}, pt2: {pt2}");
                    if ((w == W || w2 ==W) && (min == pt1 || max == pt2)) { return true; }
                    else { return false; }
                case "Z":
                    w = Math.Round(deltaZ, 1);
                    w2 = Math.Round(deltaZ * 2, 1);
                    min = Zmin; max = Zmax; pt1 = side1Z; pt2 = side2Z;
                    //m_manager.LogFile.AddMessage($"w: {w}, w2: {w2}, min: {min}, max: {max}");
                    //m_manager.LogFile.AddMessage($"W: {W}, pt1: {pt1}, pt2: {pt2}");
                    if ((w == W || w2 == W) && (min == pt1 || max == pt2)) { return true; }
                    else { return false; }
            }
                
            return false;
        }


        // Write Env attributes to a face
        private void WriteEnv(Dictionary<string, string> attributes, NXOpen.Face face)
        {
            foreach (var attribute in attributes)
            {
                string title = attribute.Key.Replace("@", "").Replace(".", "_");
                try
                {
                    double value = Convert.ToDouble(attribute.Value);
                    WriteAttribute(face, value, title);
                }
                catch
                {
                    string value = attribute.Value;
                    WriteAttribute(face, value, title);

                }
            }

        }




        // Position Check
        private bool PosCheck(double pt, double max, double min, double pos)
        {
            //Check face position
            if((pt - 0.02) <= pos && pos <= (pt + 0.02))
            {
                return true;
            }
            //Check face Max
            if((max-0.02) <= pos && pos <= (max + 0.02))
            {
                return true;
            }
            //Check face Min
            if ((min - 0.02) <= pos && pos <= (min + 0.02))
            {
                return true;
            }
            return false;


        }




        // SRFaceMatch
        private bool SRFaceMatch(Dictionary<string, string> attributes, NXOpen.Face face)
        {
            //json Surface Area
            double SA = Math.Round(Convert.ToDouble(attributes["SURFACE_ROUGHNESS.SURFACE.AREA"]), 1);
            //json face side attribute
            string side = attributes["@SURFACE_ROUGHNESS.RELATED.FEATURE.ID"];
            // json directions based on side attribute
            int I = 0;
            int J = 0;
            int K = 0;
            switch (side)
            {
                case "TOP":
                    K = 1;
                    break;
                case "BOTTOM":
                    K = -1;
                    break;
                case "RIGHT":
                    I = 1;
                    break;
                case "LEFT":
                    I = -1;
                    break;
                case "BACK":
                    J = 1;
                    break;
                case "FRONT":
                    J = -1;
                    break;
            }

            // Collect Face data 
            double[] axisPoint = new double[3];
            double[] axisVector = new double[3];
            double[] box = new double[6];
            int surfType;
            double r1, r2;
            int flip;
            Tag faceTag = face.Tag;
            ufs.Modl.AskFaceData(faceTag, out surfType, axisPoint, axisVector, box, out r1, out r2, out flip);

            double x = Math.Round(axisVector[0]);
            double y = Math.Round(axisVector[1]);
            double z = Math.Round(axisVector[2]);

            double area = Math.Round(GetArea(face),1);

            // Check if normal vectors of face match the json and check if the surface areas match
            if ((x == I) && (y == J) && (z == K) && (area == SA))
            {
                return true;
            }
            else { return false; }
        }
        
        private static double GetArea(Face face)

        {
            Unit areaUnit = face.OwningPart.UnitCollection.GetBase("Area");
            Unit lengthUnit = face.OwningPart.UnitCollection.GetBase("Length");
            return face.OwningPart.MeasureManager.NewFaceProperties(areaUnit, lengthUnit, 0.99, new Face[] { face }).Area;

        }

        public void WriteAttribute(NXOpen.Face aFace, double value, string title)
        {
            aFace.SetUserAttribute(title, -1, value, Update.Option.Now);
        }
        public void WriteAttribute(NXOpen.Face aFace, string value, string title)
        {
            aFace.SetUserAttribute(title, -1, value, Update.Option.Now);
        }



        // Declaring Parameters

        private CAMAutomationManager m_manager;
        private Part CADpart;
        private Body[] bodies;
        private Body body;
        private UFSession ufs;

    }
    
}
