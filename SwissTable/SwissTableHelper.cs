// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

#pragma warning disable CA1810 // Initialize reference type static fields inline

namespace System.Collections.Generic
{
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
        private int _stride;
        private readonly int _bucket_mask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ProbeSeq(int hash, int bucket_mask)
        {
            this._bucket_mask = bucket_mask;
            this.pos = SwissTableHelper.h1(hash) & bucket_mask;
            this._stride = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void move_next()
        {
            // We should have found an empty bucket by now and ended the probe.
            Debug.Assert(this._stride <= _bucket_mask, "Went past end of probe sequence");
            this._stride += SwissTableHelper.GROUP_WIDTH;
            this.pos += this._stride;
            this.pos &= _bucket_mask;
        }
    }

    internal static class SwissTableHelper
    {
        static unsafe SwissTableHelper()
        {
            if (Avx2.IsSupported)
            {
                // 256 bits(AVX2 use vector 256) / 8 (byte bits) = 32 bytes
                GROUP_WIDTH = 256 / 8;
            }
            else
           if (Sse2.IsSupported)
            {
                // 128 bits(SSE2 use vector 128) / 8 (byte bits) = 16 bytes
                GROUP_WIDTH = 128 / 8;
            }
            else
            {
                GROUP_WIDTH = sizeof(nuint);
            }
        }

        public static int GROUP_WIDTH;

        /// Control byte value for an empty bucket.
        public const byte EMPTY = 0b1111_1111;

        /// Control byte value for a deleted bucket.
        public const byte DELETED = 0b1000_0000;

        /// Checks whether a control byte represents a full bucket (top bit is clear).
        public static bool is_full(byte ctrl) => (ctrl & 0x80) == 0;

        /// Checks whether a control byte represents a special value (top bit is set).
        public static bool is_special(byte ctrl) => (ctrl & 0x80) != 0;

        /// Checks whether a special control value is EMPTY (just check 1 bit).
        public static bool special_is_empty(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return (ctrl & 0x01) != 0;
        }

        /// Primary hash function, used to select the initial bucket to probe from.
        public static int h1(int hash)
        {
            return hash;
        }

        /// Secondary hash function, saved in the low 7 bits of the control byte.
        public static byte h2(int hash)
        {
            // Grab the top 7 bits of the hash.
            // cast to uint to use `shr` rahther than `sar`, which makes sure the top bit of returned byte is 0.
            var top7 = (uint)hash >> 25;
            return (byte)top7;
            //return (byte)(hash & 0x0000_007F);
        }

        // DISPATHCH METHODS

        // Generally we do not want to duplicate code, but for performance(use struct and inline), we have to do so.
        // The difference between mirror implmentations should only be `_dummyGroup` except `MoveNext`, in which we use C++ union trick

        // For enumerator, which need record the current state
        [StructLayout(LayoutKind.Explicit)]
        internal struct BitMaskUnion
        {
            [FieldOffset(0)]
            internal Avx2BitMask avx2BitMask;
            [FieldOffset(0)]
            internal Sse2BitMask sse2BitMask;
            [FieldOffset(0)]
            internal FallbackBitMask fallbackBitMask;
        }

