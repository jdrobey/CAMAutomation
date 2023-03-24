using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NXOpen;
using NXOpen.CAM;

namespace CAMAutomation
{
    public class ObjectiveFunctionContainer
    {
        public ObjectiveFunctionContainer(string outputDirectory)
        {
            Initialize();

            CSVFolder = Directory.Exists(outputDirectory) ? System.IO.Path.Combine(outputDirectory, "CSV"): null;
        }


        ~ObjectiveFunctionContainer()
        {
            // empty
        }


        public void Initialize()
        {
            SingleClamping = CreateSingleClampingFunction();
            PairClamping = CreatePairClampingFunction();

            IsInitialized = true;
        }


        public ObjectiveFunction<ClampingConfiguration> CreateSingleClampingFunction()
        {
            bool isnormalizeRequested = System.Configuration.ConfigurationManager.AppSettings["MISUMI_NORMALIZE_CLAMPING_OBJECTIVE_FUNCTION"] == "1";
            string stringFunctionOutput = System.Configuration.ConfigurationManager.AppSettings["MISUMI_CLAMPING_OBJECTIVE_FUNCTION"];

            SortedList<string, Func<ClampingConfiguration, object>> inputLambdas = new SortedList<string, Func<ClampingConfiguration, object>>();
            inputLambdas["CG"] = (c) => c.GetReferenceGravityCenterHeight();
            inputLambdas["LA"] = (c) => c.ReferenceLeverArmRatio;
            inputLambdas["FM"] = (c) => c.FullyMachinableFeatures.Length;
            inputLambdas["PM"] = (c) => c.PartiallyMachinableFeatures.Length;
            inputLambdas["CA"] = (c) => c.GetReferenceClampingArea();
            inputLambdas["CT"] = (c) => c.ReferenceClampingThickness;
            inputLambdas["CH"] = (c) => c.ReferenceClampingHeight;
            inputLambdas["MD"] = (c) => c.GetReferenceBoundingBoxDimension(Enumerable.Max);

            string sourceCodeDelegates = @"
        // Func<object, object> N => ObjectiveFunction.ApplyNormalyse;
        // static Func<NXOpen.Vector3d, double, NXOpen.Vector3d> Mul => Utilities.MathUtils.Multiply;
        static double min = Utilities.MathUtils.ABS_TOL;
        //";

            return new ObjectiveFunction<ClampingConfiguration>(inputLambdas, stringFunctionOutput, sourceCodeDelegates, isnormalizeRequested);
        }


        public void SingleClampingCSVWriter(ClampingConfiguration[] clampingConfigurations, string setup)
        {
            if (CSVFolder == null || SingleClamping == null)
            {
                return;
            }
            Directory.CreateDirectory(CSVFolder);

            string fileName = System.IO.Path.Combine(CSVFolder, String.Format("{0}{1}.csv", SingleFileBaseName, setup));

            object[] header =
                {
                   "Priority",
                   "Objective Function Value",
                   "CG (Gravity Center Height)",
                   "Normalized CG",
                   "LA (Lever Arm Ratio)",
                   "Normalized LA",
                   "CA (Clamping Area)",
                   "Normalized CA",
                   "CH (Clamping Height)",
                   "Normalized CH",
                   "Fully Machinable Features",
                   "FM (NB Fully Machinable Features)",
                   "Normalized FM",
                   "Partially Machinable Features",
                   "PM (NB Partially Machinable Features)",
                   "Normalized PM",
                   "CT (Clamping Thickness)",
                   "Normalized CT",
                   "MD (Maximum Dimension)",
                   "Origin vector",
                   "X CSYS Vector",
                   "Y CSYS Vector",
                   "Z CSYS Vector"
                };

            object[][] values = clampingConfigurations.Select(c => {
                Vector3d[] csys = Utilities.NXOpenUtils.GetCsysAxis(c.ReferenceClampingCsys); Point3d origin = c.ReferenceClampingCsys.Origin;
                return new object[]
                {
                    c.Priority,
                    c.ObjectiveFunctionValue,
                    SingleClamping.InputLambdas["CG"](c),
                    SingleClamping.NormalizedInputLambdas["CG"](c),
                    SingleClamping.InputLambdas["LA"](c),
                    SingleClamping.NormalizedInputLambdas["LA"](c),
                    SingleClamping.InputLambdas["CA"](c),
                    SingleClamping.NormalizedInputLambdas["CA"](c),
                    SingleClamping.InputLambdas["CH"](c),
                    SingleClamping.NormalizedInputLambdas["CH"](c),
                    Utilities.Serialize.Write(p => ((CAMFeature)p).Name, c.FullyMachinableFeatures),
                    SingleClamping.InputLambdas["FM"](c),
                    SingleClamping.NormalizedInputLambdas["FM"](c),
                    Utilities.Serialize.Write(p => ((CAMFeature)p).Name, c.PartiallyMachinableFeatures),
                    SingleClamping.InputLambdas["PM"](c),
                    SingleClamping.NormalizedInputLambdas["PM"](c),
                    SingleClamping.InputLambdas["CT"](c),
                    SingleClamping.NormalizedInputLambdas["CT"](c),
                    SingleClamping.InputLambdas["MD"](c),
                    Utilities.Serialize.Write(origin),
                    Utilities.Serialize.Write(csys[0]),
                    Utilities.Serialize.Write(csys[1]),
                    Utilities.Serialize.Write(csys[2]),
                };
            }).ToArray();

            Utilities.CSV.Write(fileName, values, header);
        }


