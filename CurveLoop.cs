using System;
using System.Collections.Generic;
using System.Linq;

using NXOpen;
using NXOpen.UF;
using NXOpen.Features;

namespace CAMAutomation
{
    public class CurveLoop
    {
        public static CurveLoop CreateCurveLoop(Curve[] curves)
        {
            if (curves.Length > 0)
            {
                // Are all curves coming from the same part ?
                if (AreAllCurvesInSamePart(curves))
                {
                    if (curves.Length == 1)
                    {
                        // Is the curve closed ?
                        if (IsCurveClosed(curves.First()))
                        {
                            return new CurveLoop(curves);
                        }
                    }
                    else if (curves.Length > 1)
                    {
                        // Are the curves forming a loop ?
                        if (AreCurvesLoop(curves))
                        {
                            return new CurveLoop(curves);
                        }
                    }
                }
            }

            return null;
        }


        public static bool AreAllCurvesInSamePart(Curve[] curves)
        {
            Part[] owningParts = curves.Select(p => p.OwningPart as Part).ToArray();
            Part owningPart = owningParts.First();

            return owningPart != null && owningParts.All(p => p == owningPart);
        }


        public static bool IsCurveClosed(Curve curve)
        {
            try
            {
                double startParam = 0.0;
                double endParam = 1.0;

                int flag = UFConstants.UF_MODL_LOC;

                double[] startPoint = new double[3];
                double[] endPoint = new double[3];

                Utilities.RemoteSession.UFSession.Modl.EvaluateCurve(curve.Tag, ref startParam, ref flag, startPoint);
                Utilities.RemoteSession.UFSession.Modl.EvaluateCurve(curve.Tag, ref endParam, ref flag, endPoint);

                return Utilities.MathUtils.AreCoincident(new Point3d(startPoint[0], startPoint[1], startPoint[2]),
                                                         new Point3d(endPoint[0], endPoint[1], endPoint[2]));
            }
            catch (Exception)
            {
                return false;
            }
        }


        public static bool AreCurvesLoop(Curve[] curves)
        {
            // Create an undo mark
            string undoMarkName = "Join_Curves";
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, undoMarkName);

            try
            {
                int numInputCurves = curves.Length;
                Tag[] inputCurves = curves.Select(p => p.Tag).ToArray();

                int numJoinedCurves = 0;
                Tag[] joinedCurves = new Tag[numInputCurves];

                Utilities.RemoteSession.UFSession.Curve.AutoJoinCurves(inputCurves, numInputCurves, 2, joinedCurves, out numJoinedCurves);

                // The curves will be considered as a loop if there is only one joined curves and if the latter is closed
                return numJoinedCurves == 1 && 
                       IsCurveClosed(Utilities.RemoteSession.NXSession.GetObjectManager().GetTaggedObject(joinedCurves.First()) as Curve);   
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Undo to the undo mark and delete the joined curve
                Utilities.RemoteSession.NXSession.UndoToMark(undoMark, undoMarkName);
                Utilities.RemoteSession.NXSession.DeleteUndoMark(undoMark, undoMarkName);
            }
        }


        private CurveLoop(Curve[] curves)
        {
            m_curves = curves.ToArray();

            m_OwningPart = curves.First().OwningPart as Part;

            m_IsAreaComputed = false;
        }


        ~CurveLoop()
        {
            // empty
        }


        public Curve[] GetCurves()
        {
            return m_curves.ToArray();
        }


        public double GetLength()
        {
            return m_curves.Sum(p => p.GetLength());
        }


        public double GetArea()
        {
            if (!m_IsAreaComputed)
            {
                // Fill surface the curve loop
                Face face = CreateFillSurface();
                if (face!= null)
                {
                    // Compute the Area of the Fill surface (face)
                    m_Area = Utilities.NXOpenUtils.GetArea(face);
                    m_IsAreaComputed = true;

                    //Delete the Fill surface object (face)
                    Utilities.NXOpenUtils.DeleteNXObject(face.GetBody());
                }
            }

            return m_Area;
        }


        private Face CreateFillSurface()
        {
            // Create an undo mark
            string undoMarkName = "Fill_Surface";
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, undoMarkName);

            FillHoleBuilder builder = m_OwningPart.Features.FreeformSurfaceCollection.CreateFillHoleBuilder(null);

            try
            {
                // Method
                builder.ShapeControlType = FillHoleBuilder.ShapeControlTypes.None;

                // Tolerances
                double factor = m_OwningPart.PartUnits == BasePart.Units.Inches ? 1.0 : 25.4;
                builder.Tolerance = 0.0004 * factor;
                builder.CurveChain.DistanceTolerance = 0.0004 * factor;
                builder.CurveChain.ChainingTolerance = 0.0004 * factor;
                builder.SelectPassThrougCurves.DistanceTolerance = 0.0004 * factor;
                builder.SelectPassThrougCurves.ChainingTolerance = 0.0004 * factor;

                // Options
                builder.CurveChain.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves);
                builder.CurveChain.AllowSelfIntersection(true);

