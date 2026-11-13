using System;

namespace Sv.Rules
{
    internal sealed class Rows<T>
    {
        T[] items;
        int count;

        public Rows()
        {
            items = new T[8];
        }

        public int Count => count;

        public T this[int index] => items[index];

        public void Add(T item)
        {
            if (count == items.Length)
            {
                Array.Resize(ref items, items.Length * 2);
            }

            items[count] = item;
            count++;
        }

        public T[] ToArray()
        {
            var copy = new T[count];
            Array.Copy(items, copy, count);
            return copy;
        }
    }
}