        public ObjectiveFunction<Utilities.Pair<ClampingConfiguration>> CreatePairClampingFunction()
        {
            bool isnormalizeRequested = System.Configuration.ConfigurationManager.AppSettings["MISUMI_NORMALIZE_CLAMPING_OBJECTIVE_FUNCTION_PAIR"] == "1";
            string stringFunctionOutput = System.Configuration.ConfigurationManager.AppSettings["MISUMI_CLAMPING_OBJECTIVE_FUNCTION_PAIR"];

            // Assuming a feature can not be partially machinable in one setup and fully in the other since setup are at 180 degree respectively
            SortedList<string, Func<Utilities.Pair<ClampingConfiguration>, object>> inputLambdas = new SortedList<string, Func<Utilities.Pair<ClampingConfiguration>, object>>();

            inputLambdas["P1"] = (p) => p.One.PartiallyMachinableFeatures.Length;
            inputLambdas["P2"] = (p) => p.Two.PartiallyMachinableFeatures.Length;
            // Total of partiially machinable feature remove the intersection as they are totally machinable in 2 setups
            inputLambdas["PT"] = (p) => p.One.PartiallyMachinableFeatures.Length + p.Two.PartiallyMachinableFeatures.Length
                                        - 2 * p.One.GetPartiallyMachinableFeatureIntersection(p.Two.PartiallyMachinableFeatures).Length;
            inputLambdas["F1"] = (p) => p.One.FullyMachinableFeatures.Length;
            inputLambdas["F2"] = (p) => p.Two.FullyMachinableFeatures.Length;
            // Total of Fully machinable feature machinable feature remove the intersection and add machinable feature in 2 setups
            inputLambdas["FT"] = (p) => p.One.FullyMachinableFeatures.Length + p.Two.FullyMachinableFeatures.Length
                                        - p.One.GetFullyMachinableFeatureIntersection(p.Two.FullyMachinableFeatures).Length
                                        + p.One.GetPartiallyMachinableFeatureIntersection(p.Two.PartiallyMachinableFeatures).Length;
            // Objective Function
            inputLambdas["OF1"] = (p) => p.One.ObjectiveFunctionValue;
            inputLambdas["OF2"] = (p) => p.Two.ObjectiveFunctionValue;

            inputLambdas["FLIP"] = (p) => Utilities.SO3Utils.YFlip(p.One.ReferenceClampingCsys.Orientation.Element, p.Two.ReferenceClampingCsys.Orientation.Element);

            // Angle between axis of first and second setup
            Func<Utilities.Pair<ClampingConfiguration>, Utilities.SO3Utils.EulerAngles> EulerAngle =
                (p) => Utilities.SO3Utils.GetEulerAngles(p.One.ReferenceClampingCsys.Orientation.Element, p.Two.ReferenceClampingCsys.Orientation.Element);

            inputLambdas["ROLL"] = (p) => EulerAngle(p).Roll;
            inputLambdas["PITCH"] = (p) => EulerAngle(p).Pitch;
            inputLambdas["YAW"] = (p) => EulerAngle(p).Yaw;

            string sourceCodeDelegates = @"
        //";

            return new ObjectiveFunction<Utilities.Pair<ClampingConfiguration>>(inputLambdas, stringFunctionOutput, sourceCodeDelegates, isnormalizeRequested);
        }


