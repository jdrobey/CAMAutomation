using NXOpen;
using NXOpen.Annotations;
using NXOpen.Assemblies;
using NXOpen.UF;
using NXOpen.GeometricUtilities;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using static NXOpen.UF.UFModl;
using static System.Math;


namespace CAMAutomation
{
    public class CAMFaceAnalyzer
    {
        public CAMFaceAnalyzer(Component component)
        {
            m_manager = CAMAutomationManager.GetInstance();
            ufs = UFSession.GetUFSession();
            List<FACE> FACES = new List<FACE>();
            List<CYLINDER> CYLINDERS = new List<CYLINDER>();
            List<CONE> CONES = new List<CONE>();
            List<PLANE> PLANES = new List<PLANE>();
            List<BSURF> BSURFS = new List<BSURF>();
            List<NURBS> NURBs = new List<NURBS>();

            CADcomponent = component;
            part = CADcomponent.Prototype as Part;

            foreach (Body prototypeBody in part.Bodies.ToArray())
            {
                Body body = CADcomponent.FindOccurrence(prototypeBody) as Body;
                Edge[] edges = body.GetEdges();
                Face[] faces = body.GetFaces();
                foreach (Face face in faces)
                {
                    double[] axisPoint = new double[3];
                    double[] axisVector = new double[3];
                    double[] box = new double[6];
                    int surfType;
                    double r1, r2;
                    int flip;

                    Tag faceTag = face.Tag;
                    ufs.Modl.AskFaceData(faceTag, out surfType, axisPoint, axisVector, box, out r1, out r2, out flip);

                    if (surfType == 16) {
                        CYLINDER temp = new CYLINDER(); 
                        temp.Type = "Cylinder";
                        temp.TypeID = surfType;
                        temp.Face = face;
                        temp.FaceTag = faceTag;
                        temp.Pt_X = Round(axisPoint[0], 2);
                        temp.Pt_Y = Round(axisPoint[1], 2);
                        temp.Pt_Z = Round(axisPoint[2], 2);
                        temp.Dir_I = Round(axisVector[0], 3);
                        temp.Dir_J = Round(axisVector[1], 3);
                        temp.Dir_K = Round(axisVector[2], 3);
                        temp.Xmin = Round(box[0], 2);
                        temp.Ymin = Round(box[1], 2);
                        temp.Zmin = Round(box[2], 2);
                        temp.Xmax = Round(box[3], 2);
                        temp.Ymax = Round(box[4], 2);
                        temp.Zmax = Round(box[5], 2);
                        temp.MajorRadius = r1;
                        temp.MinorRadius = r2;
                        temp.Norm_dir = flip;

                        CYLINDERS.Add(temp);
                        FACES.Add(temp);
                        
                    }
                    else if (surfType == 17) 
                    { 
                        CONE temp = new CONE();
                        temp.Type = "Cone";
                        temp.TypeID = surfType;
                        temp.Face = face;
                        temp.FaceTag = faceTag;
                        temp.Pt_X = axisPoint[0];
                        temp.Pt_Y = axisPoint[1];
                        temp.Pt_Z = axisPoint[2];
                        temp.Dir_I = axisVector[0];
                        temp.Dir_J = axisVector[1];
                        temp.Dir_K = axisVector[2];
                        temp.Xmin = box[0];
                        temp.Ymin = box[1];
                        temp.Zmin = box[2];
                        temp.Xmax = box[3];
                        temp.Ymax = box[4];
                        temp.Zmax = box[5];
                        temp.MajorRadius = r1;
                        temp.MinorRadius = r2;
                        temp.Norm_dir = flip;

                        CONES.Add(temp);
                        FACES.Add(temp);
                    }
                    else if (surfType == 22) 
                    { 
                        PLANE temp = new PLANE();
                        temp.Type = "Plane";
                        temp.TypeID = surfType;
                        temp.Face = face;
                        temp.FaceTag = faceTag;
                        temp.Pt_X = axisPoint[0];
                        temp.Pt_Y = axisPoint[1];
                        temp.Pt_Z = axisPoint[2];
                        temp.Dir_I = axisVector[0];
                        temp.Dir_J = axisVector[1];
                        temp.Dir_K = axisVector[2];
                        temp.Xmin = box[0];
                        temp.Ymin = box[1];
                        temp.Zmin = box[2];
                        temp.Xmax = box[3];
                        temp.Ymax = box[4];
                        temp.Zmax = box[5];
                        temp.MajorRadius = r1;
                        temp.MinorRadius = r2;
                        temp.Norm_dir = flip;

                        PLANES.Add(temp);
                        FACES.Add(temp);
                    }
                    else if (surfType == 43) 
                    { 
                        BSURF temp = new BSURF();
                        Bsurface bsurface;
                        ufs.Modl.AskBsurf(faceTag, out bsurface);
                        temp.num_poles_u = bsurface.num_poles_u;
                        temp.num_poles_v = bsurface.num_poles_v;
                        temp.order_u = bsurface.order_u;
                        temp.order_v = bsurface.order_v;
                        temp.is_rational = bsurface.is_rational;
                        temp.knots_u = bsurface.knots_u;
                        temp.knots_v = bsurface.knots_v;
                        temp.poles = bsurface.poles;

                        temp.Type = "B-Surf";
                        temp.TypeID = surfType;
                        temp.Face = face;
                        temp.FaceTag = faceTag;
                        temp.Pt_X = axisPoint[0];
                        temp.Pt_Y = axisPoint[1];
                        temp.Pt_Z = axisPoint[2];
                        temp.Dir_I = axisVector[0];
                        temp.Dir_J = axisVector[1];
                        temp.Dir_K = axisVector[2];
                        temp.Xmin = box[0];
                        temp.Ymin = box[1];
                        temp.Zmin = box[2];
                        temp.Xmax = box[3];
                        temp.Ymax = box[4];
                        temp.Zmax = box[5];
                        temp.MajorRadius = r1;
                        temp.MinorRadius = r2;
                        temp.Norm_dir = flip;

                        BSURFS.Add(temp);
                        FACES.Add(temp);
                    }
                    else 
                    { 
                        NURBS temp = new NURBS();
                        temp.Type = "NURBS";
                        temp.TypeID = surfType;
                        temp.Face = face;
                        temp.FaceTag = faceTag;
                        temp.Pt_X = axisPoint[0];
                        temp.Pt_Y = axisPoint[1];
                        temp.Pt_Z = axisPoint[2];
                        temp.Dir_I = axisVector[0];
                        temp.Dir_J = axisVector[1];
                        temp.Dir_K = axisVector[2];
                        temp.Xmin = box[0];
                        temp.Ymin = box[1];
                        temp.Zmin = box[2];
                        temp.Xmax = box[3];
                        temp.Ymax = box[4];
                        temp.Zmax = box[5];
                        temp.MajorRadius = r1;
                        temp.MinorRadius = r2;
                        temp.Norm_dir = flip;

                        NURBs.Add(temp);
                        FACES.Add(temp);
                    }
                    



                }
            }


            Cylinders = CYLINDERS.ToArray();
            Cones = CONES.ToArray();
            Planes = PLANES.ToArray();
            Bsurfs= BSURFS.ToArray();
            Nurbs= NURBs.ToArray();
            
        }
        

