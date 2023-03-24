using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Configuration;

using NXOpen;
using NXOpen.Features;

namespace CAMAutomation
{
    public class BlankPreprocessor
    {
        public BlankPreprocessor(Part cadPart)
        {
            m_cadPart = cadPart;
            m_units = cadPart.PartUnits;

            m_manager = CAMAutomationManager.GetInstance();

            m_stocks = new List<Stock>();
        }


        ~BlankPreprocessor()
        {
            // empty
        }


        public Part CreateBlank()
        {
            try
            {
                // Parse Input File
                ParseInputFile();

                // Retrieve CAD Body
                RetrieveCADBody();

                // Retrieve Minimum Extra Material
                RetrieveMinimumExtraMaterial();

                // Retrieve Clamping Extra Material
                RetrieveClampingExtraMaterial();

                // Retrieve Stock Bar Min Area Option
                RetrieveStockBarMinAreaOption();

                // Retrieve the Candidate Stock (Block + Bar)
                RetrieveCandidateStock();

                // RetrieveCandidatePlate (for Near Net Blank)
                RetrieveCandidatePlate();

                // Check if it is possible to proceed
                if (CanProceed())
                {
                    // Create the Blank Part
                    CreateBlankPart();

                    // Report Best Candidate Stock to CAMReport
                    ReportBestCandidateStock();

                    // Save Blank
                    SaveBlank();
                }

                return m_blankPart;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message);

                return null;
            }
        }


        private void ParseInputFile()
        {
            //Clear the Stock Library
            m_stocks.Clear();

            // Load the document
            XmlDocument doc = new XmlDocument();
            doc.Load(m_manager.InputFile);

            // Retrieve the root
            XmlElement root = doc.DocumentElement;

            // Parse Bars
            ParseBars(root);

            // Parse Blocks
            ParseBlocks(root);

            // Parse Plates
            ParsePlates(root);
        }


        private void ParseBars(XmlNode root)
        {
            foreach (XmlNode node in root.SelectNodes("Stock//Bars//Bar"))
            {
                StockBar bar = new StockBar();

                ParseStockPrismData(bar, node);

                m_stocks.Add(bar);
            }
        }


        private void ParseBlocks(XmlNode root)
        {
            foreach (XmlNode node in root.SelectNodes("Stock//Blocks//Block"))
            {
                StockBlock block = new StockBlock();

                ParseStockPrismData(block, node);

                m_stocks.Add(block);
            }
        }


        private void ParsePlates(XmlNode root)
        {
            foreach (XmlNode node in root.SelectNodes("Stock//Plates//Plate"))
            {
                StockPlate plate = new StockPlate();

                ParseStockPrismData(plate, node);

                m_stocks.Add(plate);
            }
        }


        private void ParseStockData(Stock stock, XmlNode node)
        {
            // ID
            XmlNode idNode = node.SelectSingleNode("ID");
            if (idNode != null)
            {
                stock.Id = idNode.InnerText;

                if (m_stocks.Any(p => p.Id == stock.Id))
                {
                    throw new Exception("Stock Library: Id " + stock.Id + " is already used");
                }
            }
            else
            {
                throw new Exception("Stock Library: Id not found in node <" + node.Name + ">");
            }

            // Name
            XmlNode nameNode = node.SelectSingleNode("Name");
            if (nameNode != null)
            {
                stock.Name = nameNode.InnerText;
            }
            else
            {
                throw new Exception("Stock Library: Name not found in node <" + node.Name + ">");
            }

            // Material
            XmlNode materialNode = node.SelectSingleNode("Material");
            if (materialNode != null)
            {
                stock.Material = materialNode.InnerText;
            }
            else
            {
                throw new Exception("Stock Library: Material not found in node <" + node.Name + ">");
            }
        }


