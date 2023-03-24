using System;
using System.Collections.Generic;
using System.Linq;

namespace CAMAutomation
{
    public class Dimension
    {
        public Dimension(double val = 0.0, double stkrm = 0.0)
        {
            Value = val;
            MinStkRm = stkrm;
            IsCutLength = false;
        }

        public Dimension(Dimension other)
        {
            Value = other.Value;
            MinStkRm = other.MinStkRm;
            IsCutLength = other.IsCutLength;
        }

        ~Dimension()
        {
            // empty
        }

        public double Value { get; set; }
        public double MinStkRm { get; set; }
        public bool IsCutLength { get; set; }
    }


    public abstract class Stock
    {
        public enum StockType { BLOCK = 0, BAR = 1, PLATE = 2 };

        public Stock(StockType type)
        {
            Type = type;
        }

        public Stock(Stock other)
        {
            Type = other.Type;

            Id = other.Id;
            Name = other.Name;
            Material = other.Material;

            m_Dimensions = other.m_Dimensions.Select(p => new Dimension(p)).ToArray();
        }

        ~Stock()
        {
            // empty
        }


        public abstract Stock Copy();
        public abstract double ComputeVolume(bool includeStkrm);


        public int GetNumDimensions()
        {
            return m_Dimensions.Length;
        }


        protected double GetDimension(int index, bool includeStkrm)
        {
            if (index >= 0 && index < GetNumDimensions())
            {
                if (includeStkrm)
                {
                    return m_Dimensions[index].Value;
                }
                else
                {
                    return m_Dimensions[index].Value - (2.0 * m_Dimensions[index].MinStkRm);
                }
            }
            else
            {
                return 0.0;
            }
        }


        protected double GetStkRm(int index)
        {
            if (index >= 0 && index < GetNumDimensions())
            {
                return m_Dimensions[index].MinStkRm;
            }
            else
            {
                return 0.0;
            }
        }


        protected void SetDimension(int index, double value, double stkrm)
        {
            if (index >= 0 && index < GetNumDimensions())
            {
                m_Dimensions[index].Value = value;
                m_Dimensions[index].MinStkRm = stkrm;
            }
        }


        public int GetNumCutLength()
        {
            return m_Dimensions.Count(p => p.IsCutLength);
        }


        protected double GetCutLength(int cutlengthIndex, bool includeStkrm)
        {
            int index = GetGlobalIndex(cutlengthIndex);
            return GetDimension(index, includeStkrm);
        }


        protected double GetCutLengthStkRm(int cutlengthIndex)
        {
            int index = GetGlobalIndex(cutlengthIndex);
            return GetStkRm(index);
        }


        protected void ApplyCutLength(int cutlengthIndex, double value, double stkrm)
        {
            int index = GetGlobalIndex(cutlengthIndex);
            SetDimension(index, value, stkrm);
        }


        private int GetGlobalIndex(int cutlengthIndex)
        {
            int[] cutlengthIndices = m_Dimensions.Select((p, i) => new { dim = p, idx = i }).Where(q => q.dim.IsCutLength).Select(r => r.idx).ToArray();

            if (cutlengthIndex < cutlengthIndices.Length)
            {
                return cutlengthIndices[cutlengthIndex];
            }
            else
            {
                return -1;
            }
        }


        public StockType Type { get; private set; }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Material { get; set; }

        protected Dimension[] m_Dimensions;
    }


    public abstract class StockPrism : Stock
    {
        public StockPrism(StockType type) : base(type)
        {
            // Create an array of 3 dimensions
            m_Dimensions = new Dimension[] 
            {
                new Dimension(),
                new Dimension(),
                new Dimension()
            };
        }

        public StockPrism(StockPrism other) : base(other)
        {
            // empty
        }

        ~StockPrism()
        {
            // empty
        }


        public override abstract Stock Copy();
        

        public void SetDimension1(double value, double stkrm)
        {
            SetDimension(0, value, stkrm);
        }


        public void SetDimension2(double value, double stkrm)
        {
            SetDimension(1, value, stkrm);
        }


        public void SetDimension3(double value, double stkrm)
        {
            SetDimension(2, value, stkrm);
        }


        public double GetDimension1(bool includeStkrm)
        {
            return GetDimension(0, includeStkrm);
        }


        public double GetDimension2(bool includeStkrm)
        {
            return GetDimension(1, includeStkrm);
        }


        public double GetDimension3(bool includeStkrm)
        {
            return GetDimension(2, includeStkrm);
        }


        public double GetStkRm1()
        {
            return GetStkRm(0);
        }


        public double GetStkRm2()
        {
            return GetStkRm(1);
        }


        public double GetStkRm3()
        {
            return GetStkRm(2);
        }


