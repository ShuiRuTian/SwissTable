// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using static System.Collections.Generic.SwissTableHelper;

namespace System.Collections.Generic
{
    [DebuggerTypeProxy(typeof(IDictionaryDebugView<,>))]
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public partial class MyDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISerializable, IDeserializationCallback where TKey : notnull
    {
        // Term define:
        //   Capacity: The maximum number of non-empty items that a hash table can hold before scaling. (come from `EnsureCapacity`)
        //   count: the real count of stored items
        //   growth_left: due to the existing of tombstone, non-empty does not mean there is indeed a value.
        //   bucket: `Entry` in the code, which could hold a key-value pair

        // this contains all meaningfull data but _version
        private struct RawTableInner
        {
            internal int _bucket_mask;
            internal byte[] _controls;
            internal Entry[]? _entries;
            // Number of elements that can be inserted before we need to grow the table
            // This need to be calculated individually, for the tombstone("DELETE")
            internal int _growth_left;
            // number of real values stored in the map
            // `items` in rust
            internal int _count;
            internal IEqualityComparer<TKey>? _comparer;
        }
        // each add/remove will add one version
        // For Enumerator, if version is changed, one error will be thrown for enumerator should not changed.
        private int _version;
        private RawTableInner rawTable;

        public int Count => this.rawTable._count;

        // The real space that the swisstable allocated
        // always be the power of 2
        // This means the upper bound is not Int32.MaxValue(0x7FFFF_FFFF), but 0x4000_0000
        // TODO: Should we throw an expection if user try to grow again when it has been largest?
        // This is hard(impossible?) to be larger, for the length of array is limited to 0x7FFFF_FFFF
        private int _buckets => this.rawTable._bucket_mask + 1;

        private bool is_empty_singleton => rawTable._bucket_mask == 0;


        public MyDictionary() : this(0, null) { }

        public MyDictionary(int capacity) : this(capacity, null) { }

        public MyDictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

        public MyDictionary(int capacity, IEqualityComparer<TKey>? comparer)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            Initialize(capacity);

            if (comparer is not null && comparer != EqualityComparer<TKey>.Default) // first check for null to avoid forcing default comparer instantiation unnecessarily
            {
                this.rawTable._comparer = comparer;
            }

            // Special-case EqualityComparer<string>.Default, StringComparer.Ordinal, and StringComparer.OrdinalIgnoreCase.
            // We use a non-randomized comparer for improved perf, falling back to a randomized comparer if the
            // hash buckets become unbalanced.
            if (typeof(TKey) == typeof(string))
            {
                // IEqualityComparer<string>? stringComparer = NonRandomizedStringEqualityComparer.GetStringComparer(_comparer);
                // if (stringComparer is not null)
                // {
                //     _comparer = (IEqualityComparer<TKey>?)stringComparer;
                // }
            }
        }

