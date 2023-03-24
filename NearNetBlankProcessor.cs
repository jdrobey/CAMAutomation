using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Configuration;

using NXOpen;
using NXOpen.Features;
using NXOpen.Preferences;
using NXOpen.GeometricUtilities;

namespace CAMAutomation
{
    public class NearNetBlankProcessor
    {
        public enum Status { FAILED = 0, SKIPPED = 1, SUCCESS = 2 }


        public NearNetBlankProcessor(Part cadPart, Stock candidateStock, StockPlate candidatePlate)
        {
            m_cadPart = cadPart;
            m_candidateStock = candidateStock;
            m_candidatePlate = candidatePlate;

            m_boundaryCurves = new List<CurveLoop>();
            m_garbageCollector = new List<TaggedObject>();

            m_manager = CAMAutomationManager.GetInstance();
        }


        ~NearNetBlankProcessor()
        {
            // empty
        }


        public Part Execute(out Status status, out string msg)
        {
            try
            {
                // Check for CAD part and candidate Stock Plate
                CheckInputs();

                // Output Directory
                RetrieveOutputDirectory();

                // Get Near Net Blank Parameters
                GetNearNetBlankParameters();

                // Create Blank part
                CreateBlankPart();

                // Flag Initial Features for deletion
                FlagInitialFeaturesForDeletion();

                // Retrieve CAD body
                RetrieveCADBody();

                // Retrieve Near Net Blank Clamping Csys
                RetrieveNearNetBlankClampingCsys();

                // Compute CAD Bounding Box
                ComputeCADBoundingBox();

                // Orient View For Boundary Curves
                OrientViewForBoundaryCurves();

                // Create Boundary Curves
                CreateBoundaryCurves();

                // Offset Boundary Curves
                OffsetBoundaryCurves();

                // Extrude Boundary Curves
                ExtrudeBoundaryCurves();

                // Do we need to skip the execution ?
                if (!ShouldSkipExecution(out string reason))
                {
                    // Create DXF file
                    CreateDXFFile();

                    // Move Blank Body to Absolute Csys
                    MoveBlankBodyToAbsoluteCsys();

                    // Empty garbage Collector
                    EmptyGarbageCollector();

                    // Save blank part
                    SaveBlankPart();

                    status = Status.SUCCESS;
                    msg = String.Empty;

                    return m_blankPart;
                }
                else
                {
                    // Delete Blank Part
                    DeleteBlankPart();

                    status = Status.SKIPPED;
                    msg = reason;
                }
            }
            catch (Exception ex)
            {
                // Delete Blank Part
                DeleteBlankPart();

                status = Status.FAILED;
                msg = ex.Message;
            }

            return null;
        }


        private void CheckInputs()
        {
            // CAD part check
            if (m_cadPart == null)
            {
                throw new Exception("CAD part cannot be null");
            }

            // Stock plate check
            if (m_candidatePlate == null)
            {
                throw new Exception("Candidate stock plate cannot be null");
            }
            else if (m_candidatePlate.GetThickness(true) <= 0.0)
            {
                throw new Exception("Candidate stock plate thickness should be greater than 0.0");
            }
        }


        private void RetrieveOutputDirectory()
        {
            m_OutputDirectory = Path.GetDirectoryName(m_cadPart.FullPath);
        }


