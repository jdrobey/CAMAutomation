using System;
using System.Collections.Generic;
using System.Linq;


namespace CAMAutomation
{
    public class Machine
    {
        public enum MachineType { INVALID = -1, AXIS_3 = 1, AXIS_5 = 2 };

        public struct Envelope
        {
            public Envelope(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        public static string GetMachineTypeAsString(MachineType type)
        {
            if (type == MachineType.AXIS_3)
            {
                return "3-Axis";
            }
            else if (type == MachineType.AXIS_5)
            {
                return "5-Axis";
            }
            else
            {
                return String.Empty;
            }
        }


        public static MachineType GetMachineTypeFromString(string str)
        {
            foreach (MachineType type in Enum.GetValues(typeof(MachineType)))
            {
                if (GetMachineTypeAsString(type) == str)
                {
                    return type;
                }
            }

            return MachineType.INVALID;
        }


        public Machine()
        {
            IsAvailable = false;
            SupportsPreFinishedBlank = false;
            AllMaterialsSupported = false;
            AllViseOpeningSupported = false;
            AllWeightSupported = false;

            m_supportedMaterials = new HashSet<string>();
        }


        ~Machine()
        {
            // empty
        }


        public bool IsMaterialSupported(string material)
        {
            // Short circuit the "OR" operator
            return AllMaterialsSupported || m_supportedMaterials.Any(p => material.Contains(p));
        }


        public bool IsViseOpeningSupported(double opening)
        {
            return AllViseOpeningSupported || opening <= MaxViseOpening;
        }

       
        public bool IsWeightSupported(double weight)
        {
            // In Pounds
            return AllWeightSupported || weight <= MaxWeight;
        }


        public void AddSupportedMaterial(string material)
        {
            m_supportedMaterials.Add(material);
        }


        public double GetVolume()
        {
            return MaxWorkEnvelope.X * MaxWorkEnvelope.Y * MaxWorkEnvelope.Z;
        }


        public bool DoesPartFitIn(double X, double Y, double Z)
        {
            double minMaxWorkEnvelopeDimension = Math.Min(MaxWorkEnvelope.X, Math.Min(MaxWorkEnvelope.Y, MaxWorkEnvelope.Z));
            double maxPartDimension = Math.Max(X, Math.Max(Y, Z));

            return maxPartDimension <= minMaxWorkEnvelopeDimension;
        }


        public bool IsPartTooSmallFor(double X, double Y, double Z)
        {
            double maxMinWorkEnvelopeDimension = Math.Max(MinWorkEnvelope.X, Math.Max(MinWorkEnvelope.Y, MinWorkEnvelope.Z));
            double minPartDimension = Math.Min(X, Math.Min(Y, Z));

            return minPartDimension >= maxMinWorkEnvelopeDimension;
        }


        public string MachineID { get; set; }
        public MachineType Type { get; set; }

        public bool SupportsPreFinishedBlank { get; set; }
        public bool IsAvailable { get; set; }
        public bool AllMaterialsSupported { get; set; }
        public bool AllViseOpeningSupported { get; set; }
        public bool AllWeightSupported { get; set; }

        private HashSet<string> m_supportedMaterials;

        public double MaxViseOpening;
        public double MaxWeight; // In Pounds

        public Envelope MaxWorkEnvelope { get; set; }
        public Envelope MinWorkEnvelope { get; set; }
    }
}