        public MyDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public MyDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer) :
            this(dictionary != null ? dictionary.Count : 0, comparer)
        {
            if (dictionary == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
            }

            CloneFromCollection(dictionary);
        }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) :
            this((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0, comparer)
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }

            CloneFromCollection(collection);
        }

        protected MyDictionary(SerializationInfo info, StreamingContext context)
        {
            // We can't do anything with the keys and values until the entire graph has been deserialized
            // and we have a resonable estimate that GetHashCode is not going to fail.  For the time being,
            // we'll just cache this.  The graph is not valid until OnDeserialization has been called.
            HashHelpers.SerializationInfoTable.Add(this, info);
        }

        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                // TODO: uncomment
                // if (typeof(TKey) == typeof(string))
                // {
                //     return (IEqualityComparer<TKey>)IInternalStringEqualityComparer.GetUnderlyingEqualityComparer((IEqualityComparer<string?>?)_comparer);
                // }
                // else
                // {
                return rawTable._comparer ?? EqualityComparer<TKey>.Default;
                // }
            }
        }

        private void CloneFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            // It is likely that the passed-in dictionary is Dictionary<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation and overhead by looping through the entries array directly.
            // We only do this when dictionary is Dictionary<TKey,TValue> and not a subclass, to maintain
            // back-compat with subclasses that may have overridden the enumerator behavior.
            if (collection.GetType() == typeof(MyDictionary<TKey, TValue>))
            {
                MyDictionary<TKey, TValue> source = (MyDictionary<TKey, TValue>)collection;

                CloneFromDictionary(source);
            }

            // Fallback path for IEnumerable that isn't a non-subclassed Dictionary<TKey,TValue>.
            foreach (KeyValuePair<TKey, TValue> pair in collection)
            {
                Add(pair.Key, pair.Value);
            }
        }

        private void CloneFromDictionary(MyDictionary<TKey, TValue> source)
        {
            if (source.Count == 0)
            {
                // Nothing to copy, all done
                return;
            }
            // This is not currently a true .AddRange as it needs to be an initalized dictionary
            // of the correct size, and also an empty dictionary with no current entities (and no argument checks).

            if (this.rawTable._comparer == source.rawTable._comparer)
            {
                CloneRawTable(source.rawTable, this.rawTable);
                return;
            }
            Debug.Assert(source.rawTable._entries != null);
            byte[] oldCtrls = source.rawTable._controls;
            Entry[] oldEntries = source.rawTable._entries;

            // TODO: Maybe we could use IBITMASK to accllerate
            int length = source.rawTable._entries.Length;
            for (int i = 0; i < length; i++)
            {
                if (is_full(oldCtrls[i]))
                {
                    Add(oldEntries[i].Key, oldEntries[i].Value);
                }
            }
            return;
        }

        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            // TODO: maybe need to duplicate most of code with `Remove(TKey key)` for performance issue, see C# old implementation
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            if (this.rawTable._entries != null)
            {
                var index = this.FindBucketIndex(key);
                if (index != null)
                {
                    value = this.erase(index.Value);
                    return true;
                }
            }
            value = default;
            return false;
        }

        public bool TryAdd(TKey key, TValue value) =>
            TryInsert(key, value, InsertionBehavior.None);

        private KeyCollection? _keys;
        public KeyCollection Keys => _keys ??= new KeyCollection(this);

        private ValueCollection? _values;
        public ValueCollection Values => _values ??= new ValueCollection(this);

        public TValue this[TKey key]
        {
            get
            {
                ref TValue value = ref FindValue(key);
                if (!Unsafe.IsNullRef(ref value))
                {
                    return value;
                }

                ThrowHelper.ThrowKeyNotFoundException(key);
                return default;
            }
            set
            {
                bool modified = TryInsert(key, value, InsertionBehavior.OverwriteExisting);
                Debug.Assert(modified);
            }
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            ref TValue valRef = ref FindValue(key);
            if (!Unsafe.IsNullRef(ref valRef))
            {
                value = valRef;
                return true;
            }

            value = default;
            return false;
        }

        public void Clear()
        {
            int count = rawTable._count;
            if (count > 0)
            {
                Debug.Assert(rawTable._entries != null, "_entries should be non-null");

                Array.Fill(rawTable._controls, EMPTY);
                rawTable._count = 0;
                rawTable._growth_left = MyDictionary<TKey, TValue>.bucket_mask_to_capacity(rawTable._bucket_mask);
                // TODO: maybe we could remove this branch to improve perf. Or maybe CLR has optimised this.
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>()
                    || RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                {
                    Array.Clear(rawTable._entries);
                }
            }
        }

        private static bool IsCompatibleKey(object key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            return key is TKey;
        }

        private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            CopyToWorker(array, index);
        }

        // This method not check whether array and index, maybe the name should be CopyToUnsafe?
        private void CopyToWorker(KeyValuePair<TKey, TValue>[] array, int index)
        {
            foreach (var item in this)
            {
                array[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
            }
        }

        #region IDictionary
        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        bool IDictionary.IsFixedSize => false;

        bool IDictionary.IsReadOnly => false;

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;


        object? IDictionary.this[object key]
        {
            get
            {
                if (IsCompatibleKey(key))
                {
                    ref TValue value = ref FindValue((TKey)key);
                    if (!Unsafe.IsNullRef(ref value))
                    {
                        return value;
                    }
                }

                return null;
            }
            set
            {
                if (key == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
                }
                ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

                try
                {
                    TKey tempKey = (TKey)key;
                    try
                    {
                        this[tempKey] = (TValue)value!;
                    }
                    catch (InvalidCastException)
                    {
                        ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                    }
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
                }
            }
        }

        void IDictionary.Add(object key, object? value)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

            try
            {
                TKey tempKey = (TKey)key;

                try
                {
                    Add(tempKey, (TValue)value!);
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                }
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
            }
        }

        bool IDictionary.Contains(object key)
        {
            if (IsCompatibleKey(key))
            {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DictEntry);

        void IDictionary.Remove(object key)
        {
            if (IsCompatibleKey(key))
            {
                Remove((TKey)key);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if (array.Rank != 1)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            }

            if (array.GetLowerBound(0) != 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                CopyToWorker(pairs, index);
            }
            else if (array is DictionaryEntry[] dictEntryArray)
            {
                foreach (var item in this)
                {
                    dictEntryArray[index++] = new DictionaryEntry(item.Key, item.Value);
                }
            }
            else
            {
                object[]? objects = array as object[];

                if (objects == null)
                {
                    ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                }

                try
                {
                    foreach (var item in this)
                    {
                        objects[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                }
            }
        }
        #endregion

        #region IReadOnlyDictionary<TKey, TValue>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        #endregion

        #region IDictionary<TKey, TValue>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        public void Add(TKey key, TValue value)
        {
            bool modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
            Debug.Assert(modified); // If there was an existing key and the Add failed, an exception will already have been thrown.
        }

        public bool ContainsKey(TKey key) =>
            !Unsafe.IsNullRef(ref FindBucket(key));

        public bool ContainsValue(TValue value)
        {
            // TODO: "inline" to get better performance
            foreach (var item in new ValueCollection(this))
            {
                if (EqualityComparer<TValue>.Default.Equals(item, value))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Remove(TKey key)
        {
            // TODO: maybe need to duplicate most of code with `Remove(TKey key, out TValue value)` for performance issue, see C# old implementation
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            if (this.rawTable._entries != null)
            {
                var index = this.FindBucketIndex(key);
                if (index != null)
                {
                    this.erase(index.Value);
                }
            }
            return false;
        }
        #endregion

        #region ICollection<KeyValuePair<TKey, TValue>>
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair) =>
            Add(keyValuePair.Key, keyValuePair.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref TValue value = ref FindValue(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref value) && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value))
            {
                return true;
            }

            return false;
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index) =>
            CopyTo(array, index);


        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref TValue value = ref FindValue(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref value) && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value))
            {
                Remove(keyValuePair.Key);
                return true;
            }

            return false;
        }
        #endregion

        #region IEnumerable<KeyValuePair<TKey, TValue>>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        #region Serialization/Deserialization
        // constants for Serialization/Deserialization
        private const string VersionName = "Version"; // Do not rename (binary serialization)
        private const string HashSizeName = "HashSize"; // Do not rename (binary serialization)
        private const string KeyValuePairsName = "KeyValuePairs"; // Do not rename (binary serialization)
        private const string ComparerName = "Comparer"; // Do not rename (binary serialization)

        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.info);
            }

            info.AddValue(VersionName, _version);
            info.AddValue(ComparerName, Comparer, typeof(IEqualityComparer<TKey>));
            info.AddValue(HashSizeName, rawTable._entries == null ? 0 : rawTable._entries.Length);

            if (rawTable._entries != null)
            {
                var array = new KeyValuePair<TKey, TValue>[Count];
                // This is always safe, for the array is allocated by ourself. There are always enough space.
                CopyToWorker(array, 0);
                info.AddValue(KeyValuePairsName, array, typeof(KeyValuePair<TKey, TValue>[]));
            }
        }

        public virtual void OnDeserialization(object? sender)
        {
            HashHelpers.SerializationInfoTable.TryGetValue(this, out SerializationInfo? siInfo);
            if (siInfo == null)
            {
                // We can return immediately if this function is called twice.
                // Note we remove the serialization info from the table at the end of this method.
                return;
            }

            int realVersion = siInfo.GetInt32(VersionName);
            int hashsize = siInfo.GetInt32(HashSizeName);
            rawTable._comparer = (IEqualityComparer<TKey>)siInfo.GetValue(ComparerName, typeof(IEqualityComparer<TKey>))!; // When serialized if comparer is null, we use the default.
            Initialize(hashsize);

            if (hashsize != 0)
            {
                KeyValuePair<TKey, TValue>[]? array = (KeyValuePair<TKey, TValue>[]?)
                    siInfo.GetValue(KeyValuePairsName, typeof(KeyValuePair<TKey, TValue>[]));

                if (array == null)
                {
                    ThrowHelper.ThrowSerializationException(ExceptionResource.Serialization_MissingKeys);
                }

                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i].Key == null)
                    {
                        ThrowHelper.ThrowSerializationException(ExceptionResource.Serialization_NullKey);
                    }

                    Add(array[i].Key, array[i].Value);
                }
            }
            _version = realVersion;
            HashHelpers.SerializationInfoTable.Remove(this);
        }
        #endregion

        public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHashCodeOfKey(TKey key)
        {
            return (this.rawTable._comparer == null) ? key.GetHashCode() : this.rawTable._comparer.GetHashCode(key);
        }

        /// <summary>
        /// The real implementation of any public insert behavior
        /// Do some extra work other than insert
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="behavior"></param>
        /// <returns></returns>
        private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
        {
            // NOTE: this method is mirrored in CollectionsMarshal.GetValueRefOrAddDefault below.
            // If you make any changes here, make sure to keep that version in sync as well.

            ref var bucket = ref this.FindBucket(key);
            // replace
            if (!Unsafe.IsNullRef(ref bucket))
            {
                switch (behavior)
                {
                    case InsertionBehavior.OverwriteExisting:
                        bucket.Key = key;
                        bucket.Value = value;
                        return true;
                    case InsertionBehavior.ThrowOnExisting:
                        ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(key);
                        break;
                    case InsertionBehavior.None:
                        return false;
                }
            }
            // insert new
            var hashCode = this.GetHashCodeOfKey(key);
            // We can avoid growing the table once we have reached our load
            // factor if we are replacing a tombstone(Delete). This works since the
            // number of EMPTY slots does not change in this case.
            var index = find_insert_slot(hashCode);
            var old_ctrl = rawTable._controls[index];
            if (this.rawTable._growth_left == 0 && special_is_empty(old_ctrl))
            {
                this.EnsureCapacity(this.rawTable._count + 1);
                index = find_insert_slot(hashCode);
            }
            Debug.Assert(rawTable._entries != null);
            record_item_insert_at(index, old_ctrl, hashCode);
            this.rawTable._entries[index].Key = key;
            this.rawTable._entries[index].Value = value;
            return true;
        }

        /// <summary>
        /// A helper class containing APIs exposed through <see cref="Runtime.InteropServices.CollectionsMarshal"/>.
        /// These methods are relatively niche and only used in specific scenarios, so adding them in a separate type avoids
        /// the additional overhead on each <see cref="Dictionary{TKey, TValue}"/> instantiation, especially in AOT scenarios.
        /// </summary>
        internal static class CollectionsMarshalHelper
        {
            /// <inheritdoc cref="Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault{TKey, TValue}(Dictionary{TKey, TValue}, TKey, out bool)"/>
            public static ref TValue? GetValueRefOrAddDefault(MyDictionary<TKey, TValue> dictionary, TKey key, out bool exists)
            {
                // NOTE: this method is mirrored by Dictionary<TKey, TValue>.TryInsert above.
                // If you make any changes here, make sure to keep that version in sync as well.

                ref var bucket = ref dictionary.FindBucket(key);
                // replace
                if (!Unsafe.IsNullRef(ref bucket))
                {
                    exists = true;
                    return ref bucket.Value!;
                }
                // insert new
                var hashCode = dictionary.GetHashCodeOfKey(key);
                // We can avoid growing the table once we have reached our load
                // factor if we are replacing a tombstone(Delete). This works since the
                // number of EMPTY slots does not change in this case.
                var index = dictionary.find_insert_slot(hashCode);
                var old_ctrl = dictionary.rawTable._controls[index];
                if (dictionary.rawTable._growth_left == 0 && special_is_empty(old_ctrl))
                {
                    dictionary.EnsureCapacity(dictionary.rawTable._count + 1);
                    index = dictionary.find_insert_slot(hashCode);
                }
                Debug.Assert(dictionary.rawTable._entries != null);
                dictionary.record_item_insert_at(index, old_ctrl, hashCode);
                dictionary.rawTable._entries[index].Key = key;
                dictionary.rawTable._entries[index].Value = default!;
                exists = false;
                return ref dictionary.rawTable._entries[index].Value!;
            }
        }

        private void record_item_insert_at(int index, byte old_ctrl, int hash)
        {
            this.rawTable._growth_left -= special_is_empty(old_ctrl) ? 1 : 0;
            set_ctrl_h2(index, hash, this.rawTable._controls);
            this.rawTable._count += 1;
        }

        private struct Entry
        {
            public TKey Key;
            public TValue Value;
        }

        // allocate and initialize when with real capacity
        // Note that the _entries might still not allocated
        // this means we do not want to use any existing data, including resize or use dictionary initialize
        // `realisticCapacity` is any positive number
        private void Initialize(int realisticCapacity)
        {
            RawTableInner innerTable;
            if (realisticCapacity == 0)
            {
                innerTable = new_in();
                this.rawTable._controls = innerTable._controls;
                this.rawTable._entries = innerTable._entries;
                return;
            }
            var idealCapacity = capacity_to_buckets(realisticCapacity);
            innerTable = new_uninitialized(idealCapacity);
            Array.Fill(innerTable._controls, EMPTY);
            this.rawTable._bucket_mask = innerTable._bucket_mask;
            this.rawTable._controls = innerTable._controls;
            this.rawTable._entries = innerTable._entries;
            this.rawTable._count = innerTable._count;
            this.rawTable._growth_left = innerTable._growth_left;
        }

        // TODO: check whether we need NonRandomizedStringEqualityComparer
        // If we need rehash, we should learn from rust.
        // this function only grow for now.
        private void Resize(int realisticCapacity)
        {
            Debug.Assert(this.rawTable._entries != null);
            var idealCapacity = capacity_to_buckets(realisticCapacity);
            Debug.Assert(idealCapacity >= rawTable._entries.Length);
            var newTable = new_uninitialized(idealCapacity);
            Debug.Assert(newTable._entries != null);
            Array.Fill(newTable._controls, EMPTY);
            CloneRawTable(this.rawTable, newTable);
            this.rawTable = newTable;
        }

        // Caller need to make sure `newRawTable` is clean and have enough capacity
        private void CloneRawTable(RawTableInner sourceRawTable, RawTableInner newRawTable)
        {
            Debug.Assert(sourceRawTable._entries is not null);
            Debug.Assert(newRawTable._entries is not null);
            Debug.Assert(newRawTable._entries.Length >= sourceRawTable._count);
            Debug.Assert(newRawTable._count == 0);

            // We can use a simpler version of insert() here since:
            // - there are no DELETED entries.
            // - we know there is enough space in the table.
            // - all elements are unique.
            byte[] oldCtrls = sourceRawTable._controls;
            Entry[] oldEntries = sourceRawTable._entries;
            Entry[] newEntries = newRawTable._entries;
            int length = sourceRawTable._entries.Length;
            // TODO: Maybe we could use IBITMASK to accllerate
            for (int i = 0; i < length; i++)
            {
                if (is_full(oldCtrls[i]))
                {
                    this.set_ctrl(i, oldCtrls[i], newRawTable._controls);
                    newEntries[i].Key = oldEntries[i].Key;
                    newEntries[i].Value = oldEntries[i].Value;
                }
            }
            newRawTable._growth_left -= sourceRawTable._count;
            newRawTable._count = sourceRawTable._count;
        }

        /// <summary>
        /// Ensures that the dictionary can hold up to 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        public int EnsureCapacity(int capacity)
        {
            // "capacity" is `this._count + this._growth_left` in the new implementation
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            int currentCapacity = this.rawTable._count + this.rawTable._growth_left;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }
            if (this.rawTable._entries == null)
            {
                this.Initialize(capacity);
            }
            else
            {
                this.Resize(capacity);
            }

            return this.rawTable._count + this.rawTable._growth_left;
        }


        /// <summary>
        /// Sets the capacity of this dictionary to what it would be if it had been originally initialized with all its entries
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        ///
        /// To allocate minimum size storage array, execute the following statements:
        ///
        /// dictionary.Clear();
        /// dictionary.TrimExcess();
        /// </remarks>
        public void TrimExcess() => TrimExcess(Count);

        /// <summary>
        /// Sets the capacity of this dictionary to hold up 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        /// </remarks>
        public void TrimExcess(int capacity)
        {
            if (capacity < Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            if (capacity == 0)
            {
                this.Initialize(capacity);
            }
            var idealCap = capacity_to_buckets(capacity);
            int currentCapacity = this.rawTable._count + this.rawTable._growth_left;
            if (idealCap >= currentCapacity)
            {
                return;
            }
            _version++;
            this.Resize(capacity);
        }

        /// <summary>
        /// Creates a new empty hash table without allocating any memory, using the
        /// given allocator.
        ///
        /// In effect this returns a table with exactly 1 bucket. However we can
        /// leave the data pointer dangling since that bucket is never written to
        /// due to our load factor forcing us to always have at least 1 free bucket.
        /// </summary>
        private static RawTableInner new_in()
        {
            // unlike rust, maybe we should convert this to a forever lived array that is allocated with specific value.
            byte[] _controls = _group.static_empty();
            return new RawTableInner
            {
                _bucket_mask = 0,
                _controls = _controls,
                _entries = null,
                _growth_left = 0,
                _count = 0
            };
        }

        /// <summary>
        /// Allocates a new hash table with the given number of buckets.
        ///
        /// The control bytes are left uninitialized.
        /// </summary>
        // unlike rust, we never cares about out of memory
        private static RawTableInner new_uninitialized(int buckets)
        {
            Debug.Assert(BitOperations.IsPow2(buckets));
            byte[] _controls = new byte[buckets + _group.WIDTH];
            Entry[] _entries = new Entry[buckets];
            return new RawTableInner
            {
                _bucket_mask = buckets - 1,
                _controls = _controls,
                _entries = _entries,
                _growth_left = MyDictionary<TKey, TValue>.bucket_mask_to_capacity(buckets - 1),
                _count = 0
            };
        }

        // always insert a new one
        // not check replace, caller should make sure
        private unsafe int find_insert_slot(int hash)
        {
            var probe_seq = new ProbeSeq(hash, rawTable._bucket_mask);
            while (true)
            {
                fixed (byte* ptr = &rawTable._controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bit = group.match_empty_or_deleted().lowest_set_bit();
                    if (bit.HasValue)
                    {
                        var result = (probe_seq.pos + bit.Value) & rawTable._bucket_mask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (is_full(rawTable._controls[result]))
                        {
                            Debug.Assert(rawTable._bucket_mask < _group.WIDTH);
                            Debug.Assert(probe_seq.pos != 0);
                            fixed (byte* ptr2 = &rawTable._controls[0])
                            {
                                return _group.load_aligned(ptr2)
                                    .match_empty_or_deleted()
                                    .lowest_set_bit_nonzero();
                            }
                        }
                        return result;
                    }
                }
                probe_seq.move_next(rawTable._bucket_mask);
            }
        }

        private bool IsKeyEqual(TKey key1, TKey key2)
        {
            // TODO: in Dictionary, this is a complex condition to improve performance, learn from it.
            var comparer = rawTable._comparer ?? EqualityComparer<TKey>.Default;
            return comparer.Equals(key1, key2);
        }

        internal ref TValue FindValue(TKey key)
        {
            ref Entry bucket = ref FindBucket(key);
            return ref bucket.Value;
        }

        private unsafe ref Entry FindBucket(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            var hash = GetHashCodeOfKey(key);
            var h2_hash = h2(hash);
            var probe_seq = new ProbeSeq(hash, rawTable._bucket_mask);
            ref Entry entry = ref Unsafe.NullRef<Entry>();
            while (true)
            {
                fixed (byte* ptr = &rawTable._controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        Debug.Assert(rawTable._entries != null);
                        // there must be set bit
                        var bit = bitmask.lowest_set_bit()!.Value;
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probe_seq.pos + bit) & rawTable._bucket_mask;
                        entry = ref rawTable._entries[index];
                        if (IsKeyEqual(key, entry.Key))
                        {
                            return ref entry;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return ref entry;
                    }
                }
                probe_seq.move_next(rawTable._bucket_mask);
            }
            // TODO: or maybe just
            // var index1 = this.FindBucketIndex(key);
            // ref Entry res = ref Unsafe.NullRef<Entry>();
            // if (index1.HasValue)
            // {
            //     res = ref this._entries[index1.Value];
            // }
            // return ref res;
        }

        private unsafe int? FindBucketIndex(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            Debug.Assert(rawTable._entries != null, "expected entries to be != null");

            var hash = GetHashCodeOfKey(key);
            var h2_hash = h2(hash);
            var probe_seq = new ProbeSeq(hash, rawTable._bucket_mask);
            ref Entry entry = ref Unsafe.NullRef<Entry>();
            while (true)
            {
                fixed (byte* ptr = &rawTable._controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        var bit = bitmask.lowest_set_bit()!.Value;
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probe_seq.pos + bit) & rawTable._bucket_mask;
                        entry = ref rawTable._entries[index];
                        if (IsKeyEqual(key, entry.Key))
                        {
                            return index;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return null;
                    }
                }
                probe_seq.move_next(rawTable._bucket_mask);
            }
        }

        private unsafe TValue erase(int index)
        {
            // Attention, we could not just only set mark to `Deleted` to assume it is deleted, the reference is still here, and GC would not collect it.
            Debug.Assert(is_full(rawTable._controls[index]));
            Debug.Assert(rawTable._entries != null, "entries should be non-null");
            int index_before = unchecked((index - _group.WIDTH)) & rawTable._bucket_mask;
            TValue res;
            fixed (byte* ptr_before = &rawTable._controls[index_before])
            fixed (byte* ptr = &rawTable._controls[index])
            {
                var empty_before = _group.load(ptr_before).match_empty();
                var empty_after = _group.load(ptr).match_empty();
                byte ctrl;
                // If we are inside a continuous block of Group::WIDTH full or deleted
                // cells then a probe window may have seen a full block when trying to
                // insert. We therefore need to keep that block non-empty so that
                // lookups will continue searching to the next probe window.
                //
                // Note that in this context `leading_zeros` refers to the bytes at the
                // end of a group, while `trailing_zeros` refers to the bytes at the
                // begining of a group.
                if (empty_before.leading_zeros() + empty_after.trailing_zeros() >= _group.WIDTH)
                {
                    ctrl = DELETED;
                }
                else
                {
                    ctrl = EMPTY;
                    rawTable._growth_left += 1;
                }
                set_ctrl(index, ctrl, this.rawTable._controls);
                rawTable._count -= 1;
                res = this.rawTable._entries[index].Value;
                // TODO: maybe we could remove this branch to improve perf. Or maybe CLR has optimised this.
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                {
                    this.rawTable._entries[index].Key = default!;
                }
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                {
                    this.rawTable._entries[index].Value = default!;
                }
            }
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void set_ctrl_h2(int index, int hash, byte[] controls)
        {
            set_ctrl(index, h2(hash), controls);
        }

        /// Sets a control byte, and possibly also the replicated control byte at
        /// the end of the array.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void set_ctrl(int index, byte ctrl, byte[] controls)
        {
            // Replicate the first Group::WIDTH control bytes at the end of
            // the array without using a branch:
            // - If index >= Group::WIDTH then index == index2.
            // - Otherwise index2 == self.bucket_mask + 1 + index.
            //
            // The very last replicated control byte is never actually read because
            // we mask the initial index for unaligned loads, but we write it
            // anyways because it makes the set_ctrl implementation simpler.
            //
            // If there are fewer buckets than Group::WIDTH then this code will
            // replicate the buckets at the end of the trailing group. For example
            // with 2 buckets and a group size of 4, the control bytes will look
            // like this:
            //
            //     Real    |             Replicated
            // ---------------------------------------------
            // | [A] | [B] | [EMPTY] | [EMPTY] | [A] | [B] |
            // ---------------------------------------------
            var index2 = ((index - _group.WIDTH) & rawTable._bucket_mask) + _group.WIDTH;
            controls[index] = ctrl;
            controls[index2] = ctrl;
        }

        // TODO: overflow, what about cap > uint.Max
        /// Returns the number of buckets needed to hold the given number of items,
        /// taking the maximum load factor into account.
        private static int capacity_to_buckets(int cap)
        {
            Debug.Assert(cap > 0);
            var capacity = (uint)cap;

            // For small tables we require at least 1 empty bucket so that lookups are
            // guaranteed to terminate if an element doesn't exist in the table.
            if (capacity < 8)
            {
                // We don't bother with a table size of 2 buckets since that can only
                // hold a single element. Instead we skip directly to a 4 bucket table
                // which can hold 3 elements.
                return (capacity < 4 ? 4 : 8);
            }

            // Otherwise require 1/8 buckets to be empty (87.5% load)
            //
            // Be careful when modifying this, calculate_layout relies on the
            // overflow check here.
            int adjusted_capacity;
            switch (capacity)
            {
                // 0x01FFFFFF is the max value that would not overflow when *8
                case <= 0x01FFFFFF:
                    adjusted_capacity = (int)unchecked(capacity * 8 / 7);
                    break;
                // 0x37FFFFFF is the max value that smaller than 0x0400_0000 after *8/7
                case <= 0x37FFFFFF:
                    return 0x4000_0000;
                default:
                    throw new Exception("capacity overflow");
            }

            // Any overflows will have been caught by the checked_mul. Also, any
            // rounding errors from the division above will be cleaned up by
            // next_power_of_two (which can't overflow because of the previous divison).
            return nextPowerOfTwo(adjusted_capacity);

            // TODO: what about this overflow?
            static int nextPowerOfTwo(int num)
            {
                num |= num >> 1;
                num |= num >> 2;
                num |= num >> 4;
                num |= num >> 8;
                num |= num >> 16;
                return num + 1;
            }
        }

        /// Returns the maximum effective capacity for the given bucket mask, taking
        /// the maximum load factor into account.
        private static int bucket_mask_to_capacity(int bucket_mask)
        {
            if (bucket_mask < 8)
            {
                // For tables with 1/2/4/8 buckets, we always reserve one empty slot.
                // Keep in mind that the bucket mask is one less than the bucket count.
                return bucket_mask;
            }
            else
            {
                // For larger tables we reserve 12.5% of the slots as empty.
                return (bucket_mask + 1) / 8 * 7;
            }
        }

        // Probe sequence based on triangular numbers, which is guaranteed (since our
        // table size is a power of two) to visit every group of elements exactly once.
        //
        // A triangular probe has us jump by 1 more group every time. So first we
        // jump by 1 group (meaning we just continue our linear scan), then 2 groups
        // (skipping over 1 group), then 3 groups (skipping over 2 groups), and so on.
        //
        // The proof is a simple number theory question: i*(i+1)/2 can walk through the complete residue system of 2n
        // to prove this, we could prove when "0 <= i <= j < 2n", "i * (i + 1) / 2 mod 2n == j * (j + 1) / 2" iff "i == j"
        // sufficient: we could have `(i-j)(i+j+1)=4n*k`, k is integer. It is obvious that if i!=j, the left part is odd, but right is always even.
        // So, the the only chance is i==j
        // necessary: obvious
        // Q.E.D.
        internal struct ProbeSeq
        {
            internal int pos;
            internal int stride;
            internal ProbeSeq(int hash, int bucket_mask)
            {
                this.pos = h1(hash) & bucket_mask;
                this.stride = 0;
            }
            internal void move_next(int bucket_mask)
            {
                // We should have found an empty bucket by now and ended the probe.
                Debug.Assert(this.stride <= bucket_mask, "Went past end of probe sequence");
                this.stride += _group.WIDTH;
                this.pos += this.stride;
                this.pos &= bucket_mask;
            }
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly MyDictionary<TKey, TValue> _dictionary;
            private readonly int _version;
            private KeyValuePair<TKey, TValue> _current;
            private IBitMask _currentBitMask;
            private int current_ctrl_offset;
            private readonly int _getEnumeratorRetType;  // What should Enumerator.Current return?
            private bool isValid; // valid when _current has correct value.
            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal unsafe Enumerator(MyDictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _getEnumeratorRetType = getEnumeratorRetType;
                _current = default;
                current_ctrl_offset = 0;
                isValid = false;
                fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                {
                    _currentBitMask = _group.load_aligned(ctrl).match_full();
                }
            }

            #region IDictionaryEnumerator
            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (!isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    Debug.Assert(_current.Key != null);
                    return new DictionaryEntry(_current.Key, _current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (!isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    Debug.Assert(_current.Key != null);
                    return _current.Key;
                }
            }

            object? IDictionaryEnumerator.Value
            {
                get
                {
                    if (!isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    return _current.Value;
                }
            }
            #endregion

            #region IEnumerator<KeyValuePair<TKey, TValue>>
            public KeyValuePair<TKey, TValue> Current => this._current;

            object? IEnumerator.Current
            {
                get
                {
                    if (!isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    Debug.Assert(_current.Key != null);

                    if (_getEnumeratorRetType == DictEntry)
                    {
                        return new DictionaryEntry(_current.Key, _current.Value);
                    }

                    return new KeyValuePair<TKey, TValue>(_current.Key, _current.Value);
                }
            }

            public void Dispose() { }

            public unsafe bool MoveNext()
            {
                if (_version != _dictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }
                while (true)
                {
                    var lowest_set_bit = this._currentBitMask.lowest_set_bit();
                    if (lowest_set_bit.HasValue)
                    {
                        Debug.Assert(this._dictionary.rawTable._entries != null);
                        isValid = true;
                        this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                        var entry = this._dictionary.rawTable._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                        this._current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        return true;
                    }
                    // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                    if (this.current_ctrl_offset + _group.WIDTH >= this._dictionary._buckets)
                    {
                        this._current = default;
                        isValid = false;
                        return false;
                    }

                    this.current_ctrl_offset += _group.WIDTH;
                    fixed (byte* ctrl = &_dictionary.rawTable._controls[current_ctrl_offset])
                    {
                        this._currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }
            }
            unsafe void IEnumerator.Reset()
            {
                if (_version != _dictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                _current = default;
                current_ctrl_offset = 0;
                isValid = false;
                fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                {
                    _currentBitMask = _group.load_aligned(ctrl).match_full();
                }
            }
            #endregion
        }

        [DebuggerTypeProxy(typeof(DictionaryKeyCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly MyDictionary<TKey, TValue> _dictionary;

            public KeyCollection(MyDictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            public void CopyTo(TKey[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                Debug.Assert(array != null);

                if (index < 0 || index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }
                // TODO: we might also use SIMD to pass through the control bytes, which would provide better performance for spare situation.
                foreach (var item in this)
                {
                    array[index++] = item;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            void ICollection<TKey>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            bool ICollection<TKey>.Contains(TKey item) =>
                _dictionary.ContainsKey(item);

            bool ICollection<TKey>.Remove(TKey item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
                return false;
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TKey[] keys)
                {
                    CopyTo(keys, index);
                }
                else
                {
                    object[]? objects = array as object[];

                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }

                    try
                    {
                        foreach (var item in this)
                        {
                            objects[index++] = item;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TKey>, IEnumerator
            {
                private readonly MyDictionary<TKey, TValue> _dictionary;
                private readonly int _version;
                private IBitMask _currentBitMask;
                private int current_ctrl_offset;
                private bool isValid; // valid when _current has correct value.
                private TKey? _current;

                internal unsafe Enumerator(MyDictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }

                public void Dispose() { }

                public unsafe bool MoveNext()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }
                    while (true)
                    {
                        var lowest_set_bit = this._currentBitMask.lowest_set_bit();
                        if (lowest_set_bit.HasValue)
                        {
                            Debug.Assert(this._dictionary.rawTable._entries != null);
                            isValid = true;
                            this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                            var entry = this._dictionary.rawTable._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                            this._current = entry.Key;
                            return true;
                        }
                        // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                        if (this.current_ctrl_offset + _group.WIDTH >= this._dictionary._buckets)
                        {
                            this._current = default;
                            isValid = false;
                            return false;
                        }

                        this.current_ctrl_offset += _group.WIDTH;
                        fixed (byte* ctrl = &_dictionary.rawTable._controls[current_ctrl_offset])
                        {
                            this._currentBitMask = _group.load_aligned(ctrl).match_full();
                        }
                    }
                }

                public TKey Current => _current!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (!isValid)
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _current;
                    }
                }

                unsafe void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }
            }
        }


        [DebuggerTypeProxy(typeof(DictionaryValueCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly MyDictionary<TKey, TValue> _dictionary;

            public ValueCollection(MyDictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            public void CopyTo(TValue[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if ((uint)index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                foreach (var item in this)
                {
                    array[index++] = item;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Remove(TValue item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
                return false;
            }

            void ICollection<TValue>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TValue[] values)
                {
                    CopyTo(values, index);
                }
                else
                {
                    object[]? objects = array as object[];

                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }

                    try
                    {
                        foreach (var item in this)
                        {
                            objects[index++] = item!;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TValue>, IEnumerator
            {
                private readonly MyDictionary<TKey, TValue> _dictionary;
                private readonly int _version;
                private IBitMask _currentBitMask;
                private int current_ctrl_offset;
                private bool isValid; // valid when _current has correct value.
                private TValue? _current;

                internal unsafe Enumerator(MyDictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }

                public void Dispose() { }

                public unsafe bool MoveNext()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }
                    while (true)
                    {
                        Debug.Assert(this._dictionary.rawTable._entries != null);
                        var lowest_set_bit = this._currentBitMask.lowest_set_bit();
                        if (lowest_set_bit.HasValue)
                        {
                            isValid = true;
                            this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                            var entry = this._dictionary.rawTable._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                            this._current = entry.Value;
                            return true;
                        }
                        // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                        if (this.current_ctrl_offset + _group.WIDTH >= this._dictionary._buckets)
                        {
                            this._current = default;
                            isValid = false;
                            return false;
                        }

                        this.current_ctrl_offset += _group.WIDTH;
                        fixed (byte* ctrl = &_dictionary.rawTable._controls[current_ctrl_offset])
                        {
                            this._currentBitMask = _group.load_aligned(ctrl).match_full();
                        }
                    }
                }

                public TValue Current => _current!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (!isValid)
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _current;
                    }
                }

                unsafe void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary.rawTable._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }
            }
        }
    }
}