        private void GetNearNetBlankParameters()
        {
            // Retrieve variable "MISUMI_NEARNETBLANK_VOLUME_GAIN"
            string nearNetBlankVolumeGainStr = ConfigurationManager.AppSettings["MISUMI_NEARNETBLANK_VOLUME_GAIN"];
            if (nearNetBlankVolumeGainStr == null || !double.TryParse(nearNetBlankVolumeGainStr, out m_nearNetBlankVolumeGain) || m_nearNetBlankVolumeGain <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_NEARNETBLANK_VOLUME_GAIN");
            }

            // Retrieve variable "MISUMI_NEARNETBLANK_EXTRA_MATERIAL_INSIDE"
            string nearNetBlankExtraMaterialInsideStr = ConfigurationManager.AppSettings["MISUMI_NEARNETBLANK_EXTRA_MATERIAL_INSIDE"];
            if (nearNetBlankExtraMaterialInsideStr == null || !double.TryParse(nearNetBlankExtraMaterialInsideStr, out m_nearNetBlankExtraMaterialInside) || m_nearNetBlankExtraMaterialInside <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_NEARNETBLANK_EXTRA_MATERIAL_INSIDE");
            }

            // Retrieve variable "MISUMI_NEARNETBLANK_EXTRA_MATERIAL_OUTSIDE"
            string nearNetBlankExtraMaterialOutsideStr = ConfigurationManager.AppSettings["MISUMI_NEARNETBLANK_EXTRA_MATERIAL_OUTSIDE"];
            if (nearNetBlankExtraMaterialOutsideStr == null || !double.TryParse(nearNetBlankExtraMaterialOutsideStr, out m_nearNetBlankExtraMaterialOutside) || m_nearNetBlankExtraMaterialOutside <= 0.0)
            {
                throw new Exception("Invalid value for environment variable MISUMI_NEARNETBLANK_EXTRA_MATERIAL_OUTSIDE");
            }
        }


        private void CreateBlankPart()
        {
            // Copy cadPart to a new file. This file will become the blank part
            string blankpath = Path.Combine(m_OutputDirectory, "Blank.prt");
            File.Copy(m_cadPart.FullPath, blankpath);

            // Open the blank part
            m_blankPart = Utilities.RemoteSession.NXSession.Parts.OpenDisplay(blankpath, out PartLoadStatus status);

            if (m_blankPart == null)
            {
                throw new Exception("Unable to open the Blank part file");
            }

            // Switch to Modeling application
            Utilities.RemoteSession.NXSession.ApplicationSwitchImmediate("UG_APP_MODELING");
        }


        private void FlagInitialFeaturesForDeletion()
        {
            m_garbageCollector.AddRange(m_blankPart.Features.ToArray());
        }


        private void RetrieveCADBody()
        {
            // Retrieve solid body
            m_cadBody = m_blankPart.Bodies.ToArray().Where(s => s.IsSolidBody).FirstOrDefault();

            if (m_cadBody == null)
            {
                throw new Exception("Unable to retrieve the CAD body");
            }
        }


        private void RetrieveNearNetBlankClampingCsys()
        {
            // The Near Net Blank Clamping Csys will be the same as the CAD_CLAMPING_CSYS
            DatumCsys nearNetBlankClampingDatumCsys = Utilities.NXOpenUtils.GetDatumCsysByName(m_blankPart, "CAD_CSYS_CLAMPING_NEAR_NET_BLANK");

            if (nearNetBlankClampingDatumCsys != null)
            {
                m_nearNetBlankClampingCsys = Utilities.NXOpenUtils.GetCsysFromDatum(nearNetBlankClampingDatumCsys);
            }
            else
            {
                throw new Exception("Unable to retrieve the CAD_CSYS_CLAMPING_NEAR_NET_BLANK");
            }
        }


        private void ComputeCADBoundingBox()
        {
            m_boundingBox = BoundingBox.ComputeBodyBoundingBox(m_cadBody, m_nearNetBlankClampingCsys);

            if (m_boundingBox == null)
            {
                throw new Exception("Unable to compute the bounding box of the CAD part");
            }
        }