        private void ParseStockPrismData(StockPrism prism, XmlNode node)
        {
            ParseStockData(prism, node);

            // Dimension 1
            XmlNode dim1Node = node.SelectSingleNode("Dim1");
            if (dim1Node != null)
            {
                ParseDimension(dim1Node, out double value, out double stkrm);

                prism.SetDimension1(value, stkrm);
            }

            // Dimension 2
            XmlNode dim2Node = node.SelectSingleNode("Dim2");
            if (dim2Node != null)
            {
                ParseDimension(dim2Node, out double value, out double stkrm);

                prism.SetDimension2(value, stkrm);
            }

            // Dimension 3
            XmlNode dim3Node = node.SelectSingleNode("Dim3");
            if (dim3Node != null)
            {
                ParseDimension(dim3Node, out double value, out double stkrm);

                prism.SetDimension3(value, stkrm);
            }
        }


        private void ParseDimension(XmlNode node, out double value, out double stkrm)
        {
            try
            {
                // Unit
                double factor = 1.0;
                XmlAttribute unitAttribute = node.Attributes["Unit"];
                if (unitAttribute != null)
                {
                    if (unitAttribute.Value == "in" && m_units == BasePart.Units.Millimeters)
                    {
                        factor = 25.4;
                    }
                    else if (unitAttribute.Value == "mm" && m_units == BasePart.Units.Inches)
                    {
                        factor = 1.0 / 25.4;
                    }
                }

                // Value
                XmlAttribute valueAttribute = node.Attributes["Value"];
                if (valueAttribute != null)
                {
                    if (Double.TryParse(valueAttribute.Value, out value))
                    {
                        if (value > 0.0)
                        {
                            value *= factor;
                        }
                        else
                        {
                            throw new Exception("Negative Value found in node <" + node.Name + ">");
                        }
                    }
                    else
                    {
                        throw new Exception("Invalid Value in node <" + node.Name + ">");
                    }
                }
                else
                {
                    throw new Exception("Value not found in node <" + node.Name + ">");
                }

                // StkRm
                XmlAttribute stkrmAttribute = node.Attributes["MinStkRm"];
                if (stkrmAttribute != null)
                {
                    if (Double.TryParse(stkrmAttribute.Value, out stkrm))
                    {
                        if (stkrm >= 0.0)
                        {
                            stkrm *= factor;

                            if (value - 2.0 * stkrm <= 0.0)
                            {
                                throw new Exception("Inconsistent MinStkRm found in node <" + node.Name + ">");
                            }
                        }
                        else
                        {
                            throw new Exception("Negative MinStkRm found in node <" + node.Name + ">");
                        }
                    }
                    else
                    {
                        throw new Exception("Invalid MinStkRm in node <" + node.Name + ">");
                    }
                }
                else
                {
                    throw new Exception("MinStkRm not found in node <" + node.Name + ">");
                }
            }
            catch (Exception ex)
            {
                value = 0.0;
                stkrm = 0.0;

                throw ex;
            }
        }


        private void RetrieveCADBody()
        {
            Body[] bodies = m_cadPart.Bodies.ToArray();
            if (bodies.Length != 1)
            {
                throw new Exception("Multiple bodies found in the part");
            }

            m_cadBody = bodies.First();
        }


        private void RetrieveMinimumExtraMaterial()
        {
            // Retrieve the min stock Removal for the third dimension for BAR or second and third dimension for PLATE
            string minExtraMaterialStr = ConfigurationManager.AppSettings["MISUMI_MIN_EXTRA_MATERIAL"];
            if (minExtraMaterialStr != null && double.TryParse(minExtraMaterialStr, out m_minExtraMaterial) && m_minExtraMaterial > 0.0)
            {
                // The value provided is in IN. Make the conversion if necessary
                if (m_units == BasePart.Units.Millimeters)
                {
                    m_minExtraMaterial *= 25.4;
                }
            }
            else
            {
                m_minExtraMaterial = 0.0;
            }
        }