                // Add the curves
                CurveDumbRule rule = (m_OwningPart as BasePart).ScRuleFactory.CreateRuleCurveDumb(m_curves);
                builder.CurveChain.AddToSection(new SelectionIntentRule[] { rule }, null, null, null, new Point3d(), Section.Mode.Create, false);

                // Commit the builder
                FillHole feature = builder.CommitFeature() as FillHole;

                // Retrieve the created body
                if (feature != null)
                {
                    Face[] faces = feature.GetFaces();
                    if (faces.Length == 1)
                    {
                        return faces.First();
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

            // At this point, the Fill Surface fails for a given reason. 
            // We undo the NX session, so all remaining traces of the Fill Surface operation are deleted
            Utilities.RemoteSession.NXSession.UndoToMark(undoMark, undoMarkName);
            Utilities.RemoteSession.NXSession.DeleteUndoMark(undoMark, undoMarkName);

            return null;
        }


        public CurveLoop Contract(double offset)
        {
            return Offset(offset, (a, b) => a.CompareTo(b));
        }


        public CurveLoop Expand(double offset)
        {
            return Offset(offset, (a, b) => b.CompareTo(a));
        }


        private CurveLoop Offset(double offset, Comparison<double> comparison)
        {
            // Offset the curve loop in the 2 directions
            CurveLoop loop1 = Offset(offset, true);
            CurveLoop loop2 = Offset(offset, false);

            if (loop1 != null && loop2 != null)
            {
                // Put the 2 loops in an array
                CurveLoop[] loops = new CurveLoop[] { loop1, loop2 };

                // Order the array with the custom comparison<double>
                loops = loops.OrderBy(p => p.GetLength(), Comparer<double>.Create(comparison)).ToArray();

                // The loopToKeep will be the first item in the array
                // The loopToDelete will be the second item in the array
                CurveLoop loopToKeep = loops[0];
                CurveLoop loopToDelete = loops[1];

                // Delete the CurveLoop to delete
                loopToDelete.Delete();

                // Return the one to keep
                return loopToKeep;
            }
            else
            {
                return null;
            }
        }


        private CurveLoop Offset(double offset, bool reverseDir)
        {
            // Create an undo mark
            string undoMarkName = "Offset_Curve";
            Session.UndoMarkId undoMark = Utilities.RemoteSession.NXSession.SetUndoMark(Session.MarkVisibility.Invisible, undoMarkName);

            OffsetCurveBuilder builder = m_OwningPart.Features.CreateOffsetCurveBuilder(null);

            try
            {
                // Options
                builder.CurveFitData.Tolerance = 0.004;
                builder.CurveFitData.AngleTolerance = 0.5;
                builder.OffsetDistance.RightHandSide = offset.ToString();
                builder.ReverseDirection = reverseDir;
                builder.InputCurvesOptions.InputCurveOption = NXOpen.GeometricUtilities.CurveOptions.InputCurve.Blank;
                builder.TrimMethod = OffsetCurveBuilder.TrimOption.ExtendTangents;
                builder.CurvesToOffset.AllowSelfIntersection(true);
                builder.InputCurvesOptions.Associative = true;
                builder.RoughOffset = false;

                // Add the curves
                CurveDumbRule rule = (m_OwningPart as BasePart).ScRuleFactory.CreateRuleCurveDumb(m_curves);
                builder.CurvesToOffset.AddToSection(new SelectionIntentRule[] { rule }, null, null, null, new Point3d(), Section.Mode.Create, false);

                // Commit the builder
                OffsetCurve feature = builder.CommitFeature() as OffsetCurve;

                if (feature != null)
                {
                    // Retrieve the new created curves
                    Curve[] newCurves = feature.GetEntities().Select(p => p as Curve).Where(p => p != null).ToArray();

                    // Create the new offset CurveLoop
                    return CreateCurveLoop(newCurves);
                }
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

            // At this point, the Offset Curve fails for a given reason. 
            // We undo the NX session, so all remaining traces of the Offset Curve operation are deleted
            Utilities.RemoteSession.NXSession.UndoToMark(undoMark, undoMarkName);
            Utilities.RemoteSession.NXSession.DeleteUndoMark(undoMark, undoMarkName);

            return null;
        }


        public void Delete()
        {
            Utilities.NXOpenUtils.DeleteNXObjects(m_curves);
            m_curves = null;
        }


        private Curve[] m_curves;

        private Part m_OwningPart;

        private bool m_IsAreaComputed;
        private double m_Area;
    }
}

