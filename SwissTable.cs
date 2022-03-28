using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Collections.Generic
{

    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    [TypeForwardedFrom("System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public partial class MyDictionary<TKey, TValue> : IDictionary<TKey, TValue> //, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISerializable, IDeserializationCallback where TKey : notnull
    {
        private static readonly ITriviaInfo _groupInfo = new Sse2TriviaInfo();
        private static readonly IGroup _group = new Sse2Group();

        private struct RawTableInner
        {
            internal int _bucket_mask;
            internal byte[] _controls;
            internal Entry[]? _entries;
            internal int _growth_left;
            internal int _count;
        }

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => false;

        // each add/remove will add one version
        // For Enumerator, if version is changed, one error will be thrown for enumerator should not changed.
        private readonly int _version;

        private IEqualityComparer<TKey>? _comparer;

        // The real space that the swisstable allocated
        // always be the power of 2
        // This means the upper bound is not Int32.MaxValue(0x7FFFF_FFFF), but 0x4000_0000
        // This is hard(impossible?) to be larger, for the length of array is limited to 0x7FFFF_FFFF
        private int _buckets => this._bucket_mask + 1;

        // Mask to get an index from a hash value. The value is one less than the
        // number of buckets in the table.
        private int num_ctrl_bytes => _entries.Length + _groupInfo.WIDTH;
        private bool is_empty_singleton => _bucket_mask == 0;

        private int _bucket_mask;
        private byte[] _controls;
        private Entry[]? _entries;
        // Number of elements that can be inserted before we need to grow the table
        // This need to be calculated individually, for the tombstone("DELETE")
        private int _growth_left;
        // number of real values stored in the map
        // `items` in rust
        private int _count;

        // Control byte value for an empty bucket.
        private const byte EMPTY = 0b1111_1111;

        /// Control byte value for a deleted bucket.
        private const byte DELETED = 0b1000_0000;

        /// Checks whether a control byte represents a full bucket (top bit is clear).
        // #[inline]
        private static bool is_full(byte ctrl) => (ctrl & 0x80) == 0;

        /// Checks whether a control byte represents a special value (top bit is set).
        // #[inline]
        private static bool is_special(byte ctrl) => (ctrl & 0x80) != 0;

        /// Checks whether a special control value is EMPTY (just check 1 bit).
        // #[inline]
        private static bool special_is_empty(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return (ctrl & 0x01) != 0;
        }

        /// Primary hash function, used to select the initial bucket to probe from.
        // #[inline]
        // #[allow(clippy::cast_possible_truncation)]
        private static int h1(int hash)
        {
            // On 32-bit platforms we simply ignore the higher hash bits.
            return hash;
        }

        /// Secondary hash function, saved in the low 7 bits of the control byte.
        // #[inline]
        // #[allow(clippy::cast_possible_truncation)]
        private static byte h2(int hash)
        {
            // Grab the top 7 bits of the hash. While the hash is normally a full 64-bit
            // value, some hash functions (such as FxHash) produce a usize result
            // instead, which means that the top 32 bits are 0 on 32-bit platforms.
            var top7 = hash >> 25;
            return (byte)(top7 & 0x7f); // truncation
        }

        public MyDictionary() : this(0, null) { }

        public MyDictionary(int capacity) : this(capacity, null) { }

        public MyDictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

        public MyDictionary(int capacity, IEqualityComparer<TKey>? comparer)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            switch (capacity)
            {
                case < 0:
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
                    break;
                default:
                    Initialize(capacity);
                    break;
            }

            if (comparer is not null && comparer != EqualityComparer<TKey>.Default) // first check for null to avoid forcing default comparer instantiation unnecessarily
            {
                _comparer = comparer;
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

            AddRange(dictionary);
        }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) :
            this((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0, comparer)
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }

            AddRange(collection);
        }

        private void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            // It is likely that the passed-in dictionary is Dictionary<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation and overhead by looping through the entries array directly.
            // We only do this when dictionary is Dictionary<TKey,TValue> and not a subclass, to maintain
            // back-compat with subclasses that may have overridden the enumerator behavior.
            if (collection.GetType() == typeof(MyDictionary<TKey, TValue>))
            {
                MyDictionary<TKey, TValue> source = (MyDictionary<TKey, TValue>)collection;

                if (source.Count == 0)
                {
                    // Nothing to copy, all done
                    return;
                }

                // This is not currently a true .AddRange as it needs to be an initalized dictionary
                // of the correct size, and also an empty dictionary with no current entities (and no argument checks).
                Debug.Assert(source._entries is not null);
                Debug.Assert(_entries is not null);
                Debug.Assert(_entries.Length >= source.Count);
                Debug.Assert(_count == 0);

                byte[] oldCtrls = source._controls;
                Entry[] oldEntries = source._entries;

                // TODO: Is there a quick way? just copy is one approach, but I want to do it "clean"(No "delete" control byte)
                // if (source._comparer == _comparer)
                // {
                //     // If comparers are the same, we can copy _entries without rehashing.
                //     CopyEntries(oldEntries, source._count);
                //     return;
                // }

                // Comparers differ need to rehash all the entires via Add
                int count = source._count;
                for (int i = 0; i < count; i++)
                {
                    // Only copy if an entry
                    if (is_full(oldCtrls[i]))
                    {
                        Add(oldEntries[i].key, oldEntries[i].value);
                    }
                }
                return;
            }

            // Fallback path for IEnumerable that isn't a non-subclassed Dictionary<TKey,TValue>.
            foreach (KeyValuePair<TKey, TValue> pair in collection)
            {
                Add(pair.Key, pair.Value);
            }
        }

        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            // TODO: maybe need to duplicate most of code with `Remove(TKey key)` for performance issue, see C# old implementation
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            if (this._entries != null)
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

        #region IDictionary<TKey, TValue>
        TValue IDictionary<TKey, TValue>.this[TKey key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => throw new NotImplementedException();

        ICollection<TValue> IDictionary<TKey, TValue>.Values => throw new NotImplementedException();

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
            if (this._entries != null)
            {
                var index = this.FindBucketIndex(key);
                if (index != null)
                {
                    this.erase(index.Value);
                }
            }
            return false;
        }

        bool IDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region ICollection<KeyValuePair<TKey, TValue>>
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Clear()
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region IEnumerable<KeyValuePair<TKey, TValue>>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHashCodeOfKey(TKey key)
        {
            return (this._comparer == null) ? key.GetHashCode() : this._comparer.GetHashCode(key);
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
            var hashCode = this.GetHashCodeOfKey(key);
            // We can avoid growing the table once we have reached our load
            // factor if we are replacing a tombstone(Delete). This works since the
            // number of EMPTY slots does not change in this case.
            var index = find_insert_slot(hashCode);
            var old_ctrl = _controls[index];
            if (this._growth_left == 0 && special_is_empty(old_ctrl))
            {
                this.EnsureCapacity(this._count + 1);
                index = find_insert_slot(hashCode);
            }
            Debug.Assert(_entries != null, "entries should be non-null");
            if (is_full(old_ctrl))
            {
                switch (behavior)
                {
                    case InsertionBehavior.OverwriteExisting:
                        this._entries[index].key = key;
                        this._entries[index].value = value;
                        return true;
                    case InsertionBehavior.ThrowOnExisting:
                        ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(key);
                        break;
                    case InsertionBehavior.None:
                        return false;
                }
                Debug.Fail("unhandled behavior");
            }
            record_item_insert_at(index, old_ctrl, hashCode);
            this._entries[index].key = key;
            this._entries[index].value = value;
            return true;
        }

        private void record_item_insert_at(int index, byte old_ctrl, int hash)
        {
            this._growth_left -= special_is_empty(old_ctrl) ? 1 : 0;
            set_ctrl_h2(index, hash);
            this._count += 1;
        }

        private struct Entry
        {
            public TKey key;
            public TValue value;
        }


        // why we need forceNewHashCodes?
        private void Resize(int newSize, bool forceNewHashCodes)
        {
            // Value types never rehash
            Debug.Assert(!forceNewHashCodes || !typeof(TKey).IsValueType);
            Debug.Assert(_entries != null, "_buckets should be non-null");
            Debug.Assert(newSize >= _entries.Length);
        }

        // allocate and initialize when with real capacity
        // this means we do not want to use any existing data, including resize or use dictionary initialize
        // `realisticCapacity` is any number
        private void Initialize(int realisticCapacity)
        {
            RawTableInner innerTable;
            if (realisticCapacity == 0)
            {
                innerTable = new_in();
                this._controls = innerTable._controls;
                this._entries = innerTable._entries;
                return;
            }
            var idealCapacity = capacity_to_buckets(realisticCapacity);
            innerTable = new_uninitialized(idealCapacity);
            Array.Fill(innerTable._controls, EMPTY);
            this._bucket_mask = innerTable._bucket_mask;
            this._controls = innerTable._controls;
            this._entries = innerTable._entries;
            this._count = innerTable._count;
            this._growth_left = innerTable._growth_left;
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
            int currentCapacity = this._count + this._growth_left;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }
            if (this._entries == null)
            {
                this.Initialize(capacity);
            }
            else
            {
                // resize
            }

            return this._count;
        }

        // additional could not be overflow in our use cases. In fact, we could convert it to capacity!
        private void reserve_rehash_inner(int additional)
        {
            var new_items = _buckets;
            var full_capacity = bucket_mask_to_capacity(this._bucket_mask);
            if (new_items <= full_capacity / 2)
            {
                this.rehash_in_place();
            }
            else
            {
                this.resize_inner(new_items > full_capacity ? new_items : full_capacity);
            }
        }

        // unlike rust, we do not need rehash, for we does not provide a customized hasher
        // Instead, this function clean current hashmap, set control byte from delete to empty, and free the reference 
        private void rehash_in_place()
        {
            this.prepare_rehash_in_place();
            for (int i = 0; i < _buckets; i++)
            {
                if (_controls[i] == DELETED)
                {
                    _controls[i] = EMPTY;
                    // FIXME: use RuntimeHelpers.IsReferenceOrContainsReferences<TKey>()
                    if (!typeof(TValue).IsValueType)
                    {
                        _entries[i].value = default!;
                    }
                    this._count -= 1;
                }
            }
            this._growth_left = bucket_mask_to_capacity(this._bucket_mask) - this._count;
            throw new NotImplementedException();

            // At this point, DELETED elements are elements that we haven't
            // rehashed yet. Find them and re-insert them at their ideal
            // position.
            for (int i = 0; i < this._buckets; i++)
            {
                if (this._controls[i] != DELETED)
                {
                    continue;
                }
                var i_p = this._entries[i];

            }
        }

        private void resize_inner(int capacity)
        {


        }

        private void prepare_resize(int capacity)
        {
            Debug.Assert(this._count <= capacity);
        }

        unsafe private void prepare_rehash_in_place()
        {
            for (int i = 0; i < this._buckets; i += _groupInfo.WIDTH)
            {
                fixed (byte* ctrl = &_controls[i])
                {
                    var group = _group.load_aligned(ctrl);
                    group = group.convert_special_to_empty_and_full_to_deleted();
                    group.store_aligned(ctrl);
                }
            }
            // Fix up the trailing control bytes. See the comments in set_ctrl
            // for the handling of tables smaller than the group width.
            int copyCount = _buckets < _groupInfo.WIDTH ? _buckets : _groupInfo.WIDTH;
            int sourceStartIndex = _buckets < _groupInfo.WIDTH ? _groupInfo.WIDTH : _buckets;
            var srcSpan = new Span<byte>(_controls, 0, copyCount);
            var dstSpan = new Span<byte>(_controls, sourceStartIndex, copyCount);
            srcSpan.CopyTo(dstSpan);
        }

        /// <summary>
        /// Creates a new empty hash table without allocating any memory, using the
        /// given allocator.
        ///
        /// In effect this returns a table with exactly 1 bucket. However we can
        /// leave the data pointer dangling since that bucket is never written to
        /// due to our load factor forcing us to always have at least 1 free bucket.
        /// </summary>
        private RawTableInner new_in()
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
        private RawTableInner new_uninitialized(int buckets)
        {
            Debug.Assert(BitOperations.IsPow2(buckets));
            byte[] _controls = new byte[buckets + _groupInfo.WIDTH];
            Entry[] _entries = new Entry[buckets];
            return new RawTableInner
            {
                _bucket_mask = buckets - 1,
                _controls = _controls,
                _entries = _entries,
                _growth_left = bucket_mask_to_capacity(buckets - 1),
                _count = 0
            };
        }

        private unsafe int find_insert_slot(int hash)
        {
            var probe_seq = new ProbeSeq(hash, _bucket_mask);
            while (true)
            {
                fixed (byte* ptr = &_controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bit = group.match_empty_or_deleted().lowest_set_bit();
                    if (bit.HasValue)
                    {
                        var result = (probe_seq.pos + bit.Value) & _bucket_mask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (is_full(_controls[result]))
                        {
                            Debug.Assert(_bucket_mask < _groupInfo.WIDTH);
                            Debug.Assert(probe_seq.pos != 0);
                            fixed (byte* ptr2 = &_controls[0])
                            {
                                return _group.load_aligned(ptr2)
                                    .match_empty_or_deleted()
                                    .lowest_set_bit_nonzero();
                            }
                        }
                        return result;
                    }
                }
                probe_seq.move_next(_bucket_mask);
            }
        }

        private bool IsKeyEqual(TKey key1, TKey key2)
        {
            // TODO: in Dictionary, this is a complex condition to improve performance, learn from it.
            var comparer = _comparer ?? EqualityComparer<TKey>.Default;
            return comparer.Equals(key1, key2);
        }

        internal ref TValue FindValue(TKey key)
        {
            ref Entry bucket = ref FindBucket(key);
            return ref bucket.value;
        }

        private unsafe ref Entry FindBucket(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            Debug.Assert(_entries != null, "expected entries to be != null");

            var hash = GetHashCodeOfKey(key);
            var h2_hash = h2(hash);
            var probe_seq = new ProbeSeq(hash, _bucket_mask);
            ref Entry entry = ref Unsafe.NullRef<Entry>();
            while (true)
            {
                fixed (byte* ptr = &_controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        var bit = bitmask.lowest_set_bit()!.Value;
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probe_seq.pos + bit) & _bucket_mask;
                        entry = ref _entries[index];
                        if (IsKeyEqual(key, entry.key))
                        {
                            return ref entry;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return ref entry;
                    }
                }
                probe_seq.move_next(_bucket_mask);
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
            Debug.Assert(_entries != null, "expected entries to be != null");

            var hash = GetHashCodeOfKey(key);
            var h2_hash = h2(hash);
            var probe_seq = new ProbeSeq(hash, _bucket_mask);
            ref Entry entry = ref Unsafe.NullRef<Entry>();
            while (true)
            {
                fixed (byte* ptr = &_controls[probe_seq.pos])
                {
                    var group = _group.load(ptr);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        var bit = bitmask.lowest_set_bit()!.Value;
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probe_seq.pos + bit) & _bucket_mask;
                        entry = ref _entries[index];
                        if (IsKeyEqual(key, entry.key))
                        {
                            return index;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return null;
                    }
                }
                probe_seq.move_next(_bucket_mask);
            }
        }

        /// Marks all table buckets as empty without dropping their contents.
        private void clear_no_drop()
        {
            if (!is_empty_singleton)
            {
                Array.Fill(_controls, EMPTY);
            }
            _count = 0;
            _growth_left = bucket_mask_to_capacity(_bucket_mask);
        }



        private unsafe TValue erase(int index)
        {
            // Attention, we could not just only set mark to `Deleted` to assume it is deleted, the reference is still here, and GC would not collect it.
            Debug.Assert(is_full(_controls[index]));
            Debug.Assert(_entries != null, "entries should be non-null");
            int index_before = unchecked((index - _groupInfo.WIDTH)) & _bucket_mask;
            TValue res;
            fixed (byte* ptr_before = &_controls[index_before])
            fixed (byte* ptr = &_controls[index])
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
                if (empty_before.leading_zeros() + empty_after.trailing_zeros() >= _groupInfo.WIDTH)
                {
                    ctrl = DELETED;
                }
                else
                {
                    ctrl = EMPTY;
                    _growth_left += 1;
                }
                set_ctrl(index, ctrl);
                _count -= 1;
                res = this._entries[index].value;
                // TODO: maybe we could remove this branch to improve perf. Or maybe CLR has optimised this.
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                {
                    this._entries[index].key = default!;
                }
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                {
                    this._entries[index].value = default!;
                }
            }
            return res;
        }

        private void set_ctrl_h2(int index, int hash)
        {
            set_ctrl(index, h2(hash));
        }

        /// Sets a control byte, and possibly also the replicated control byte at
        /// the end of the array.
        private void set_ctrl(int index, byte ctrl)
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
            var index2 = (((index - (_groupInfo.WIDTH))) & _bucket_mask) + _groupInfo.WIDTH;
            _controls[index] = ctrl;
            _controls[index2] = ctrl;
        }

        // TODO: overflow, what about cap > uint.Max
        /// Returns the number of buckets needed to hold the given number of items,
        /// taking the maximum load factor into account.
        ///
        private int capacity_to_buckets(int cap)
        {
            Debug.Assert(cap > 0);

            // For small tables we require at least 1 empty bucket so that lookups are
            // guaranteed to terminate if an element doesn't exist in the table.
            if (cap < 8)
            {
                // We don't bother with a table size of 2 buckets since that can only
                // hold a single element. Instead we skip directly to a 4 bucket table
                // which can hold 3 elements.
                return (cap < 4 ? 4 : 8);
            }

            // Otherwise require 1/8 buckets to be empty (87.5% load)
            //
            // Be careful when modifying this, calculate_layout relies on the
            // overflow check here.
            var adjusted_cap = checked(cap * 8 / 7);

            // Any overflows will have been caught by the checked_mul. Also, any
            // rounding errors from the division above will be cleaned up by
            // next_power_of_two (which can't overflow because of the previous divison).
            return nextPowerOfTwo(adjusted_cap);

            // TODO: what about this overflow?
            int nextPowerOfTwo(int num)
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
        // #[inline]
        private int bucket_mask_to_capacity(int bucket_mask)
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

        /// Probe sequence based on triangular numbers, which is guaranteed (since our
        /// table size is a power of two) to visit every group of elements exactly once.
        ///
        /// A triangular probe has us jump by 1 more group every time. So first we
        /// jump by 1 group (meaning we just continue our linear scan), then 2 groups
        /// (skipping over 1 group), then 3 groups (skipping over 2 groups), and so on.
        ///
        /// The proof is a simple number theory question: i*(i+1)/2 can walk through the complete residue system of 2n
        /// to prove this, we could prove when `0 <= i <= j < 2n`, `i*(i+1)/2 mod 2n == j*(j+1)/2` iff `i == j`
        /// sufficient: we could have `(i-j)(i+j+1)=4n*k`, k is integer. It is obvious that if i!=j, the left part is odd, but right is always even.
        /// So, the the only chance is i==j
        /// necessary: obvious
        /// Q.E.D.
        struct ProbeSeq
        {
            public int pos;
            public int stride;
            public ProbeSeq(int hash, int bucket_mask)
            {
                this.pos = h1(hash) & bucket_mask;
                this.stride = 0;
            }
            public void move_next(int bucket_mask)
            {
                // We should have found an empty bucket by now and ended the probe.
                Debug.Assert(this.stride <= bucket_mask, "Went past end of probe sequence");
                this.stride += _groupInfo.WIDTH;
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

            unsafe internal Enumerator(MyDictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _getEnumeratorRetType = getEnumeratorRetType;
                _current = default;
                current_ctrl_offset = 0;
                isValid = false;
                fixed (byte* ctrl = &_dictionary._controls[0])
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
            KeyValuePair<TKey, TValue> IEnumerator<KeyValuePair<TKey, TValue>>.Current => this._current;

            object IEnumerator.Current
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

            void IDisposable.Dispose() { }

            unsafe public bool MoveNext()
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
                        isValid = true;
                        this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                        var entry = this._dictionary._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                        this._current = new KeyValuePair<TKey, TValue>(entry.key, entry.value);
                        return true;
                    }
                    // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                    if (this.current_ctrl_offset + _groupInfo.WIDTH >= this._dictionary._buckets)
                    {
                        this._current = default;
                        isValid = false;
                        return false;
                    }

                    fixed (byte* ctrl = &_dictionary._controls[current_ctrl_offset])
                    {
                        this._currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                    this.current_ctrl_offset += _groupInfo.WIDTH;
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
                fixed (byte* ctrl = &_dictionary._controls[0])
                {
                    _currentBitMask = _group.load_aligned(ctrl).match_full();
                }
            }
            #endregion
        }

        // [DebuggerTypeProxy(typeof(DictionaryKeyCollectionDebugView<,>))]
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

                    int count = _dictionary._count;
                    Entry[]? entries = _dictionary._entries;
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

                unsafe internal Enumerator(MyDictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }

                public void Dispose() { }

                unsafe public bool MoveNext()
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
                            isValid = true;
                            this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                            var entry = this._dictionary._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                            this._current = entry.key;
                            return true;
                        }
                        // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                        if (this.current_ctrl_offset + _groupInfo.WIDTH >= this._dictionary._buckets)
                        {
                            this._current = default;
                            isValid = false;
                            return false;
                        }

                        fixed (byte* ctrl = &_dictionary._controls[current_ctrl_offset])
                        {
                            this._currentBitMask = _group.load_aligned(ctrl).match_full();
                        }
                        this.current_ctrl_offset += _groupInfo.WIDTH;
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
                    fixed (byte* ctrl = &_dictionary._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }
            }
        }


        // [DebuggerTypeProxy(typeof(DictionaryValueCollectionDebugView<,>))]
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

            public struct Enumerator : IEnumerator<TValue>, IEnumerator
            {
                private readonly MyDictionary<TKey, TValue> _dictionary;
                private readonly int _version;
                private IBitMask _currentBitMask;
                private int current_ctrl_offset;
                private bool isValid; // valid when _current has correct value.
                private TValue? _current;

                unsafe internal Enumerator(MyDictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _current = default;
                    current_ctrl_offset = 0;
                    isValid = false;
                    fixed (byte* ctrl = &_dictionary._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }

                public void Dispose() { }

                unsafe public bool MoveNext()
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
                            isValid = true;
                            this._currentBitMask = this._currentBitMask.remove_lowest_bit();
                            var entry = this._dictionary._entries[this.current_ctrl_offset + lowest_set_bit.Value];
                            this._current = entry.value;
                            return true;
                        }
                        // Shoudl we use closure here? What about the perf? Would CLR optimise this?
                        if (this.current_ctrl_offset + _groupInfo.WIDTH >= this._dictionary._buckets)
                        {
                            this._current = default;
                            isValid = false;
                            return false;
                        }

                        fixed (byte* ctrl = &_dictionary._controls[current_ctrl_offset])
                        {
                            this._currentBitMask = _group.load_aligned(ctrl).match_full();
                        }
                        this.current_ctrl_offset += _groupInfo.WIDTH;
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
                    fixed (byte* ctrl = &_dictionary._controls[0])
                    {
                        _currentBitMask = _group.load_aligned(ctrl).match_full();
                    }
                }
            }
        }
    }
}