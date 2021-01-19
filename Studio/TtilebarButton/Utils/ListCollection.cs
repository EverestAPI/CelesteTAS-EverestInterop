using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;

namespace CelesteStudio.TtilebarButton.Utils
{
    internal class ListCollection<T> : IList<T>, IList
    {
        private static readonly bool s_checkForNull = !typeof(T).IsValueType;

        internal IList<T> m_list;

        public virtual bool IsReadOnly => false;
        public int Count => m_list.Count;

        public virtual bool AllowNew
        {
            get
            {
                var type = typeof(T);
                return type.IsValueType || !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null;
            }
        }
        public virtual bool AllowEdit => !IsReadOnly;
        public virtual bool AllowRemove => !IsReadOnly;

        public T this[int index]
        {
            get => m_list[index];
            set
            {
                ThrowIfNull(value, "value");
                ThrowIfOutOfRange(index, Count);
                ThrowIfReadOnly();

                var old = m_list[index];
                if (!EqualityComparer<T>.Default.Equals(old, value))
                {
                    OnValidate(value);
                    SetInner(index, old, value);
                }
            }
        }

        public ListCollection()
        {
            m_list = new List<T>();
        }


        public int IndexOf(T item)
        {
            return m_list.IndexOf(item);
        }
        public bool Contains(T item)
        {
            return m_list.Contains(item);
        }
        public void CopyTo(T[] array, int arrayIndex)
        {
            m_list.CopyTo(array, arrayIndex);
        }

        public void AddRange(IEnumerable<T> range)
        {
            if (range == null)
                throw new ArgumentNullException(nameof(range));

            ThrowIfReadOnly();
            AddRangeInner(range);
        }
        public T Add(T item)
        {
            ThrowIfNull(item);
            ThrowIfReadOnly();

            OnValidate(item);
            InsertInner(Count, item);

            return item;
        }
        public T Insert(int index, T item)
        {
            ThrowIfNull(item);
            ThrowIfOutOfRange(index, Count + 1);
            ThrowIfReadOnly();

            OnValidate(item);
            InsertInner(index, item);

            return item;
        }
        internal virtual void InsertInner(int index, T item)
        {
            OnInsert(index, item);
            {
                m_list.Insert(index, item);
            }
            OnInsertComplete(index, item);
            OnListChanged();
        }
        internal virtual void SetInner(int index, T old, T value)
        {
            OnSet(index, old, value);
            {
                m_list[index] = value;
            }
            OnSetComplete(index, old, value);
            OnListChanged();
        }

        public bool Remove(T item)
        {
            ThrowIfReadOnly();

            var index = IndexOf(item);
            if (index >= 0)
            {
                RemoveInner(index);
                return true;
            }

            return false;
        }
        public void RemoveAt(int index)
        {
            ThrowIfOutOfRange(index, Count);
            ThrowIfReadOnly();

            RemoveInner(index);
        }
        internal virtual void RemoveInner(int index)
        {
            var value = m_list[index];

            OnRemove(index, value);
            {
                m_list.RemoveAt(index);
            }
            OnRemoveComplete(index, value);
            OnListChanged();
        }

        public void Clear()
        {
            ThrowIfReadOnly();

            if (m_list.Count == 0)
                return;

            ClearInner();
        }
        internal virtual void ClearInner()
        {
            OnClear();
            {
                m_list.Clear();
            }
            OnClearComplete();
            OnListChanged();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void ClearAndAddRange(IEnumerable<T> range)
        {
            if (range == null)
                throw new ArgumentNullException(nameof(range));
            ThrowIfReadOnly();
            ClearAndAddRangeInner(range);
        }
        internal virtual void ClearAndAddRangeInner(IEnumerable<T> range)
        {
            ClearInner();

            foreach (var item in range)
            {
                OnValidate(item);
                InsertInner(Count, item);
            }
        }
        internal virtual void AddRangeInner(IEnumerable<T> range)
        {
            foreach (var item in range)
            {
                OnValidate(item);
                InsertInner(Count, item);
            }
        }

        public virtual T AddNew()
        {
            if (!AllowNew)
                throw new NotSupportedException();

            var item = (T)Activator.CreateInstance(typeof(T));
            Add(item);
            return item;
        }

        protected virtual void OnValidate(T value)
        {
        }

        protected virtual void OnSet(int index, T oldValue, T newValue)
        {
        }
        protected virtual void OnSetComplete(int index, T oldValue, T newValue)
        {
        }

        protected virtual void OnInsert(int index, T value)
        {
        }
        protected virtual void OnInsertComplete(int index, T value)
        {
        }

        protected virtual void OnRemove(int index, T value)
        {
        }
        protected virtual void OnRemoveComplete(int index, T value)
        {
        }

        protected virtual void OnClear()
        {
        }
        protected virtual void OnClearComplete()
        {
        }

        protected virtual void OnListChanged()
        {
        }

        #region Checks

        private void ThrowIfNull(T item)
        {
            if (s_checkForNull && EqualityComparer<T>.Default.Equals(item, default(T)))
                throw new ArgumentNullException(nameof(item));
        }
        private void ThrowIfNull(T item, string paramName)
        {
            if (s_checkForNull && EqualityComparer<T>.Default.Equals(item, default(T)))
                throw new ArgumentNullException(paramName);
        }
        private void ThrowIfOutOfRange(int index, int count)
        {
            if (index < 0 || index >= count)
                throw new IndexOutOfRangeException();
        }
        private void ThrowIfReadOnly()
        {
            if (IsReadOnly)
                throw new ReadOnlyException();
        }

        #endregion

        #region IList

        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => ((ICollection)m_list).SyncRoot;
        bool IList.IsFixedSize => IsReadOnly;

        object IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T)value;
        }


        bool IList.Contains(object value)
        {
            return ((IList)this).IndexOf(value) != -1;
        }
        int IList.IndexOf(object value)
        {
            if (value is T)
                return IndexOf((T)value);

            return -1;
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }
        void IList<T>.Insert(int index, T item)
        {
            Insert(index, item);
        }

        int IList.Add(object value)
        {
            var index = Count;
            Add((T)value);
            return index;
        }
        void IList.Insert(int index, object value)
        {
            Insert(index, (T)value);
        }

        void IList.Remove(object value)
        {
            if (value is T)
                Remove((T)value);
        }
        void IList.RemoveAt(int index)
        {
            RemoveAt(index);
        }
        void IList.Clear()
        {
            Clear();
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            ((IList)m_list).CopyTo(array, arrayIndex);
        }

        #endregion
    }
}
