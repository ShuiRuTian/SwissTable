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

    public static class SwissTableHelper
    {
        public static unsafe int GROUP_WIDTH
        {
            get
            {
                if (Sse2.IsSupported)
                {
                    // 128 bits(_data length) / 8 (byte bits) = 16 bytes
                    return 128 / 8;
                }
                else
                {
                    return sizeof(nuint);
                }
            }
        }

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
        }

        // DISPATHCH METHODS

        // Generally we do not want to duplicate code, but for performance(use struct and inline), we have to do so.
        // The difference between mirror implmentations should only be `_dummyGroup` except `MoveNext`, in which we use C++ union trick
        private static Sse2Group _dummySse2Group;
        private static FallbackGroup _dummyFallbackGroup;

        // For enumerator, which need record the current state
        [StructLayout(LayoutKind.Explicit)]
        public struct BitMaskUnion
        {
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

        public static byte[] DispatchGetEmptyControls()
        {
            if (Sse2.IsSupported)
            {
                return _dummySse2Group.static_empty;
            }
            else
            {
                return _dummyFallbackGroup.static_empty;
            }
        }

        public static BitMaskUnion DispatchGetMatchFullBitMask(byte[] controls, int index)
        {
            if (Sse2.IsSupported)
            {
                return GetMatchFullBitMaskForSse2(controls, index);
            }
            else
            {
                return GetMatchFullBitMaskForFallback(controls, index);
            }
        }

        private static unsafe BitMaskUnion GetMatchFullBitMaskForSse2(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.sse2BitMask = default(Sse2Group).load(ctrl).match_full();
            }
            return result;
        }

        private static unsafe BitMaskUnion GetMatchFullBitMaskForFallback(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.fallbackBitMask = default(FallbackGroup).load(ctrl).match_full();
            }
            return result;
        }

        public static ref MyDictionary<TKey, TValue>.Entry DispatchMoveNextDictionary<TKey, TValue>(
            int version,
            int tolerantVersion,
            in MyDictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            if (Sse2.IsSupported)
            {
                return ref MoveNextDictionaryForSse2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
            else
            {
                return ref MoveNextDictionaryForFallback(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
        }

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
                if (lowest_set_bit.HasValue)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.remove_lowest_bit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit.Value];
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
                if (lowest_set_bit.HasValue)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.remove_lowest_bit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit.Value];
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
        public static bool DispatchIsEraseSafeToSetEmptyControlFlag(byte[] controls, int index)
        {
            if (Sse2.IsSupported)
            {
                return IsEraseSafeToSetEmptyControlFlagForSse2(controls, index);
            }
            else
            {
                return IsEraseSafeToSetEmptyControlFlagForFallback(controls, index);
            }
        }

        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForSse2(byte[] controls, int index)
        {
            var bucketMask = GetBucketMaskFromControlsLength(controls.Length);
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = default(Sse2Group).load(ptr_before).match_empty();
                var empty_after = default(Sse2Group).load(ptr).match_empty();
                return empty_before.leading_zeros() + empty_after.trailing_zeros() < GROUP_WIDTH;
            }
        }

        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForFallback(byte[] controls, int index)
        {
            var bucketMask = GetBucketMaskFromControlsLength(controls.Length);
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = default(FallbackGroup).load(ptr_before).match_empty();
                var empty_after = default(FallbackGroup).load(ptr).match_empty();
                return empty_before.leading_zeros() + empty_after.trailing_zeros() < GROUP_WIDTH;
            }
        }

        public static int? DispatchFindBucketIndexOfDictionary<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            if (Sse2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForSse2(dictionary, key);
            }
            else
            {
                return FindBucketIndexOfDictionaryForFallback(dictionary, key);
            }
        }

        // TODO: use negative as not find
        private static unsafe int? FindBucketIndexOfDictionaryForSse2<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

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
                    var group = default(Sse2Group).load(ptr + probeSeq.pos);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        Debug.Assert(entries != null);
                        var bit = bitmask.lowest_set_bit()!.Value;
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
                        return null;
                    }
                    probeSeq.move_next();
                }
            }
        }

        private static unsafe int? FindBucketIndexOfDictionaryForFallback<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

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
                    var group = default(FallbackGroup).load(ptr + probeSeq.pos);
                    var bitmask = group.match_byte(h2_hash);
                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                    while (bitmask.any_bit_set())
                    {
                        // there must be set bit
                        Debug.Assert(entries != null);
                        var bit = bitmask.lowest_set_bit()!.Value;
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
                        return null;
                    }
                    probeSeq.move_next();
                }
            }
        }

        public static void DispatchCopyToArrayFromDictionaryWorker<TKey, TValue>(MyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            if (Sse2.IsSupported)
            {
                CopyToArrayFromDictionaryWorkerForSse2(dictionary, destArray, index);
            }
            else
            {
                CopyToArrayFromDictionaryWorkerForFallback(dictionary, destArray, index);
            }
        }

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
                var bitMask = default(Sse2Group).load(ptr).match_full();
                while (true)
                {
                    var lowestSetBit = bitMask.lowest_set_bit();
                    if (lowestSetBit.HasValue)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.remove_lowest_bit();
                        ref var entry = ref entries[offset + lowestSetBit.Value];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = default(Sse2Group).load(ptr + offset).match_full();
                }
            }
        }

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
                var bitMask = default(FallbackGroup).load(ptr).match_full();
                while (true)
                {
                    var lowestSetBit = bitMask.lowest_set_bit();
                    if (lowestSetBit.HasValue)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.remove_lowest_bit();
                        ref var entry = ref entries[offset + lowestSetBit.Value];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = default(FallbackGroup).load(ptr + offset).match_full();
                }
            }
        }

        public static int DispatchFindInsertSlot(int hash, byte[] contorls)
        {
            if (Sse2.IsSupported)
            {
                return FindInsertSlotForSse2(hash, contorls);
            }
            else
            {
                return FindInsertSlotForFallback(hash, contorls);
            }
        }

        private static unsafe int FindInsertSlotForSse2(int hash, byte[] contorls)
        {
            var bucketMask = GetBucketMaskFromControlsLength(contorls.Length);
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &contorls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = _dummySse2Group.load(ptr + probeSeq.pos)
                        .match_empty_or_deleted()
                        .lowest_set_bit();
                    if (bit.HasValue)
                    {
                        var result = (probeSeq.pos + bit.Value) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (is_full(contorls[result]))
                        {
                            Debug.Assert(bucketMask < GROUP_WIDTH);
                            Debug.Assert(probeSeq.pos != 0);
                            return default(Sse2Group).load(ptr)
                                .match_empty_or_deleted()
                                .lowest_set_bit_nonzero();
                        }
                        return result;
                    }
                    probeSeq.move_next();
                }
            }
        }

        private static unsafe int FindInsertSlotForFallback(int hash, byte[] contorls)
        {
            var bucketMask = GetBucketMaskFromControlsLength(contorls.Length);
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &contorls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = default(FallbackGroup).load(ptr + probeSeq.pos)
                        .match_empty_or_deleted()
                        .lowest_set_bit();
                    if (bit.HasValue)
                    {
                        var result = (probeSeq.pos + bit.Value) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (is_full(contorls[result]))
                        {
                            Debug.Assert(bucketMask < GROUP_WIDTH);
                            Debug.Assert(probeSeq.pos != 0);
                            return default(FallbackGroup).load(ptr)
                                .match_empty_or_deleted()
                                .lowest_set_bit_nonzero();
                        }
                        return result;
                    }
                    probeSeq.move_next();
                }
            }
        }
    }
}