        private void RetrieveClampingExtraMaterial()
        {
            // Retrieve the clamping extra material value and add it to the Z dimension
            string clampingExtraMaterialStr = ConfigurationManager.AppSettings["MISUMI_CLAMPING_EXTRA_MATERIAL"];
            if (clampingExtraMaterialStr != null && double.TryParse(clampingExtraMaterialStr, out m_clampingExtraMaterial) && m_clampingExtraMaterial > 0.0)
            {
                // The value provided is in INCH. Make the conversion if necessary
                if (m_units == BasePart.Units.Millimeters)
                {
                    m_clampingExtraMaterial *= 25.4;
                }
            }
            else
            {
                m_clampingExtraMaterial = 0.0;
            }
        }

        private void RetrieveStockBarMinAreaOption()
        {
            string stockBarMinAreaOptionStr = ConfigurationManager.AppSettings["MISUMI_STOCKBAR_MIN_AREA_OPTION"];

            if (stockBarMinAreaOptionStr != null && stockBarMinAreaOptionStr == "1")
            {
                m_stockBarMinAreaOption = true;
            }
            else
            {
                m_stockBarMinAreaOption = false;
            }
        }

        private void RetrieveCandidateStock()
        {
            // Compute the Bounding Box
            // We need to compute the bounding box along the CAD_CSYS_CLAMPING
            CartesianCoordinateSystem cadCsysClamping = Utilities.NXOpenUtils.GetCsysByName(m_cadPart, "CAD_CSYS_CLAMPING");

            if (cadCsysClamping != null)
            {
                BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(m_cadBody, cadCsysClamping);

                // Get Cad volume

                if (boundingBox != null)
                {
                    // Get the Bounding Box dimension
                    double X = boundingBox.XLength;
                    double Y = boundingBox.YLength;
                    double Z = boundingBox.ZLength;

                    // Update the Z dimension by adding the clamping extra material
                    Z += m_clampingExtraMaterial;

                    // Compute Volume
                    double volume = X * Y * Z;

                    // Filter Conditions for Bar and Block
                    Func<StockPrism, bool>[] barFilterConditions =
                    {
                        p => p is StockBar,
                        p => p.Material == m_manager.Material,
                        p => p.GetDimension1(false) >= X - Utilities.MathUtils.ABS_TOL,
                        p => p.GetDimension2(false) >= Y - Utilities.MathUtils.ABS_TOL,
                        p => p.GetDimension3(false) >= Z - Utilities.MathUtils.ABS_TOL,
                    };

                    Func<StockPrism, bool>[] blockFilterConditions =
                    {
                        p => p is StockBlock,
                        p => p.Material == m_manager.Material,
                        p => p.GetDimension1(false) >= X - Utilities.MathUtils.ABS_TOL,
                        p => p.GetDimension2(false) >= Y - Utilities.MathUtils.ABS_TOL,
                        p => p.GetDimension3(false) >= Z - Utilities.MathUtils.ABS_TOL,
                    };

                    // Sorting Conditions for Bar and Block
                    Func<StockPrism, double>[] barSortingConditions = null;
                    Func<StockPrism, double>[] blockSortingConditions =
                    {
                        p => p.ComputeVolume(true) - volume,
                        p => p.GetDimension1(true) - X,
                        p => p.GetDimension2(true) - Y,
                        p => p.GetDimension3(true) - Z,
                    };

                    if (m_stockBarMinAreaOption)
                    {
                        barSortingConditions = new Func<StockPrism, double>[]
                        {
                            p => (p as StockBar).GetArea(true),
                            p => p.GetDimension1(true) - X,
                            p => p.GetDimension2(true) - Y,
                            p => p.GetDimension3(true) - Z,
                        };
                    }
                    else
                    {
                        barSortingConditions = blockSortingConditions;
                    }

                    // Retrieve best StockBar and StockBlock
                    StockPrism[] prisms = m_stocks.Where(p => p is StockPrism).Select(p => p as StockPrism).ToArray();
                    Stock stockBarCandidate = RetrieveBestCandidateStockPrism(prisms, X, Y, Z, barFilterConditions, barSortingConditions);
                    Stock stockBlockCandidate = RetrieveBestCandidateStockPrism(prisms, X, Y, Z, blockFilterConditions, blockSortingConditions);
                    
                    // Prioritize Bars over Blocks
                    m_candidateStock = stockBarCandidate == null ? stockBlockCandidate : stockBarCandidate;
                }
                else
                {
                    throw new Exception("Unable to retrieve the CAD bounding box");
                }
            }
            else
            {
                throw new Exception("Unable to retrieve the CAD_CSYS_CLAMPING");
            }
        }