        private void OrientViewForBoundaryCurves()
        {
            // Retrieve the orientation matrix, depending on the shortest side of the bounding box
            Matrix3x3 orientation = new Matrix3x3();
            if (m_boundingBox.XLength <= m_boundingBox.YLength && m_boundingBox.XLength <= m_boundingBox.ZLength)
            {
                // Rotate about Y axis, so X+ becomes Z+

                orientation.Xx = -m_boundingBox.ZUnitDirection.X;
                orientation.Xy = -m_boundingBox.ZUnitDirection.Y;
                orientation.Xz = -m_boundingBox.ZUnitDirection.Z;

                orientation.Yx = m_boundingBox.YUnitDirection.X;
                orientation.Yy = m_boundingBox.YUnitDirection.Y;
                orientation.Yz = m_boundingBox.YUnitDirection.Z;

                orientation.Zx = m_boundingBox.XUnitDirection.X;
                orientation.Zy = m_boundingBox.XUnitDirection.Y;
                orientation.Zz = m_boundingBox.XUnitDirection.Z;

                // Define the extrude direction.
                m_extrudeDirection = m_boundingBox.XUnitDirection;
            }
            else if (m_boundingBox.YLength <= m_boundingBox.XLength && m_boundingBox.YLength <= m_boundingBox.ZLength)
            {
                // Rotate about X axis, so Y+ becomes Z+

                orientation.Xx = m_boundingBox.XUnitDirection.X;
                orientation.Xy = m_boundingBox.XUnitDirection.Y;
                orientation.Xz = m_boundingBox.XUnitDirection.Z;

                orientation.Yx = -m_boundingBox.ZUnitDirection.X;
                orientation.Yy = -m_boundingBox.ZUnitDirection.Y;
                orientation.Yz = -m_boundingBox.ZUnitDirection.Z;

                orientation.Zx = m_boundingBox.YUnitDirection.X;
                orientation.Zy = m_boundingBox.YUnitDirection.Y;
                orientation.Zz = m_boundingBox.YUnitDirection.Z;

                // Define the extrude direction.
                m_extrudeDirection = m_boundingBox.YUnitDirection;
            }
            else if (m_boundingBox.ZLength <= m_boundingBox.XLength && m_boundingBox.ZLength <= m_boundingBox.YLength)
            {
                // No rotation

                orientation.Xx = m_boundingBox.XUnitDirection.X;
                orientation.Xy = m_boundingBox.XUnitDirection.Y;
                orientation.Xz = m_boundingBox.XUnitDirection.Z;

                orientation.Yx = m_boundingBox.YUnitDirection.X;
                orientation.Yy = m_boundingBox.YUnitDirection.Y;
                orientation.Yz = m_boundingBox.YUnitDirection.Z;

                orientation.Zx = m_boundingBox.ZUnitDirection.X;
                orientation.Zy = m_boundingBox.ZUnitDirection.Y;
                orientation.Zz = m_boundingBox.ZUnitDirection.Z;

                // Define the extrude direction.
                m_extrudeDirection = m_boundingBox.ZUnitDirection;
            }

            // Orient the view
            m_blankPart.Views.WorkView.Orient(orientation);
        }


        private void CreateBoundaryCurves()
        {
            // Set Shading Options so Shadow Outline will work
            SetShadingOptions();

            Tag[] bodiesTag = new Tag[] { m_cadBody.Tag };
            Tag viewTag = m_blankPart.ModelingViews.WorkView.Tag;
            double tolerance = 0.01;
            double factor = m_blankPart.PartUnits == BasePart.Units.Inches ? 1.0 : 25.4;

            Utilities.RemoteSession.UFSession.Curve.CreateShadowOutline(bodiesTag.Length,
                                                                        bodiesTag,
                                                                        viewTag,
                                                                        out int loop_count,
                                                                        out int[] countarray,
                                                                        out Tag[][] curve_array,
                                                                        new double[] { tolerance * factor });

            foreach (Tag[] curvesTag in curve_array)
            {
                // Retrieve curves forming a loop and create CurveLoop objects
                Curve[] curves = curvesTag.Select(p => Utilities.RemoteSession.NXSession.GetObjectManager().GetTaggedObject(p) as Curve).ToArray();
                CurveLoop loop = CurveLoop.CreateCurveLoop(curves);

                if (loop != null)
                {
                    m_boundaryCurves.Add(loop);
                }
                else
                {
                    throw new Exception("Cannot extract profile curves");
                }
            }
        }


        private void SetShadingOptions()
        {
            ViewVisualizationVisual.DisplayAppearanceOptions options = new ViewVisualizationVisual.DisplayAppearanceOptions();

            options.RenderingStyle = ViewVisualizationVisual.RenderingStyle.Shaded;
            options.HiddenEdges = ViewVisualizationVisual.HiddenEdges.Invisible;
            options.Silhouettes = true;
            options.SmoothEdges = false;
            options.SmoothEdgeColor = 0;
            options.SmoothEdgeFont = ViewVisualizationVisual.SmoothEdgeFont.Original;
            options.SmoothEdgeWidth = ViewVisualizationVisual.SmoothEdgeWidth.Original;
            options.SmoothEdgeAngleTolerance = 0.2;

            m_blankPart.ModelingViews.WorkView.VisualizationVisualPreferences.DisplayAppearance = options;
            m_blankPart.ModelingViews.WorkView.ChangePerspective(false);
            m_blankPart.ModelingViews.WorkView.RenderingStyle = View.RenderingStyleType.ShadedWithEdges;
        }


