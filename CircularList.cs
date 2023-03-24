using System;
using System.Collections.Generic;
using System.Linq;


namespace CAMAutomation
{
    public class CircularList<T> : List<T>
    {
        public static bool AreEquivalent(CircularList<T> loop1, CircularList<T> loop2)
        {
            return AreEquivalent(loop1, loop2, loop1.AreEquivalent);
        }


        public static bool AreEquivalent(CircularList<T> loop1, CircularList<T> loop2, Func<T, T, bool> equivalence)
        {
            if (loop1.Count() == loop2.Count())
            {
                if (!loop2.Any(p => equivalence(p, loop1.First())))
                {
                    return false;
                }

                List<bool> equivalentList = loop2.Select(p => equivalence(p, loop1.First())).ToList();

                int indexShift = 0;
                foreach (bool isEquivalent in equivalentList)
                {
                    bool skip = false;
                    for (int i = 0; i < loop1.Count(); i++)
                    {
                        if (!isEquivalent || !equivalence(loop1[i], loop2[indexShift + i]))
                        {
                            skip = true;
                            break;
                        }
                    }

                    indexShift++;

                    if (skip)
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }


        public CircularList() : base() { }


        public CircularList(int capacity) : base(capacity) { }


        public CircularList(params T[] collection) : base(collection) { }
        

        public CircularList(IEnumerable<T> collection) : base(collection) { }


        ~CircularList()
        {
            // empty
        }


        public new T this[int index]
        {
            get => base[CircularIndex(index)];
            set => base[CircularIndex(index)] = value;
        }


        public new void Reverse()
        {
            base.Reverse();
            Rotate(-1);
        }


        public void InsertAfter(int index, T item)
        {
            Insert(CircularIndex(index) + 1, item);
        }


        public void InsertRangeAfter(int index, IEnumerable<T> collection)
        {
            InsertRange(CircularIndex(index)+1, collection);
        }


        public void ReplaceRange(List<T> range, int i, int j)
        {
            i = CircularIndex(i + 1);
            j = CircularIndex(j);

            if (i < j)
            {
                RemoveRange(i, j - i);
                InsertRange(i, range);
            }
            else if (i > j)
            {
                RemoveRange(i, Count - i);
                RemoveRange(0, j);
                AddRange(range);
            }
            else //if (i == j)
            {
                InsertRange(i, range);
            }
        }


        public void Replace(List<T> range)
        {
            RemoveRange(0, Count);
            AddRange(range);
        }


        public new void RemoveAt(int i)
        {
            base.RemoveAt(CircularIndex(i));
        }


        public void RemoveOpenSlice(int i, int j)
        {
            i = CircularIndex(i);
            j = CircularIndex(j);

            if (i == j)
            {
                Clear();
                Add(this[i]);
            }
            else
            {
                RemoveSlice(i + 1, j); 
            }
        }


        public void RemoveClosedSlice(int i, int j)
        {
            i = CircularIndex(i);
            j = CircularIndex(j);
            if (i == j)
            {
                RemoveAt(i);
            }

            j = CircularIndex(j + 1);
            if (i == j)
            {
                Clear();
            }

            RemoveSlice(i, j);
        }


        public void Rotate(int i = 1)
        {
            i = CircularIndex(i);
            List<T> fisrt = GetRange(i, Count - i);
            List<T> then = GetRange(0, i);
            Clear();
            AddRange(fisrt);
            AddRange(then);
        }

        
        public void RotateTo(T item)
        {
            int i = CircularIndex(IndexOf(item));
            Rotate(i);
        }


        public void RotateToNext(T item, Func<T, T, bool> relation)
        {
            T nextItem = Next(item, relation);
            RotateTo(nextItem);
        }


        public CircularList<T> GetRotated(int i)
        {
            CircularList<T> circularList = new CircularList<T>((this as IEnumerable<T>));
            circularList.Rotate(i);

            return circularList;
        }


        public List<T> GetOpenSlice(int i, int j)
        {
            i = CircularIndex(i);
            j = CircularIndex(j);

            List<T> slice;
            if (i == j)
            {
                slice = GetRotated(i).ToList();
                slice.RemoveAt(0);
            }
            else
            {
                slice = GetClosedSlice(i, j);
                slice.RemoveAt(slice.Count() - 1);
                slice.RemoveAt(0);
            }

            return slice;
        }


        public List<T> GetClosedSlice(int i, int j)
        {
            i = CircularIndex(i);
            j = CircularIndex(j);
            if (i == j)
            {
                return new List<T>() { this[i] };
            }

            j = CircularIndex(j + 1);
            if (i == j)
            {
                return GetRotated(i).ToList();
            }

            return GetSlice(i, j);
        }


        public new int IndexOf(T item)
        {
            return base.IndexOf(this.Where(p => AreEquivalent(p, item)).First());
        }


        public T Next(T item)
        {
            return this[IndexOf(item) + 1];
        }


        public T Next(T item, Func<T, T, bool> relation)
        {
            T nextItem = item;
            do
            {
                nextItem = Next(nextItem);
            } while (!relation(item, nextItem) && !AreEquivalent(item, nextItem));

            return nextItem;
        }


        public T Previous(T item)
        {
            return this[IndexOf(item) - 1];
        }


        public T Previous(T item, Func<T, T, bool> relation)
        {
            T previousItem = item;
            do
            {
                previousItem = Previous(previousItem);
            } while (!relation(item, previousItem) && !AreEquivalent(item, previousItem));

            return previousItem;
        }


        public bool IsEquivalent(CircularList<T> circularList)
        {
            return AreEquivalent(this, circularList);
        }


        protected virtual bool AreEquivalent(T arg1, T arg2)
        {
            return arg1.Equals(arg2);
        }


        private int CircularIndex(int index)
        {
            index %= Count;
            return index < 0 ? index + Count : index;
        }


        private void RemoveSlice(int i, int j)
        {
            if (i < j)
            {
                RemoveRange(i, j - i);
            }
            else if (i > j)
            {
                RemoveRange(i, Count - i);
                RemoveRange(0, j);
            }
        }


        private List<T> GetSlice(int i, int j)
        {
            if (i < j)
            {
                return GetRange(i, j - i);
            }
            else if (i > j)
            {
                return GetRange(i, Count - i).Concat(GetRange(0, j)).ToList();
            }
            else //if (i == j)
            {
                return new List<T>();
            }
        }
    }
}