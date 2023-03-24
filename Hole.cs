using System;

using NXOpen;


namespace CAMAutomation
{
    public class Hole : IEquatable<Hole>
    {
        public Hole(Point3d centerPoint, Vector3d centerAxis, double radius, Face face) 
        {
            m_cylinder = new Utilities.Cylinder(centerPoint, centerAxis, radius);
            m_nxFace = face;
        }


        ~Hole()
        {
            // empty
        }


        public Point3d Origin
        {
            get { return m_cylinder.Origin; }
        }


        public Vector3d Axis
        {  
            get { return m_cylinder.Axis; }
        }
     

        public double Radius
        {
            get { return m_cylinder.Radius; }
        }


        public Face NXFace
        {
            get { return m_nxFace; }
        }


        public Utilities.Line GetCenterLine()
        {
            return m_cylinder.GetCenterLine();
        }
       

        public bool IsEquivalent(Hole hole, double deltaHoleRadius = 0.0, double lineRadius = 0.0, double solidAngle = 0.0)
        {
            return Utilities.MathUtils.IsNeighbour(Origin, hole.GetCenterLine(), lineRadius) &&
                   Utilities.MathUtils.InDoubleCone(Axis, hole.GetCenterLine().Axis, solidAngle) &&
                   Utilities.MathUtils.IsNeighbour(Radius, hole.Radius, deltaHoleRadius);
        }


        public override bool Equals(object obj)
        {
            if (obj != null && obj is Hole)
            {
                return Equals((Hole)obj); 
            }

            return false;
        }


        public bool Equals(Hole hole)
        {
            return IsEquivalent(hole);
        }


        public override int GetHashCode()
        {
            return 0;
        }


        private Utilities.Cylinder m_cylinder;
        private Face m_nxFace;
    }
}