        // maybe we should just pass bucket_mask in as parater rather than calculate
        private static int GetBucketMaskFromControlsLength(int controlsLength)
        {
            Debug.Assert(controlsLength >= GROUP_WIDTH);
            if (controlsLength == GROUP_WIDTH)
                return 0;
            else
                return controlsLength - GROUP_WIDTH - 1;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] DispatchGetEmptyControls()
        {
            if (Avx2.IsSupported)
            {
                return Avx2Group.static_empty;
            }
            else
           if (Sse2.IsSupported)
            {
                return Sse2Group.static_empty;
            }
            else
            {
                return FallbackGroup.static_empty;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMaskUnion DispatchGetMatchFullBitMask(byte[] controls, int index)
        {
            if (Avx2.IsSupported)
            {
                return GetMatchFullBitMaskForAvx2(controls, index);
            }
            else
           if (Sse2.IsSupported)
            {
                return GetMatchFullBitMaskForSse2(controls, index);
            }
            else
            {
                return GetMatchFullBitMaskForFallback(controls, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForAvx2(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.avx2BitMask = Avx2Group.load(ctrl).match_full();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForSse2(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.sse2BitMask = Sse2Group.load(ctrl).match_full();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForFallback(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.fallbackBitMask = FallbackGroup.load(ctrl).match_full();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref MyDictionary<TKey, TValue>.Entry DispatchMoveNextDictionary<TKey, TValue>(
            int version,
            int tolerantVersion,
            in MyDictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return ref MoveNextDictionaryForAvx2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
            else
           if (Sse2.IsSupported)
            {
                return ref MoveNextDictionaryForSse2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
            else
            {
                return ref MoveNextDictionaryForFallback(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref MyDictionary<TKey, TValue>.Entry MoveNextDictionaryForAvx2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in MyDictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.avx2BitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForAvx2(controls, currentCtrlOffset).avx2BitMask;
                realBitMask = realBitMask.And(newBitMask);
            }

            while (true)
            {
                var lowest_set_bit = realBitMask.lowest_set_bit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.remove_lowest_bit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }
                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForAvx2(controls, currentCtrlOffset).avx2BitMask;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref MyDictionary<TKey, TValue>.Entry MoveNextDictionaryForSse2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in MyDictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.sse2BitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForSse2(controls, currentCtrlOffset).sse2BitMask;
                realBitMask = realBitMask.And(newBitMask);
            }
            while (true)
            {
                var lowest_set_bit = realBitMask.lowest_set_bit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.remove_lowest_bit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForSse2(controls, currentCtrlOffset).sse2BitMask;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref MyDictionary<TKey, TValue>.Entry MoveNextDictionaryForFallback<TKey, TValue>(
            int version,
            int tolerantVersion,
            in MyDictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.fallbackBitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForFallback(controls, currentCtrlOffset);
                realBitMask = realBitMask.And(newBitMask.fallbackBitMask);
            }
            while (true)
            {
                var lowest_set_bit = realBitMask.lowest_set_bit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.remove_lowest_bit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForFallback(controls, currentCtrlOffset).fallbackBitMask;
            }
        }

        // If we are inside a continuous block of Group::WIDTH full or deleted
        // cells then a probe window may have seen a full block when trying to
        // insert. We therefore need to keep that block non-empty so that
        // lookups will continue searching to the next probe window.
        //
        // Note that in this context `leading_zeros` refers to the bytes at the
        // end of a group, while `trailing_zeros` refers to the bytes at the
        // begining of a group.
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool DispatchIsEraseSafeToSetEmptyControlFlag(int bucketMask, byte[] controls, int index)
        {
            if (Avx2.IsSupported)
            {
                return IsEraseSafeToSetEmptyControlFlagForAvx2(bucketMask, controls, index);
            }
            else
           if (Sse2.IsSupported)
            {
                return IsEraseSafeToSetEmptyControlFlagForSse2(bucketMask, controls, index);
            }
            else
            {
                return IsEraseSafeToSetEmptyControlFlagForFallback(bucketMask, controls, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForAvx2(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = Avx2Group.load(ptr_before).match_empty();
                var empty_after = Avx2Group.load(ptr).match_empty();
                return empty_before.leading_zeros() + empty_after.trailing_zeros() < GROUP_WIDTH;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForSse2(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = Sse2Group.load(ptr_before).match_empty();
                var empty_after = Sse2Group.load(ptr).match_empty();
                return empty_before.leading_zeros() + empty_after.trailing_zeros() < GROUP_WIDTH;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForFallback(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = FallbackGroup.load(ptr_before).match_empty();
                var empty_after = FallbackGroup.load(ptr).match_empty();
                return empty_before.leading_zeros() + empty_after.trailing_zeros() < GROUP_WIDTH;
            }
        }

        public static ref MyDictionary<TKey, TValue>.Entry DispatchFindBucketOfDictionary<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return ref FindBucketOfDictionaryForAvx2(dictionary, key);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static unsafe ref MyDictionary<TKey, TValue>.Entry FindBucketOfDictionaryForAvx2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.match_byte(h2_hash);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.any_bit_set())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.lowest_set_bit_nonzero();
                                bitmask = bitmask.remove_lowest_bit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.match_empty().any_bit_set())
                            {
                                return ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                            }
                            probeSeq.move_next();
                        }
                    }
                }
                else
                {
                    EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.match_byte(h2_hash);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.any_bit_set())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.lowest_set_bit_nonzero();
                                bitmask = bitmask.remove_lowest_bit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.match_empty().any_bit_set())
                            {
                                return  ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                            }
                            probeSeq.move_next();
                        }
                    }
                }
            }
            else
            {
                fixed (byte* ptr = &controls[0])
                {
                    while (true)
                    {
                        var group = Avx2Group.load(ptr + probeSeq.pos);
                        var bitmask = group.match_byte(h2_hash);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.any_bit_set())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.lowest_set_bit_nonzero();
                            bitmask = bitmask.remove_lowest_bit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                            {
                                return ref entry;
                            }
                        }
                        if (group.match_empty().any_bit_set())
                        {
                            return ref Unsafe.NullRef<MyDictionary<TKey, TValue>.Entry>();
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        public static int DispatchFindBucketIndexOfDictionary<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForAvx2(dictionary, key);
            }
            else
           if (Sse2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForSse2(dictionary, key);
            }
            else
            {
                return FindBucketIndexOfDictionaryForFallback(dictionary, key);
            }
        }

        private static unsafe int FindBucketIndexOfDictionaryForAvx2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.match_byte(h2_hash);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.any_bit_set())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.lowest_set_bit_nonzero();
                                bitmask = bitmask.remove_lowest_bit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.match_empty().any_bit_set())
                            {
                                return -1;
                            }
                            probeSeq.move_next();
                        }
                    }
                }
                else
                {
                    EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.match_byte(h2_hash);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.any_bit_set())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.lowest_set_bit_nonzero();
                                bitmask = bitmask.remove_lowest_bit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.match_empty().any_bit_set())
                            {
                                return -1;
                            }
                            probeSeq.move_next();
                        }
                    }
                }
            }
            else
            {
                fixed (byte* ptr = &controls[0])
                {
                    while (true)
                    {
                        var group = Avx2Group.load(ptr + probeSeq.pos);
                        var bitmask = group.match_byte(h2_hash);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.any_bit_set())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.lowest_set_bit_nonzero();
                            bitmask = bitmask.remove_lowest_bit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return index;
                            }
                        }
                        if (group.match_empty().any_bit_set())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }

        }

        private static unsafe int FindBucketIndexOfDictionaryForSse2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;
            var equalComparer = hashComparer ?? EqualityComparer<TKey>.Default;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            fixed (byte* ptr = &controls[0])
            {
                while (true)
                {
                    var group = Sse2Group.load(ptr + probeSeq.pos);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        Debug.Assert(entries != null);
                        var bit = bitmask.lowest_set_bit_nonzero();
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probeSeq.pos + bit) & bucketMask;
                        ref var entry = ref entries[index];
                        if (equalComparer.Equals(key, entry.Key))
                        {
                            return index;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return -1;
                    }
                    probeSeq.move_next();
                }
            }
        }

        private static unsafe int FindBucketIndexOfDictionaryForFallback<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;
            var equalComparer = hashComparer ?? EqualityComparer<TKey>.Default;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);


            fixed (byte* ptr = &controls[0])
            {
                while (true)
                {
                    var group = FallbackGroup.load(ptr + probeSeq.pos);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        Debug.Assert(entries != null);
                        var bit = bitmask.lowest_set_bit_nonzero();
                        bitmask = bitmask.remove_lowest_bit();
                        var index = (probeSeq.pos + bit) & bucketMask;
                        ref var entry = ref entries[index];
                        if (equalComparer.Equals(key, entry.Key))
                        {
                            return index;
                        }
                    }
                    if (group.match_empty().any_bit_set())
                    {
                        return -1;
                    }
                    probeSeq.move_next();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DispatchCopyToArrayFromDictionaryWorker<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                CopyToArrayFromDictionaryWorkerForAvx2(dictionary, destArray, index);
            }
            else
           if (Sse2.IsSupported)
            {
                CopyToArrayFromDictionaryWorkerForSse2(dictionary, destArray, index);
            }
            else
            {
                CopyToArrayFromDictionaryWorkerForFallback(dictionary, destArray, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForAvx2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Avx2Group.load(ptr).match_full();
                while (true)
                {
                    var lowestSetBit = bitMask.lowest_set_bit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.remove_lowest_bit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Avx2Group.load(ptr + offset).match_full();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForSse2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Sse2Group.load(ptr).match_full();
                while (true)
                {
                    var lowestSetBit = bitMask.lowest_set_bit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.remove_lowest_bit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Sse2Group.load(ptr + offset).match_full();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForFallback<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = FallbackGroup.load(ptr).match_full();
                while (true)
                {
                    var lowestSetBit = bitMask.lowest_set_bit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.remove_lowest_bit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = FallbackGroup.load(ptr + offset).match_full();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DispatchFindInsertSlot(int hash, byte[] contorls, int bucketMask)
        {
            if (Avx2.IsSupported)
            {
                return FindInsertSlotForAvx2(hash, contorls, bucketMask);
            }
            else
           if (Sse2.IsSupported)
            {
                return FindInsertSlotForSse2(hash, contorls, bucketMask);
            }
            else
            {
                return FindInsertSlotForFallback(hash, contorls, bucketMask);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForAvx2(int hash, byte[] contorls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(contorls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &contorls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = Avx2Group.load(ptr + probeSeq.pos)
                        .match_empty_or_deleted()
                        .lowest_set_bit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return Avx2Group.load(ptr)
                            .match_empty_or_deleted()
                            .lowest_set_bit_nonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForSse2(int hash, byte[] contorls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(contorls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &contorls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = Sse2Group.load(ptr + probeSeq.pos)
                        .match_empty_or_deleted()
                        .lowest_set_bit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return Sse2Group.load(ptr)
                            .match_empty_or_deleted()
                            .lowest_set_bit_nonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForFallback(int hash, byte[] contorls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(contorls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &contorls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = FallbackGroup.load(ptr + probeSeq.pos)
                        .match_empty_or_deleted()
                        .lowest_set_bit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return FallbackGroup.load(ptr)
                            .match_empty_or_deleted()
                            .lowest_set_bit_nonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }
    }
}