        // All Faces will be given the face class with the following properties
        // Faces are assigned to child classes based on the geometry type; Cylinder, Cone, Plane, BSurf, Nurbs(misc)
        // Child classes will have different functions, methods, properties, etc based on their geometry type. 
        public class FACE
        {
            // Surface Type
            public string Type { get;set;}
            // Surface Type ID
            public int TypeID { get; set; }
            // Face Object
            public Face Face { get; set; }

            // Face Tag
            public Tag FaceTag { get; set; }

            // Point Information
            public double Pt_X { get; set; }
            public double Pt_Y { get; set; }
            public double Pt_Z { get; set; }

            // Dir Information
            public double Dir_I { get; set; }
            public double Dir_J { get; set; }
            public double Dir_K { get; set; }

            // Box Information
            public double Xmin { get; set; }
            public double Ymin { get; set; }
            public double Zmin { get; set; }
            public double Xmax { get; set; }
            public double Ymax { get; set; }
            public double Zmax { get; set; }

            

            // Major Radius
            public double MajorRadius { get; set; }
            // Minor Radius (Torus or Cone only)
            public double MinorRadius { get; set; }
            // Face normal direction: +1 if the face normal is in the 
            // same direction as the surface normal(cross product of
            // the U- and V-derivative vectors), -1 if reversed.
            public int Norm_dir { get; set; }

        }










        // Geometric Classes
        public class CYLINDER : FACE
        {

        }


        public class CONE : FACE
        {

        }

        public class PLANE : FACE
        {

        }

        public class BSURF : FACE
        {
            public int num_poles_u { get; set; }
            public int num_poles_v { get; set; }
            public int order_u { get; set; }
            public int order_v { get; set; }
            public int is_rational { get; set; }

            public double[] knots_u { get; set; }
            public double[] knots_v { get; set; }
            public double[,] poles { get; set; }


        }
        public class NURBS : FACE
        {

        }

        














        private CAMAutomationManager m_manager;
        private Component CADcomponent;
        public UFSession ufs;
        public Part part;

        public FACE[] Faces;
        public CYLINDER[] Cylinders;
        public CONE[] Cones;
        public PLANE[] Planes;
        public BSURF[] Bsurfs;
        public NURBS[] Nurbs;

    }
}

