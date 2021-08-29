namespace ThriveDevCenter.Shared.Converters
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///   Provides chunking for IEnumerables. Code taken from stackoverflow with modifications
    ///   https://stackoverflow.com/a/10425490/4371508 Licensed under CC BY-SA 3.0
    /// </summary>
    public static class ChunkedEnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int chunkSize)
        {
            if (chunkSize < 1) throw new ArgumentException();

            var wrapper = new EnumeratorWrapper<T>(source);

            int currentPos = 0;
            try
            {
                wrapper.AddRef();
                while (wrapper.Get(currentPos, out _))
                {
                    yield return new ChunkedEnumerable<T>(wrapper, chunkSize, currentPos);

                    currentPos += chunkSize;
                }
            }
            finally
            {
                wrapper.RemoveRef();
            }
        }

        private class ChunkedEnumerable<T> : IEnumerable<T>
        {
            private readonly EnumeratorWrapper<T> wrapper;
            private readonly int chunkSize;
            private readonly int start;

            public IEnumerator<T> GetEnumerator()
            {
                return new ChildEnumerator(this);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private class ChildEnumerator : IEnumerator<T>
            {
                private readonly ChunkedEnumerable<T> parent;
                private int position;
                private bool done;
                private T current;

                public ChildEnumerator(ChunkedEnumerable<T> parent)
                {
                    this.parent = parent;
                    position = -1;
                    parent.wrapper.AddRef();
                }

                public T Current
                {
                    get
                    {
                        if (position == -1 || done)
                        {
                            throw new InvalidOperationException();
                        }

                        return current;
                    }
                }

                public void Dispose()
                {
                    if (done)
                        return;

                    done = true;
                    parent.wrapper.RemoveRef();
                }

                object System.Collections.IEnumerator.Current
                {
                    get { return Current; }
                }

                public bool MoveNext()
                {
                    position++;

                    if (position + 1 > parent.chunkSize)
                    {
                        done = true;
                    }

                    if (!done)
                    {
                        done = !parent.wrapper.Get(position + parent.start, out current);
                    }

                    return !done;
                }

                public void Reset()
                {
                    // per http://msdn.microsoft.com/en-us/library/system.collections.ienumerator.reset.aspx
                    throw new NotSupportedException();
                }
            }

            public ChunkedEnumerable(EnumeratorWrapper<T> wrapper, int chunkSize, int start)
            {
                this.wrapper = wrapper;
                this.chunkSize = chunkSize;
                this.start = start;
            }
        }

        private class EnumeratorWrapper<T>
        {
            public EnumeratorWrapper(IEnumerable<T> source)
            {
                SourceEnumerable = source;
            }

            private int refs;
            private Enumeration currentEnumeration;

            private IEnumerable<T> SourceEnumerable { get; set; }

            public bool Get(int pos, out T item)
            {
                if (currentEnumeration != null && currentEnumeration.Position > pos)
                {
                    currentEnumeration.Source.Dispose();
                    currentEnumeration = null;
                }

                if (currentEnumeration == null)
                {
                    currentEnumeration = new Enumeration
                        { Position = -1, Source = SourceEnumerable.GetEnumerator(), AtEnd = false };
                }

                item = default;
                if (currentEnumeration.AtEnd)
                {
                    return false;
                }

                while (currentEnumeration.Position < pos)
                {
                    currentEnumeration.AtEnd = !currentEnumeration.Source.MoveNext();
                    currentEnumeration.Position++;

                    if (currentEnumeration.AtEnd)
                    {
                        return false;
                    }
                }

                item = currentEnumeration.Source.Current;

                return true;
            }

            // needed for dispose semantics
            public void AddRef()
            {
                refs++;
            }

            public void RemoveRef()
            {
                refs--;
                if (refs == 0 && currentEnumeration != null)
                {
                    var copy = currentEnumeration;
                    currentEnumeration = null;
                    copy.Source.Dispose();
                }
            }

            private class Enumeration
            {
                public IEnumerator<T> Source { get; init; }
                public int Position { get; set; }
                public bool AtEnd { get; set; }
            }
        }
    }
}