        private void RetrieveCandidatePlate()
        {
            // Candidate plate is retrieved only if Near Net Blank is allowed          
            if (m_manager.AllowNearNetBlank)
            {
                // Compute the Bounding Box
                // We need to compute the bounding box along the CAD_CSYS_CLAMPING
                CartesianCoordinateSystem cadCsysClamping = Utilities.NXOpenUtils.GetCsysByName(m_cadPart, "CAD_CSYS_CLAMPING_NEAR_NET_BLANK");

                if (cadCsysClamping != null)
                {
                    // Compute the minimum Bounding Box
                    BoundingBox boundingBox = BoundingBox.ComputeBodyBoundingBox(m_cadBody, cadCsysClamping);

                    if (boundingBox != null)
                    {
                        // Get the Bounding Box dimension
                        double X = boundingBox.XLength;
                        double Y = boundingBox.YLength;
                        double Z = boundingBox.ZLength;

                        // Compute Volume
                        double volume = X * Y * Z;

                        // Filter Conditions for Plate
                        Func<StockPrism, bool>[] plateFilterConditions =
                        {
                            p => p is StockPlate,
                            p => p.Material == m_manager.Material,
                            p => p.GetDimension1(false) >= X - Utilities.MathUtils.ABS_TOL,
                            p => p.GetDimension2(false) >= Y - Utilities.MathUtils.ABS_TOL,
                            p => p.GetDimension3(false) >= Z - Utilities.MathUtils.ABS_TOL,
                        };

                        // Sorting Conditions for Plate
                        Func<StockPrism, double>[] plateSortingConditions =
                        {
                            p => p.ComputeVolume(true) - volume,
                            p => p.GetDimension1(true) - X,
                            p => p.GetDimension2(true) - Y,
                            p => p.GetDimension3(true) - Z,
                        };

                        // Retrieve best StockPlate
                        StockPrism[] prisms = m_stocks.Where(p => p is StockPrism).Select(p => p as StockPrism).ToArray();
                        m_candidatePlate = RetrieveBestCandidateStockPrism(prisms, X, Y, Z, plateFilterConditions, plateSortingConditions);
                    }
                    else
                    {
                        throw new Exception("Unable to retrieve the CAD bounding box");
                    }
                }
                else
                {
                    throw new Exception("Unable to retrieve the CAD_CSYS_CLAMPING_NEAR_NET_BLANK");
                }
            }
        }


