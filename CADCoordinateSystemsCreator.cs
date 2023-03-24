using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.CAM;


namespace CAMAutomation
{
    public class CADCoordinateSystemsCreator : CoordinateSystemsCreator
    {
        public CADCoordinateSystemsCreator(Part part) : base(part)
        {
            // empty
        }


        ~CADCoordinateSystemsCreator()
        {
            // empty
        }


        protected override void CheckState()
        {
            if (m_holePatterns.Length == 0)
            {
                // No Hole patterns were found.
                m_manager.LogFile.AddMessage("No Hole Pattern was found in the CAD", m_context);
            }
            else if (m_holePatterns.Length == 1)
            {
                // A valid hole pattern has been found.
                m_manager.LogFile.AddMessage("A valid Hole Pattern was found in the CAD", m_context);
                m_manager.HasCADValidHolePattern = true;
            }
            else
            {
                // Multiple Hole patterns were found. Raise a warning
                m_manager.LogFile.AddWarning("Multiple Hole Patterns were found in the CAD", m_context);
            }

            // Raise an error if we have the pathway PFB_NO_HOLE_PATTERN
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_NO_HOLE_PATTERN)
            {
                throw new Exception("Prefinished Blanks with no hole pattern are not supported by the automation");
            }
        }


        protected override void CreateCoordinateSystems()
        {
            // If Hole Pattern is found, a FIXTURE_CSYS will be computed
            // An additional CLAMPING_CSYS will be computed from hole pattern if we have a STOCK
            if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN ||
                m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.PFB_HOLE_PATTERN)
            {
                Point3d origin;
                Vector3d x, y, z;

                // Create CAD_CSYS_FIXTURE from Hole Pattern
                ComputeFixtureCsysFromHolePattern(m_holePatterns.First(), out origin, out x, out y, out z);
                m_fixtureDatumCsys = Utilities.NXOpenUtils.CreateDatumCsys(origin, x, y, "CAD_CSYS_FIXTURE");

                // Create the clamping coordinate system. It will depend on the fixture coordinate system
                if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_HOLE_PATTERN)
                {
                    // Create CAD_CSYS_CLAMPING from Hole Pattern
                    ComputeClampingCsysFromHolePattern(out origin, out x, out y, out z, out double clampingThickness);
                    m_clampingDatumCsys = Utilities.NXOpenUtils.CreateDatumCsys(origin, x, y, "CAD_CSYS_CLAMPING");

                    // Add clamping thickness attribute to the CAD_CSYS_CLAMPING
                    Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsys, "MISUMI", "CLAMPING_THICKNESS", clampingThickness);
                }
            }

            // If we have a Stock without hole pattern we clamp on the stock
            else if (m_manager.GetAutomationAlgorithm() == CAMAutomationManager.AutomationAlgorithm.STOCK_NO_HOLE_PATTERN)
            {
                // Get the second clamping configuration
                ClampingConfiguration[] secondSetupCandidates = ComputeSecondConfigurationWithoutHolePattern();
                secondSetupCandidates = SpecifyConfiguratuion(secondSetupCandidates, 2);

                // Create ClampingConfigurations from the approximated stock ClampingFaces
                ClampingConfiguration[] firstSetupCandidates = ComputeFirstConfigurationWithoutHolePattern();
                firstSetupCandidates = SpecifyConfiguratuion(firstSetupCandidates, 1);

                List<Utilities.Pair<ClampingConfiguration>> clampingConfigurationPairs = new List<Utilities.Pair<ClampingConfiguration>>();

                // Find all first and second they will be sorted later 
                foreach (ClampingConfiguration firstSetup in firstSetupCandidates)
                { 
                    foreach (ClampingConfiguration secondSetup in secondSetupCandidates)
                    {
                        clampingConfigurationPairs.Add(new Utilities.Pair<ClampingConfiguration>(firstSetup, secondSetup));
                    }
                }

                // Select best pair using paired objective function than create according csys
                Utilities.Pair<ClampingConfiguration> winningPair = m_manager.CAMClampingConfigurator.GetBestClampingPairOption(clampingConfigurationPairs.ToArray());

                Point3d origin1 = winningPair.One.ClampingCsys.Origin;
                Vector3d x1 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.One.ClampingCsys)[0];
                Vector3d y1 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.One.ClampingCsys)[1];
                Vector3d z1 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.One.ClampingCsys)[2];
                m_clampingDatumCsys = Utilities.NXOpenUtils.CreateDatumCsys(origin1, x1, y1, "CAD_CSYS_CLAMPING");

                Point3d origin2 = winningPair.Two.ClampingCsys.Origin;
                Vector3d x2 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.Two.ClampingCsys)[0];
                Vector3d y2 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.Two.ClampingCsys)[1];
                Vector3d z2 = Utilities.NXOpenUtils.GetCsysAxis(winningPair.Two.ClampingCsys)[2];
                m_clampingDatumCsys2 = Utilities.NXOpenUtils.CreateDatumCsys(origin2, x2, y2, "CAD_CSYS_CLAMPING_2");

                Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsys2, "MISUMI", "CLAMPING_THICKNESS", winningPair.Two.ClampingThickness);

                m_manager.CAMClampingConfigurator.AddPreviousClampingConfiguration(new ClampingConfiguration[] { winningPair.One, winningPair.Two });
            }

            // If Near Net Blank is allowed, we need to create a CAD_CLAMPING_CSYS_NEAR_NET_BLANK
            // This CSYS needs to be offseted from CAD_CLAMPING_CSYS by the clamping material value along z axis
            if (m_manager.AllowNearNetBlank)
            {
                // Retrieve CAD_CLAMPING_CSYS
                CartesianCoordinateSystem csys = Utilities.NXOpenUtils.GetCsysFromDatum(m_clampingDatumCsys);

                // Retrieve CAD_CLAMPING_CSYS axis
                Vector3d x = Utilities.NXOpenUtils.GetCsysAxis(csys)[0];
                Vector3d y = Utilities.NXOpenUtils.GetCsysAxis(csys)[1];
                Vector3d z = Utilities.NXOpenUtils.GetCsysAxis(csys)[2];

                // Offset the csys origin by the clamping extra material value along z axis
                Point3d newOrigin = Utilities.MathUtils.Add(csys.Origin, Utilities.MathUtils.Multiply(z, m_clampingExtraMaterial));

                // Create CAD_CSYS_CLAMPING_NEAR_NET_BLANK
                m_clampingDatumCsysNearNetBlank = Utilities.NXOpenUtils.CreateDatumCsys(newOrigin, x, y, "CAD_CSYS_CLAMPING_NEAR_NET_BLANK");

                // Add clamping thickness attribute to the CAD_CSYS_CLAMPING_NEAR_NET_BLANK
                // Retrieve clamping thickness from CAD_CSYS_CLAMPING (stored as attribute)
                Utilities.NXOpenUtils.GetAttribute(m_clampingDatumCsys, "MISUMI", "CLAMPING_THICKNESS", out double clampingThickness);
                Utilities.NXOpenUtils.SetAttribute(m_clampingDatumCsysNearNetBlank, "MISUMI", "CLAMPING_THICKNESS", clampingThickness);
            }
        }


        private ClampingConfiguration[] SpecifyConfiguratuion(ClampingConfiguration[] clampingConfigurations, int setupNumber)
        {
            int priority = m_manager.SetupSelection.TryGetValue(setupNumber, out int value ) ? value : -1;

            if (priority >= 0 && priority < clampingConfigurations.Length)
            {
                return new ClampingConfiguration[] { clampingConfigurations[priority] };
            }

            return clampingConfigurations;
        }


        private void ComputeClampingCsysFromHolePattern(out Point3d origin, out Vector3d x, out Vector3d y, out Vector3d z, out double clampingThickness)
        {
            // Compute the minimum bounding box 
            BoundingBox box = BoundingBox.ComputeBodyBoundingBox(m_body);

            // Retrieve the centroid of the CAD
            Point3d centroid = Utilities.NXOpenUtils.GetCentroid(m_body);

            // Retrieve hole pattern plane axe (The Y axe of the hole pattern CSYS) 
            // (which DOS NOT garantee to be aligned with the minimum bounding box) 
            CartesianCoordinateSystem fixtureCsys = Utilities.NXOpenUtils.GetCsysFromDatum(m_fixtureDatumCsys);
            Vector3d tmpY = Utilities.NXOpenUtils.GetCsysAxis(fixtureCsys)[1];
            tmpY = box.GetFaces().Select(p => p.Normal).OrderBy(p => Utilities.MathUtils.GetAngle(p, tmpY)).First();
            Utilities.Line planeAxe = new Utilities.Line(centroid, tmpY);

            // The extra material is on a face parallel to the hole pattern plane. 
            // Out of the two possible parallel faces, the one closest to the hole pattern plane is selected. 
            // If it is undetermined, we select the face closest to the centroid
            // z+ is toward the part
            //
            // Retrieve the extra material plane candidates
            Utilities.Plane[] extraMaterialPlaneCandidates = box.IntersectingBoundaries(planeAxe);

            // Compute the distance between each candidate and the Hole Pattern origin
            double firstDistance = Utilities.MathUtils.GetDistance(fixtureCsys.Origin, extraMaterialPlaneCandidates.First());
            double lastDistance = Utilities.MathUtils.GetDistance(fixtureCsys.Origin, extraMaterialPlaneCandidates.Last());

            // Select the right extra material plane
            // Since z+ is toward the part, z will be the normal of the NON-candidate plane
            Utilities.Plane extraMaterialPlane;
            if (firstDistance - lastDistance < -Utilities.MathUtils.ABS_TOL)
            {
                extraMaterialPlane = extraMaterialPlaneCandidates.First();
                z = extraMaterialPlaneCandidates.Last().Normal;
            }
            else if (firstDistance - lastDistance > Utilities.MathUtils.ABS_TOL)
            {
                extraMaterialPlane = extraMaterialPlaneCandidates.Last();
                z = extraMaterialPlaneCandidates.First().Normal;
            }
            else
            {
                // It is undetermined. Therefore we select the face closest to the centroid
                Utilities.Plane[] extraMaterialPlaneByCentroidProximity = extraMaterialPlaneCandidates.OrderBy(p => Utilities.MathUtils.GetDistance(centroid, p)).ToArray();
                extraMaterialPlane = extraMaterialPlaneByCentroidProximity.First();
                z = extraMaterialPlaneByCentroidProximity.Last().Normal;
            }

            // Compute the intersecting boundaries with the extra material plane. 
            // Order them by increasing order to the box center
            Utilities.Plane[] orderedBoundariesCrossingPlane = box.IntersectingBoundaries(extraMaterialPlane)
                                                                  .OrderBy(p => Utilities.MathUtils.GetDistance(box.GetBoxCenter(), p)).ToArray();

            // x is defined as the longest side of the bounding box within the extraMaterialPlane plane defined above
            // Therefore, x is the normal of the last plane in orderedBoundariesCrossingPlane[] array
            // x points from the fixture Csys Origin toward the centroid
            Vector3d tmpX = orderedBoundariesCrossingPlane.Last().Normal;
            x = Utilities.MathUtils.Projection(Utilities.MathUtils.GetVector(fixtureCsys.Origin, centroid), tmpX);

            // Normalize z and x
            z = Utilities.MathUtils.UnitVector(z);
            x = Utilities.MathUtils.UnitVector(x);

            // y will be the cross product between z and x
            y = Utilities.MathUtils.Cross(z, x);

            // Clamping thickness is the distance between the 2 closest box boundaries intersected by the extra material plane 
            clampingThickness = Utilities.MathUtils.GetDistance(orderedBoundariesCrossingPlane[0], orderedBoundariesCrossingPlane[1]);

            // CSYS origin is at the center of the bottom face of the extra material (half distance in X and Y)
            // Therefore, we compute the CSYS origin by substracting, from the extra material plane origin, the clamping extra material value
            origin = Utilities.MathUtils.Add(extraMaterialPlane.Origin, Utilities.MathUtils.Multiply(z, -m_clampingExtraMaterial));
        }


        private ClampingConfiguration[] ComputeFirstConfigurationWithoutHolePattern()
        {
            // Compute CAD Features
            ComputeCADFeatures();

            bool success = m_manager.CAMClampingConfigurator.GetFirstClampingConfigurationCandidates(BoundingBox.ComputeBodyBoundingBox(m_body),
                                                                                                     m_body.OwningPart.UnitCollection.GetBase("Length"),
                                                                                                     m_cadFeatures,
                                                                                                     out ClampingConfiguration[] clampingConfigurations);

            if (success)
            {
                return clampingConfigurations;
            }
            else
            {
                throw new Exception("Unable to retrieve the clamping configuration pair");
            }
        }


        private ClampingConfiguration[] ComputeSecondConfigurationWithoutHolePattern()
        {
            // Compute CAD Features
            ComputeCADFeatures();

            bool success = m_manager.CAMClampingConfigurator.GetSecondClampingConfigurationCandidates(m_body,
                                                                                                      m_cadFeatures,
                                                                                                      out ClampingConfiguration[] clampingConfigurations);

            if (success)
            {
                return clampingConfigurations;
            }
            else
            {
                throw new Exception("Unable to retrieve the clamping configuration pair");
            }
        }


        private void ComputeCADFeatures()
        {
            if (m_cadFeatures == null)
            {
                // Create CAM Session
                Utilities.RemoteSession.NXSession.CreateCamSession();

                string customDir = Environment.GetEnvironmentVariable("MISUMI_CAM_CUSTOM");
                if (customDir != null)
                {
                    if (m_manager.CAMFixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_3)
                    {
                        string camMachineName = customDir + "\\" + "DLO_DOOSAN_3_AXIS";
                        Utilities.RemoteSession.NXSession.CAMSession.SpecifyConfiguration(camMachineName);
                    }
                    else if (m_manager.CAMFixtureHandler.GetFixtureType() == Utilities.CAMSingleFixtureHandler.FixtureType.AXIS_5)
                    {
                        string camMachineName = customDir + "\\" + "DLO_DMU50_5_AXIS";
                        Utilities.RemoteSession.NXSession.CAMSession.SpecifyConfiguration(camMachineName);
                    }
                }

                // Switch to Manufacturing and create CAM setup
                // Template does not matter here since we only need to detect features
                Utilities.RemoteSession.NXSession.ApplicationSwitchImmediate("UG_APP_MANUFACTURING");
                m_part.CreateCamSetup("mill_contour");

                // Detect features
                m_cadFeatures = CAMFeatureHandler.DetectFeatures(new Body[] { m_body }, out bool isBlankFeatureDetectionComplete);

                // Switch back to Modeling
                Utilities.RemoteSession.NXSession.ApplicationSwitchImmediate("UG_APP_MODELING");
            }
        }


        private CAMFeature[] m_cadFeatures;
    }
}