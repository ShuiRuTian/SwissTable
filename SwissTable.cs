using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Collections.Generic
{

    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    [TypeForwardedFrom("System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public partial class MyDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISerializable, IDeserializationCallback where TKey : notnull
    {
        private static ITriviaInfo _groupInfo = new Sse2TriviaInfo();
        private static IGroup _group = new Sse2Group();

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => false;

        private IEqualityComparer<TKey>? _comparer;


        // TODO: maybe we could allocate these two parts together.
        private int _capacity => _entries.Length;
        private int _bucket_mask => _capacity - 1;
        private int num_ctrl_bytes => _entries.Length + _groupInfo.WIDTH;
        private bool is_empty_singleton => _bucket_mask == 0;
        private byte[] _controls;
        private Entry[]? _entries;
        // Number of elements that can be inserted before we need to grow the table
        private int _growth_left;
        // number of actual values stored in the map
        private int _count = 0;


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
                // ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            if (capacity > 0)
            {
                Initialize(capacity);
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
                IEqualityComparer<string>? stringComparer = NonRandomizedStringEqualityComparer.GetStringComparer(_comparer);
                if (stringComparer is not null)
                {
                    _comparer = (IEqualityComparer<TKey>?)stringComparer;
                }
            }
        }

        public MyDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public MyDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer) :
            this(dictionary != null ? dictionary.Count : 0, comparer)
        {
            if (dictionary == null)
            {
                // ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
            }

            AddRange(dictionary);
        }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public MyDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) :
            this((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0, comparer)
        {
            if (collection == null)
            {
                // ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
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

        public void Add(TKey key, TValue value)
        {
            bool modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
            Debug.Assert(modified); // If there was an existing key and the Add failed, an exception will already have been thrown.
        }

        private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            if (_entries == null)
            {
                Initialize(0);
            }
            Debug.Assert(_entries != null);

            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "expected entries to be non-null");

            ref TValue oldValue = ref FindValue(key);

            if (!Unsafe.IsNullRef(ref oldValue))
            {
                if (behavior == InsertionBehavior.OverwriteExisting)
                {
                    oldValue = value;
                    return true;
                }

                if (behavior == InsertionBehavior.ThrowOnExisting)
                {
                    ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(key);
                }
                return false;
            }

            var hashCode = GetHashCodeOfKey(key);
            // We can avoid growing the table once we have reached our load
            // factor if we are replacing a tombstone(Delete). This works since the
            // number of EMPTY slots does not change in this case.
            var index = find_insert_slot(hashCode);
            var old_ctrl = _controls[index];
            if (_growth_left == 0 && special_is_empty(old_ctrl))
            {
                self.reserve(1, hasher);
                index = find_insert_slot(hashCode);
            }
            record_item_insert_at(index, old_ctrl, hashCode);
            _entries[index].value = value;
            return true;
        }

        private void record_item_insert_at(int index, byte old_ctrl, int hash)
        {
            _growth_left -= special_is_empty(old_ctrl) ? 1 : 0;
            set_ctrl_h2(index, hash);
            _count += 1;
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

        private int Initialize(int capacity)
        {
            int size = capacity_to_buckets(capacity);
            var buckets = new Entry[size];
            var controls = new byte[size + _groupInfo.WIDTH];
            _count = 0;

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _entries = buckets;
            _controls = controls;

            return size;
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

        private bool Equal(TKey key1, TKey key2)
        {
            // TODO: in Dictionary, this is a complex condition to improve performance, learn from it.
            var comparer = _comparer ?? EqualityComparer<TKey>.Default;
            return comparer.Equals(key1, key2);
        }

        private int GetHashCodeOfKey(TKey key)
        {
            IEqualityComparer<TKey>? comparer = _comparer;
            var hash = (comparer == null) ? key.GetHashCode() : comparer.GetHashCode(key);
            return hash;
        }
        internal unsafe ref TValue FindValue(TKey key)
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
                        var bit = bitmask.lowest_set_bit().Value;
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probe_seq.pos + bit) & _bucket_mask;
                        entry = ref _entries[index];
                        if (Equal(key, entry.key))
                        {
                            ref TValue value = ref entry.value;
                            return ref value;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return ref Unsafe.NullRef<TValue>(); ;
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

        private unsafe void erase(int index)
        {
            // TODO: Attention, in language like c#, we could not just only set mark to Delete to assume it is deleted, the reference is still here, and GC would not collect it.
            Debug.Assert(is_full(_controls[index]));
            int index_before;
            unchecked
            {
                index_before = (index - _groupInfo.WIDTH) & _bucket_mask;
            }

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
            }
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
        private int capacity_to_buckets(int cap)
        {
            Debug.Assert(cap >= 0);

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
            var adjusted_cap = cap / 7 * 8;

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
        /// if this equal is true, we could have `(i-j)(i+j+1)=4n*k`, k is integer. This is obvious if i!=j, the left part is odd, but right is always even.
        /// So, the the only chance is i==j. Q.E.D
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
    }
}