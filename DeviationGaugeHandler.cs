using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using NXOpen;
using NXOpen.GeometricAnalysis;

namespace CAMAutomation
{
    public class DeviationGaugeHandler
    {
        private enum UsedStrings
        {
            MaxDevError,
            MinDevError,
            FileDeleteError,
        }

        public DeviationGaugeHandler(BasePart part, double maxCheckingDistance, double maxCheckingAngle)
        {
            m_part = part;
            m_outputDirectory = Environment.GetEnvironmentVariable("UGII_TMP_DIR");

            m_ReferenceObjects = new List<NXObject>();
            m_TargetObjects = new List<NXObject>();

            m_MaxCheckingDistance = maxCheckingDistance;
            m_MaxCheckingAngle = maxCheckingAngle;
        }

        ~DeviationGaugeHandler()
        {
            // empty
        }


        public void AddReferenceObjects(NXObject obj)
        {
            m_ReferenceObjects.Add(obj);
        }


        public void AddReferenceObjects(NXObject[] obj)
        {
            m_ReferenceObjects.AddRange(obj);
        }


        public void AddTargetObjects(NXObject obj)
        {
            m_TargetObjects.Add(obj);
        }


        public void AddTargetObjects(NXObject[] obj)
        {
            m_TargetObjects.AddRange(obj);
        }


        public bool RunCheck(out double maximumDeviation)
        {
            return RunCheck(out maximumDeviation, out double minimumDeviation);
        }


        public bool RunCheck(out double maximumDeviation, out double minimumDeviation)
        {
            bool success = false;

            maximumDeviation = 0.0;
            minimumDeviation = 0.0;

            // Create the builder
            DeviationGaugeBuilder builder = m_part.AnalysisManager.AnalysisObjects.CreateDeviationGaugeBuilder(null);

            try
            {
                double convertFromInches = m_part.PartUnits == BasePart.Units.Millimeters ? 25.4 : 1.0;

                // Builder Options
                builder.MeasurementMethod = DeviationGaugeBuilder.MeasurementMethodType.ThreeDim;
                builder.IsColorMapDisplayed = false;
                builder.IsNeedlePlotDisplayed = false;
                builder.IsColorMapDisplayed = false;
                builder.HasMinimumValueLabel = true;
                builder.HasMaximumValueLabel = true;
                builder.MaxCheckingDistance = m_MaxCheckingDistance * convertFromInches;
                builder.MaxCheckingAngle = m_MaxCheckingAngle;
                builder.SpatialResolution = 0.1 * convertFromInches;

                // Add Reference Objects
                builder.ReferenceObjects.Clear();
                builder.ReferenceObjects.Add(m_ReferenceObjects.ToArray());

                // Add Target Objects
                builder.TargetObjects.Clear();
                builder.TargetObjects.Add(m_TargetObjects.ToArray());

                // Perform analysis
                NXObject nxObject = builder.Commit();
                DeviationGauge deviationGauge = nxObject as DeviationGauge;

                // Post result in information window
                string fileFullName = Path.Combine(m_outputDirectory, "DeviationCheckResults.txt");
                Utilities.RemoteSession.NXSession.ListingWindow.SelectDevice(ListingWindow.DeviceType.File, fileFullName);
                Utilities.RemoteSession.NXSession.Information.DisplayObjectsDetails(new NXObject[] { deviationGauge });
                Utilities.RemoteSession.NXSession.ListingWindow.SelectDevice(ListingWindow.DeviceType.Window, String.Empty);

                // Read the information file
                string[] resultContent = File.ReadAllLines(fileFullName);

                string maxdevStr = resultContent
                            .Where(s => s.Contains(usedStrings[UsedStrings.MaxDevError]))
                            .Select(s => (s.Replace(usedStrings[UsedStrings.MaxDevError], String.Empty).Split('=').Last().Trim()))
                            .Where(s => s != String.Empty && s.Replace(".", String.Empty).Replace("-", String.Empty).All(char.IsDigit)).FirstOrDefault();
                string mindevStr = resultContent
                            .Where(s => s.Contains(usedStrings[UsedStrings.MinDevError]))
                            .Select(s => (s.Replace(usedStrings[UsedStrings.MinDevError], String.Empty).Split('=').Last().Trim()))
                            .Where(s => s != String.Empty && s.Replace(".", String.Empty).Replace("-", String.Empty).All(char.IsDigit)).FirstOrDefault();

                if (maxdevStr != null && mindevStr != null)
                {
                    if (double.TryParse(maxdevStr, out double maxdev) && double.TryParse(mindevStr, out double mindev))
                    {
                        double[] maxminvalues = new double[] { Math.Abs(maxdev), Math.Abs(mindev) };

                        maximumDeviation = maxminvalues.Max();
                        minimumDeviation = mindev < 0.0 ? 0.0 : maxminvalues.Min();

                        success = true;
                    }
                }

                // Delete the temporary file
                File.Delete(fileFullName);
            }
            catch (Exception)
            {
                 // empty
            }
            finally
            {
                // Destroy the builder
                builder.Destroy();
            }

            return success;
        }


        private Dictionary<UsedStrings, string> usedStrings =
                                                 new Dictionary<UsedStrings, string>() {
                                                    { UsedStrings.MaxDevError,"Maximum Deviation/Error"},
                                                    { UsedStrings.MinDevError,"Minimum Deviation/Error"}
                                                 };

        private BasePart m_part;
        private string m_outputDirectory;

        private List<NXObject> m_ReferenceObjects;
        private List<NXObject> m_TargetObjects;

        private double m_MaxCheckingDistance;
        private double m_MaxCheckingAngle;
    }
}