        public void PairClampingCSVWriter(Tuple<Utilities.Pair<ClampingConfiguration>, double>[] sortedConfigurationTuples)
        {
            if (CSVFolder == null || SingleClamping == null)
            {
                return;
            }
            Directory.CreateDirectory(CSVFolder);

            string fileName = System.IO.Path.Combine(CSVFolder, String.Format("{0}.csv", PairFileBaseName));

            object[] header =
                {
                   "Priority",
                   "Objective Function Value",
                   "Setup 1 Priority",
                   "Setup 2 Priority",
                   "P1",
                   "Normalized P1",
                   "P2",
                   "Normalized P2",
                   "PT",
                   "Normalized PT",
                   "F1",
                   "Normalized F1",
                   "F2",
                   "Normalized F2",
                   "FT",
                   "Normalized FT",
                   "OF1",
                   "Normalized OF1",
                   "OF2",
                   "Normalized OF2",
                   "FLIP",
                   "ROLL",
                   "Normalized ROLL",
                   "PITCH",
                   "Normalized PITCH",
                   "YAW",
                   "Normalized YAW",
                };

            object[][] values = sortedConfigurationTuples.Select(ct => {
                double objectiveValue = ct.Item2; Utilities.Pair<ClampingConfiguration> p = ct.Item1;
                return new object[]
                {
                    Array.IndexOf(sortedConfigurationTuples, ct),
                    objectiveValue,
                    p.One.Priority,
                    p.Two.Priority,
                    PairClamping.InputLambdas["P1"](p),
                    PairClamping.NormalizedInputLambdas["P1"](p),
                    PairClamping.InputLambdas["P2"](p),
                    PairClamping.NormalizedInputLambdas["P2"](p),
                    PairClamping.InputLambdas["PT"](p),
                    PairClamping.NormalizedInputLambdas["PT"](p),
                    PairClamping.InputLambdas["F1"](p),
                    PairClamping.NormalizedInputLambdas["F1"](p),
                    PairClamping.InputLambdas["F2"](p),
                    PairClamping.NormalizedInputLambdas["F2"](p),
                    PairClamping.InputLambdas["FT"](p),
                    PairClamping.NormalizedInputLambdas["FT"](p),
                    PairClamping.InputLambdas["OF1"](p),
                    PairClamping.NormalizedInputLambdas["OF1"](p),
                    PairClamping.InputLambdas["OF2"](p),
                    PairClamping.NormalizedInputLambdas["OF2"](p),
                    PairClamping.InputLambdas["FLIP"](p),
                    PairClamping.InputLambdas["ROLL"](p),
                    PairClamping.NormalizedInputLambdas["ROLL"](p),
                    PairClamping.InputLambdas["PITCH"](p),
                    PairClamping.NormalizedInputLambdas["PITCH"](p),
                    PairClamping.InputLambdas["YAW"](p),
                    PairClamping.NormalizedInputLambdas["YAW"](p),
                    };
                }).ToArray();

                Utilities.CSV.Write(fileName, values, header);
            }


        public static string CSVFolder { get; set; }
        public static string SingleFileBaseName { get; } = "ClampingConfigurationsAlternatives_Setup_";
        public static string PairFileBaseName { get; } = "ClampingPairConfigurationsAlternatives";

        public ObjectiveFunction<Utilities.Pair<ClampingConfiguration>> PairClamping { get; private set; }
        public ObjectiveFunction<ClampingConfiguration> SingleClamping { get; private set; }

        public bool IsInitialized { get; private set; } = false;
    }
}