        private void OffsetBoundaryCurves()
        {
            // Retrieve internal and external offsets
            double factor = m_blankPart.PartUnits == BasePart.Units.Inches ? 1.0 : 25.4;
            double externalOffset = m_nearNetBlankExtraMaterialOutside * factor;
            double internalOffset = m_nearNetBlankExtraMaterialInside * factor;

            // Sort Boundary Curves by Area (decreasing order)
            m_boundaryCurves = m_boundaryCurves.OrderByDescending(p => p.GetArea()).ToList();

            // Expand the boundary curve (i.e., first item) and contract the other
            m_boundaryCurves = m_boundaryCurves.Select((p, i) => i == 0 ? p.Expand(externalOffset) : p.Contract(internalOffset)).ToList();

            if (m_boundaryCurves.Any(p => p == null))
            {
                throw new Exception("Unable to offset all the boundary curves");
            }
        }


        private void ExtrudeBoundaryCurves()
        {
            // Create an undo mark
            string undoMarkName = "Extrude";
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, undoMarkName);

            ExtrudeBuilder builder = m_blankPart.Features.CreateExtrudeBuilder(null);

            try
            {
                // Retrieve all the curves that need to be extruded
                Curve[] curves = m_boundaryCurves.SelectMany(s => s.GetCurves()).ToArray();

                // Add the curves
                SelectionIntentRule[] rule = new SelectionIntentRule[] { (m_blankPart as BasePart).ScRuleFactory.CreateRuleCurveDumb(curves) };
                Section section = m_blankPart.Sections.CreateSection();
                section.AllowSelfIntersection(true);
                section.AddToSection(rule, null, null, null, new Point3d(), Section.Mode.Create, false);
                builder.Section = section;

                // Extrusion start/end limits
                double plateThickness = m_candidatePlate.GetThickness(true);   // Plate thickness is already in the CAD part unit
                builder.Limits.StartExtend.TrimType = Extend.ExtendType.Value;
                builder.Limits.StartExtend.Value.RightHandSide = "0.0";
                builder.Limits.EndExtend.TrimType = Extend.ExtendType.Value;
                builder.Limits.EndExtend.Value.RightHandSide = plateThickness.ToString();

                // Extrusion direction
                Direction dir = m_blankPart.Directions.CreateDirection(new Point3d(), m_extrudeDirection, SmartObject.UpdateOption.WithinModeling);
                builder.Direction = dir;

                // Commit the builder
                Extrude feature = builder.CommitFeature() as Extrude;

                if (feature != null)
                {
                    Body[] bodies = feature.GetBodies();

                    if (bodies.Length == 1)
                    {
                        m_blankBody = bodies.First();
                    }
                }
            }
            catch (Exception)
            {
                // empty
            }
            finally
            {
                builder.Destroy();
            }

            if (m_blankBody == null)
            {
                // At this point, the Extrude fails for a given reason. 
                // We undo the NX session, so all remaining traces of the Extrude operation are deleted
                Utilities.RemoteSession.NXSession.UndoToMark(undoMark, undoMarkName);
                Utilities.RemoteSession.NXSession.DeleteUndoMark(undoMark, undoMarkName);

                throw new Exception("Unable to extrude the boundary curves");
            }
        }


        private bool ShouldSkipExecution(out string reason)
        {
            if (m_candidateStock != null)
            {
                // Compute the volume of the Stock, Blank and CAD bodies
                double stockVolume = m_candidateStock.ComputeVolume(true);
                double blankBodyVolume = Utilities.NXOpenUtils.GetVolume(m_blankBody);
                double cadBodyVolume = Utilities.NXOpenUtils.GetVolume(m_cadBody);

                // Compute the percentage gain in volume
                double ratioStock = (stockVolume - cadBodyVolume) / cadBodyVolume;
                double ratioNearNetBlank = (blankBodyVolume - cadBodyVolume) / cadBodyVolume;
                double percentageGain = (ratioStock - ratioNearNetBlank) * 100.0;

                if (percentageGain < m_nearNetBlankVolumeGain)
                {
                    reason = "Gain in volume with near net blank is below threshold value. " +
                             "Threshold value : " + m_nearNetBlankVolumeGain + " %. " +
                             "Actual gain : " + Math.Round(percentageGain, 3) + " %.";

                    return true;
                }
            }

            reason = string.Empty;
            return false;
        }


