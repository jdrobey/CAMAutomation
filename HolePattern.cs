using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace CAMAutomation
{
    public class HolePattern : ICollection<Hole>
    {
        public HolePattern()
        {
            m_holes = new List<Hole>();
        }


        public HolePattern(int capacity)
        {
            m_holes = new List<Hole>(capacity);
        }


        public HolePattern(IEnumerable<Hole> collection)
        {
            m_holes = new List<Hole>(collection);
        }


        ~HolePattern()
        {
            // empty
        }


        public Hole this[int index]
        {
            get { return m_holes[index]; }
            set { m_holes[index] = value; }
        }


        public void Add(Hole item)
        {
            if (!Contains(item))
            {
                m_holes.Add(item);
            }
        }


        public int Count
        {
            get { return m_holes.Count; }
        }


        public bool IsReadOnly
        {
            get { return false; }
        }


        public bool IsEquivalent(HolePattern pattern, double deltaHoleRadius = 0.0, double lineRadius = 0.0, double solidAngle = 0.0)
        {
            if (Count != pattern.Count)
            {
                return false;
            }

            return m_holes.All(p => pattern.Any(q => p.IsEquivalent(q, deltaHoleRadius, lineRadius, solidAngle)));
        }


        public override bool Equals(object obj)
        {
            if (obj != null && obj is HolePattern)
            {
                return Equals((HolePattern)obj);
            }

            return false;
        }


        public bool Equals(HolePattern pattern)
        {
            return IsEquivalent(pattern);
        }


        public override int GetHashCode()
        {
            return 0;
        }


        public bool Contains(Hole hole)
        {
            return m_holes.Contains(hole);
        }


        public void Clear()
        {
            m_holes.Clear();
        }


        public void CopyTo(Hole[] array, int arrayIndex)
        {
            if (!(array == null || arrayIndex < 0 || Count > array.Length - arrayIndex + 1))
            {
                for (int i = 0; i < m_holes.Count; i++)
                {
                    array[i + arrayIndex] = m_holes[i];
                }
            }
        }


        public bool Remove(Hole item)
        {
            return m_holes.Remove(item);
        }


        public IEnumerator<Hole> GetEnumerator()
        {
            return m_holes.GetEnumerator();
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_holes.GetEnumerator();
        }


        private List<Hole> m_holes;
    }
}