        private Stock RetrieveBestCandidateStockPrism(StockPrism[] prisms, double X, double Y, double Z, Func<StockPrism, bool>[] filterConditions, Func<StockPrism, double>[] sortingConditions)
        {
            // We will retrieve all Stock Prism
            // If Bar or Plate, we will apply cut lengths
            // Then, for each prism, we will get all possible orientations

            List<StockPrism> candidates = new List<StockPrism>();

            foreach (Stock prism in prisms)
            {
                if (prism is StockBlock)
                {
                    StockBlock block = prism as StockBlock;
                    candidates.AddRange(block.GetAllPossibleOrientations());
                }
                else if (prism is StockBar)
                {
                    StockBar bar = prism as StockBar;

                    double[] dimensions = { X, Y, Z };
                    for (int i = 0; i < dimensions.Length; ++i)
                    {
                        // Cut the Bar along 1 dimension
                        bar.ApplyCutLength(dimensions[i] + 2.0 * m_minExtraMaterial, m_minExtraMaterial);

                        // Find all possible orientations
                        candidates.AddRange(bar.GetAllPossibleOrientations());
                    }
                }
                else if (prism is StockPlate)
                {
                    StockPlate plate = prism as StockPlate;

                    double[] dimensions = { X, Y, Z };
                    for (int i = 0; i < dimensions.Length - 1; ++i)
                    {
                        for (int j = i + 1; j < dimensions.Length; ++j)
                        {
                            // Cut the plate along 2 dimensions
                            plate.ApplyCutLength1(dimensions[i] + 2.0 * m_minExtraMaterial, m_minExtraMaterial);
                            plate.ApplyCutLength2(dimensions[j] + 2.0 * m_minExtraMaterial, m_minExtraMaterial);

                            // Find all possible orientations
                            candidates.AddRange(plate.GetAllPossibleOrientations());
                        }
                    }
                }
            }

            return SortAndFilterArray(candidates.ToArray(), filterConditions, sortingConditions).FirstOrDefault();
        }


        private bool CanProceed()
        {
            // Is candidate found ?
            // If candidate stock is found, we proceed
            // If not, we proceed only if Near Net Blank approach is allowed and a candidate plate has been found 
            if (m_candidateStock == null)
            {
                // If Stock Prism
                if (!m_manager.AllowNearNetBlank)
                {
                    throw new Exception("There is no candidate stock for the given CAD part. " +
                                        "You may need to activate the Near Net Blank approach");
                }
                else if (m_candidatePlate == null)
                {
                    throw new Exception("There is no candidate stock for the given CAD part. " +
                                        "A Near Net Blank approach could be attempted if a candidate plate was found");
                }
            }

            return true;
        }


        private void CreateBlankPart()
        {
            // Can we use Near Net Blank approach ?
            if (m_manager.AllowNearNetBlank && m_candidatePlate != null)
            {
                // Create the blank with the Near Net Blank approach
                CreateBlankWithNearNetApproach();

                // Is the blank successfully created ?
                // If yes, the best candidate becomes the candidate plate
                if (m_blankPart != null)
                {
                    m_bestCandidate = m_candidatePlate;
                    m_manager.IsNearNetBlank = true;
                }
            }

            // If the blank was not created with the Near Net Blank approach, it will be created from regular stock
            // In that case, the best candidate becomes the candidate stock, provided a candidate stock has been found
            if (m_blankPart == null)
            {
                if (m_candidateStock != null)
                {
                    CreateBlankFromStock();
                    m_bestCandidate = m_candidateStock;
                }
                else
                {
                    throw new Exception("It is not possible to create the Blank part since no candidate stock was found");
                }
            }
        }


        private void CreateBlankWithNearNetApproach()
        {
            NearNetBlankProcessor processor = new NearNetBlankProcessor(m_cadPart, m_candidateStock, (StockPlate)m_candidatePlate);

            m_blankPart = processor.Execute(out NearNetBlankProcessor.Status status, out string msg);
            if (status == NearNetBlankProcessor.Status.SUCCESS)
            {
                m_manager.CAMReport.AddDXFFilePath(processor.DXFFilePath);

                if (m_units == BasePart.Units.Millimeters)
                {
                    m_manager.CAMReport.AddNearNetBlankArea(processor.Area, "mm2");
                    m_manager.CAMReport.AddNearNetBlankPerimeter(processor.Perimeter, "mm");
                }
                else if (m_units == BasePart.Units.Inches)
                {
                    m_manager.CAMReport.AddNearNetBlankArea(processor.Area, "in2");
                    m_manager.CAMReport.AddNearNetBlankPerimeter(processor.Perimeter, "in");
                }
            }
            else if (status == NearNetBlankProcessor.Status.SKIPPED)
            {
                m_manager.LogFile.AddMessage(msg);
            }
            else if (status == NearNetBlankProcessor.Status.FAILED)
            {
                throw new Exception("An error occurred when running the Near Net Blank processor: " + msg);
            }
        }