        public override double ComputeVolume(bool includeStkrm)
        {
            return GetDimension1(includeStkrm) * GetDimension2(includeStkrm) * GetDimension3(includeStkrm);
        }


        public void Rotate(int axis)
        {
            if (axis == 1)
            {
                Utilities.MathUtils.Swap(m_Dimensions, 1, 2);
            }
            else if (axis == 2)
            {
                Utilities.MathUtils.Swap(m_Dimensions, 0, 2);
            }
            else if (axis == 3)
            {
                Utilities.MathUtils.Swap(m_Dimensions, 0, 1);
            }
        }


        public StockPrism[] GetAllPossibleOrientations()
        {
            List<StockPrism> list = new List<StockPrism>();
            List<int[]> rotations = new List<int[]>()
            {
                new int[]{ },
                new int[]{ 1 },
                new int[]{ 1, 2 },
                new int[]{ 2 },
                new int[]{ 2, 1 },
                new int[]{ 3 },
            };

            for (int i = 0; i < rotations.Count; ++i)
            {
                StockPrism stockPrism = this.Copy() as StockPrism;
                if (stockPrism != null)
                {
                    for (int j = 0; j < rotations[i].Length; ++j)
                    {
                        stockPrism.Rotate(rotations[i][j]);
                    }

                    list.Add(stockPrism);
                }
            }

            return list.ToArray();
        }
    }


    public class StockBlock : StockPrism
    {
        public StockBlock() : base(StockType.BLOCK)
        {
            // empty
        }

        public StockBlock(StockBlock other) : base(other)
        {
            // empty
        }

        ~StockBlock()
        {
            // empty
        }

        public override Stock Copy()
        {
            return new StockBlock(this);
        }
    }


    public class StockBar : StockPrism
    {
        public StockBar() : base(StockType.BAR)
        {
            m_Dimensions[2].IsCutLength = true;
        }

        public StockBar(StockBar other) : base(other)
        {
            // empty
        }

        ~StockBar()
        {
            // empty
        }

        public override Stock Copy()
        {
            return new StockBar(this);
        }

        public double GetArea(bool includeStkrm)
        {
            Dimension[] dimensions = m_Dimensions.Where(p => !p.IsCutLength).ToArray();
            if (dimensions.Length == 2)
            {
                if (includeStkrm)
                {
                    return dimensions[0].Value * dimensions[1].Value;
                }
                else
                {
                    return (dimensions[0].Value - (2.0 * dimensions[0].MinStkRm)) * (dimensions[1].Value - (2.0 * dimensions[1].MinStkRm));
                }
            }
            else
            {
                return 0.0;
            }
        }

        public double GetCutLength(bool includeStkrm)
        {
            return GetCutLength(0, includeStkrm);
        }


        public double GetCutLengthStkRm()
        {
            return GetCutLengthStkRm(0);
        }


        public void ApplyCutLength(double value, double stkrm)
        {
            ApplyCutLength(0, value, stkrm);
        }
    }


    public class StockPlate : StockPrism
    {
        public StockPlate() : base(StockType.PLATE)
        {
            m_Dimensions[1].IsCutLength = true;
            m_Dimensions[2].IsCutLength = true;
        }

        public StockPlate(StockPlate other) : base(other)
        {
            // empty
        }

        ~StockPlate()
        {
            // empty
        }

        public override Stock Copy()
        {
            return new StockPlate(this);
        }


        public double GetThickness(bool includeStkrm)
        {
            Dimension thickness = m_Dimensions.FirstOrDefault(p => !p.IsCutLength);
            if (thickness != null)
            {
                if (includeStkrm)
                {
                    return thickness.Value;
                }
                else
                {
                    return thickness.Value - (2.0 * thickness.MinStkRm);
                }
            }
            else
            {
                return 0.0;
            }
        }


        public double GetThicknessMinStkRm()
        {
            Dimension thickness = m_Dimensions.FirstOrDefault(p => !p.IsCutLength);
            if (thickness != null)
            {
                return thickness.MinStkRm;
            }
            else
            {
                return 0.0;
            }
        }


        public double GetCutLength1(bool includeStkrm)
        {
            return GetCutLength(0, includeStkrm);
        }


        public double GetCutLength2(bool includeStkrm)
        {
            return GetCutLength(1, includeStkrm);
        }


        public double GetCutLengthStkRm1()
        {
            return GetCutLengthStkRm(0);
        }


        public double GetCutLengthStkRm2()
        {
            return GetCutLengthStkRm(1);
        }


        public void ApplyCutLength1(double value, double stkrm)
        {
            ApplyCutLength(0, value, stkrm);
        }


        public void ApplyCutLength2(double value, double stkrm)
        {
            ApplyCutLength(1, value, stkrm);
        }
    }
}