        private void CreateDXFFile()
        {           
            DxfdwgCreator builder = Utilities.RemoteSession.NXSession.DexManager.CreateDxfdwgCreator();

            try
            {
                // Save the view under a name
                View view = m_blankPart.Views.SaveAsPreservingCase(m_blankPart.ModelingViews.WorkView, "DXF_Export", true, false);

                // Output files
                DXFFilePath = Path.Combine(m_OutputDirectory, m_blankPart.Leaf) + ".dxf";
                builder.OutputFileType = DxfdwgCreator.OutputFileTypeOption.Dxf;
                builder.OutputFile = DXFFilePath;

                // Add curves
                builder.ExportSelectionBlock.SelectionScope = ObjectSelector.Scope.SelectedObjects;
                builder.ExportSelectionBlock.SelectionComp.Add(m_boundaryCurves.SelectMany(p => p.GetCurves()).ToArray());

                // Options
                builder.WidthFactorMode = DxfdwgCreator.WidthfactorMethodOptions.AutomaticCalculation;
                builder.ViewList = view.Name;
                builder.LayerMask = "1-256";
                builder.ProcessHoldFlag = true;

                // Commit the builder
                builder.Commit();
            }
            catch (Exception ex)
            {
                // We just log a warning, so the blank part creation proceed
                m_manager.LogFile.AddWarning("Unable to create the DXF file: " + ex.Message);
            }
            finally
            {
                // Destroy the builder
                builder.Destroy();
            }
        }


        private void MoveBlankBodyToAbsoluteCsys()
        {
            // Compute the Bounding Box of the Blank Body
            BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(m_blankBody, m_nearNetBlankClampingCsys);

            // The source Csys will be the Bounding Box Csys
            // The target Csys will be the Absolute Coordinate System
            CartesianCoordinateSystem sourceCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(boundingBox.MinCornerPoint, boundingBox.XUnitDirection, boundingBox.YUnitDirection);
            CartesianCoordinateSystem targetCsys = Utilities.NXOpenUtils.CreateTemporaryCsys(new Point3d(), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0));

            if (!Utilities.NXOpenUtils.MoveBody(m_blankBody, sourceCsys, targetCsys))
            {
                throw new Exception("Unable to move the Blank body");
            }
        }


        private void EmptyGarbageCollector()
        {
            // This will delete all features that were existing in the original CAD part
            Utilities.NXOpenUtils.DeleteNXObjects(m_garbageCollector.ToArray());
        }


        private void SaveBlankPart()
        {
            m_blankPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private void DeleteBlankPart()
        {
            if (m_blankPart != null)
            {
                string blankPath = m_blankPart.FullPath;

                // Close the Blank part
                PartCloseResponses response = null;
                m_blankPart.Close(BasePart.CloseWholeTree.True, BasePart.CloseModified.CloseModified, response);

                // Delete the blank part file
                File.Delete(blankPath);
            }
        }


        private string m_OutputDirectory;

        private Part m_cadPart;
        private Part m_blankPart;

        private Body m_cadBody;
        private Body m_blankBody;

        private Stock m_candidateStock;
        private StockPlate m_candidatePlate;

        private CartesianCoordinateSystem m_nearNetBlankClampingCsys;

        private List<CurveLoop> m_boundaryCurves;
        private List<TaggedObject> m_garbageCollector;

        private BoundingBox m_boundingBox;
        private Vector3d m_extrudeDirection;

        private double m_nearNetBlankVolumeGain;
        private double m_nearNetBlankExtraMaterialInside;
        private double m_nearNetBlankExtraMaterialOutside;

        public string DXFFilePath { get; private set; }
        public double Area { get; private set; }
        public double Perimeter { get; private set; }

        private CAMAutomationManager m_manager;
    }
}