        private void CreateBlankFromStock()
        {
            // Create a new part
            string filename = Path.Combine(m_manager.OutputDirectory, "Blank.prt");
            m_blankPart = Utilities.NXOpenUtils.CreateNewCADPart(filename, false, m_units);

            if (m_blankPart != null)
            {
                if (m_candidateStock is StockPrism)
                {
                    StockPrism prism = m_candidateStock as StockPrism;

                    // Create the NX block
                    Point3d blockOrigin = new Point3d();
                    double X = prism.GetDimension1(true);
                    double Y = prism.GetDimension2(true);
                    double Z = prism.GetDimension3(true);
                    if (!CreateNXBlock(m_blankPart, blockOrigin, X, Y, Z))
                    {
                        throw new Exception("Unable to create the stock prism geometry in the Blank part");
                    }
                }
            }
            else
            {
                throw new Exception("Unable to create a new Blank part file");
            }
        }


        private bool CreateNXBlock(Part part, Point3d origin, double X, double Y, double Z)
        {
            BlockFeatureBuilder builder = part.Features.CreateBlockFeatureBuilder(null);

            try
            {
                // Type
                builder.Type = BlockFeatureBuilder.Types.OriginAndEdgeLengths;

                // Make Origin Associative
                // For that, we need to create a NX point through Scalars
                Scalar scalarX = part.Scalars.CreateScalar(origin.X, Scalar.DimensionalityType.Length, SmartObject.UpdateOption.WithinModeling);
                Scalar scalarY = part.Scalars.CreateScalar(origin.Y, Scalar.DimensionalityType.Length, SmartObject.UpdateOption.WithinModeling);
                Scalar scalarZ = part.Scalars.CreateScalar(origin.Z, Scalar.DimensionalityType.Length, SmartObject.UpdateOption.WithinModeling);
                builder.OriginPoint = part.Points.CreatePoint(scalarX, scalarY, scalarZ, SmartObject.UpdateOption.WithinModeling);
                builder.ParentAssociativity = true;

                // Origin and Lengths
                builder.SetOriginAndLengths(origin, X.ToString(), Y.ToString(), Z.ToString());

                //Commit the builder
                builder.CommitFeature();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Destroy the builder
                builder.Destroy();
            }
        }


        private void ReportBestCandidateStock()
        {
            if (m_units == BasePart.Units.Millimeters)
            {
                m_manager.CAMReport.AddBestCandidateStock(m_bestCandidate, "mm");
            }
            else if (m_units == BasePart.Units.Inches)
            {
                m_manager.CAMReport.AddBestCandidateStock(m_bestCandidate, "in");
            }
        }


        private void SaveBlank()
        {
            m_blankPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private T[] SortAndFilterArray<T>(T[] array, Func<T, bool>[] filterConditions, Func<T, double>[] sortingConditions)
        {
            IEnumerable<T> result = array;

            // Filter
            foreach (Func<T, bool> filter in filterConditions)
            {
                result = result.Where(filter);
            }

            // Sorting
            if (sortingConditions.Length > 0)
            {
                IOrderedEnumerable<T> orderedArray = result.OrderBy(sortingConditions.First());
                foreach (Func<T, double> sortingCondition in sortingConditions.Skip(1))
                {
                    orderedArray.ThenBy(sortingCondition);
                }

                result = orderedArray;
            }

            return result.ToArray();
        }


        private Part m_cadPart;
        private BasePart.Units m_units;

        private CAMAutomationManager m_manager;

        private List<Stock> m_stocks;

        private Body m_cadBody;

        private double m_minExtraMaterial;
        private double m_clampingExtraMaterial;
        private bool m_stockBarMinAreaOption;

        private Stock m_candidateStock;
        private Stock m_candidatePlate;
        private Stock m_bestCandidate;

        Part m_blankPart;
    }
}
