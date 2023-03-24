using System.Linq;

using NXOpen;
using NXOpen.Facet;


namespace CAMAutomation
{
    public abstract class BaseDeviation
    {
        protected BaseDeviation(FacetedBody targetBody)
        {
            m_TargetBody = targetBody;
        }

        ~BaseDeviation()
        {
            // empty
        }

        public abstract bool ComputeDeviation(out double deviation, out Unit unit);

        protected FacetedBody m_TargetBody;
    }


    public class GeometryDeviation : BaseDeviation
    {
        public GeometryDeviation(Face[] sourceFaces, FacetedBody targetBody, double maxCheckingDistance, double maxCheckingAngle) : base(targetBody)
        {
            m_SourceFaces = sourceFaces;
            m_MaxCheckingDistance = maxCheckingDistance;
            m_MaxCheckingAngle = maxCheckingAngle;
        }

        ~GeometryDeviation()
        {
            // empty
        }

        public override bool ComputeDeviation(out double deviation, out Unit unit)
        {
            deviation = 0.0;
            unit = null;

            if (m_SourceFaces == null || m_SourceFaces.Length==0 || m_TargetBody == null)
                return false;

            // Are all source faces coming from same part ?
            if (m_SourceFaces.Any(p => p.OwningPart != m_SourceFaces.First().OwningPart))
                return false;

            // Get the owning parts of both bodies
            Part sourcePart = m_SourceFaces.First().OwningPart as Part;
            Part targetPart = m_TargetBody.OwningPart as Part;

            // Are both bodies living in the same part ?
            if (sourcePart == null || targetPart == null || sourcePart != targetPart)
                return false;

            // Retrieve the unit
            unit = targetPart.UnitCollection.GetBase("Length");

            // Create the Deviation Gauge Handler
            DeviationGaugeHandler deviationGauge = new DeviationGaugeHandler(sourcePart, m_MaxCheckingDistance, m_MaxCheckingAngle);

            // Add the source and target bodies
            deviationGauge.AddReferenceObjects(m_SourceFaces);
            deviationGauge.AddTargetObjects(m_TargetBody);

            // Run the Deviation Gauge
            bool success = deviationGauge.RunCheck(out deviation);

            return success;
        }

        private Face[] m_SourceFaces;
        private double m_MaxCheckingDistance;
        private double m_MaxCheckingAngle;
    }


    public class VolumeDeviation : BaseDeviation
    {
        public VolumeDeviation(Body sourceBody, FacetedBody targetBody) : base(targetBody)
        {
            m_SourceBody = sourceBody;
        }

        ~VolumeDeviation()
        {
            // empty
        }

        public override bool ComputeDeviation(out double deviation, out Unit unit)
        {
            deviation = 0.0;
            unit = null;

            if (m_SourceBody == null || m_TargetBody == null)
                return false;

            // Get the source body volume
            double sourceBodyVolume = Utilities.NXOpenUtils.GetVolume(m_SourceBody);

            // Get the target body volume
            double targetBodyVolume = m_TargetBody.Volume;

            // Compute the volume ration bettween the two bodies
            if (sourceBodyVolume > 0.0 && targetBodyVolume > 0.0)
            {
                deviation = (targetBodyVolume - sourceBodyVolume) / sourceBodyVolume * 100;
                return true;
            }
            else
            {
                return false;
            }
        }

        private Body m_SourceBody;
    }
}
