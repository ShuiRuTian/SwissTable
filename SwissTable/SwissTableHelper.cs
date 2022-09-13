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

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ProbeSeq(int hash, int bucket_mask)
        {
            this._bucket_mask = bucket_mask;
            this.pos = SwissTableHelper.h1(hash) & bucket_mask;
            this._stride = 0;
        }

        [SkipLocalsInit]
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
        public static readonly int GROUP_WIDTH = InitialGroupWidth();

        public static int InitialGroupWidth()
        {
            // if (Avx2.IsSupported)
            // {
            //     return Avx2Group.WIDTH;
            // }
            // else
            if (Sse2.IsSupported)
            {
                return Sse2Group.WIDTH;
            }
            else
            {
                return FallbackGroup.WIDTH;
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

        /// Checks whether a special control value is EMPTY.
        // optimise: return 1 as true, 0 as false
        public static int special_is_empty_with_int_return(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return ctrl & 0x01;
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
            // if (Avx2.IsSupported)
            // {
            //     return Avx2Group.StaticEmpty;
            // }
            // else
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
            // if (Avx2.IsSupported)
            // {
            //     return GetMatchFullBitMaskForAvx2(controls, index);
            // }
            // else
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
                result.avx2BitMask = Avx2Group.Load(ctrl).MatchFull();
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
                result.sse2BitMask = Sse2Group.load(ctrl).MatchFull();
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
                result.fallbackBitMask = FallbackGroup.load(ctrl).MatchFull();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref Dictionary<TKey, TValue>.Entry DispatchMoveNextDictionary<TKey, TValue>(
            int version,
            int tolerantVersion,
            in Dictionary<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            // if (Avx2.IsSupported)
            // {
            //     return ref MoveNextDictionaryForAvx2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            // }
            // else
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
        public static ref Dictionary<TKey, TValue>.Entry MoveNextDictionaryForAvx2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in Dictionary<TKey, TValue> dictionary,
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
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }
                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForAvx2(controls, currentCtrlOffset).avx2BitMask;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref Dictionary<TKey, TValue>.Entry MoveNextDictionaryForSse2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in Dictionary<TKey, TValue> dictionary,
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
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForSse2(controls, currentCtrlOffset).sse2BitMask;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref Dictionary<TKey, TValue>.Entry MoveNextDictionaryForFallback<TKey, TValue>(
            int version,
            int tolerantVersion,
            in Dictionary<TKey, TValue> dictionary,
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
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
            // if (Avx2.IsSupported)
            // {
            //     return IsEraseSafeToSetEmptyControlFlagForAvx2(bucketMask, controls, index);
            // }
            // else
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
                var empty_before = Avx2Group.Load(ptr_before).MatchEmpty();
                var empty_after = Avx2Group.Load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
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
                var empty_before = Sse2Group.load(ptr_before).MatchEmpty();
                var empty_after = Sse2Group.load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
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
                var empty_before = FallbackGroup.load(ptr_before).MatchEmpty();
                var empty_after = FallbackGroup.load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref Dictionary<TKey, TValue>.Entry DispatchFindBucketOfDictionary<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, int hashOfKey)
        where TKey : notnull
        {
            // if (Avx2.IsSupported)
            // {
            //     return ref FindBucketOfDictionaryForAvx2(dictionary, key, hashOfKey);
            // }
            // else
            if (Sse2.IsSupported)
            {
                return ref FindBucketOfDictionaryForSse2(dictionary, key, hashOfKey);
            }
            else
            {
                return ref FindBucketOfDictionaryForFallback(dictionary, key, hashOfKey);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref Dictionary<TKey, TValue>.Entry FindBucketOfDictionaryForAvx2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var h2_hash = h2(hash);
            var targetGroup = Avx2Group.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                            var group = Avx2Group.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                        var group = Avx2Group.Load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return ref entry;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref Dictionary<TKey, TValue>.Entry FindBucketOfDictionaryForSse2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var h2_hash = h2(hash);
            var targetGroup = Sse2Group.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Sse2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                            var group = Sse2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                        var group = Sse2Group.load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return ref entry;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref Dictionary<TKey, TValue>.Entry FindBucketOfDictionaryForFallback<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var h2_hash = h2(hash);
            var targetGroup = FallbackGroup.create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = FallbackGroup.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                            var group = FallbackGroup.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return ref entry;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
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
                        var group = FallbackGroup.load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return ref entry;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return ref Unsafe.NullRef<Dictionary<TKey, TValue>.Entry>();
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        /// <summary>
        /// Find the index of given key, negative means not found.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <returns>
        /// negative return value means not found
        /// </returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DispatchFindBucketIndexOfDictionary<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
    where TKey : notnull
        {
            // if (Avx2.IsSupported)
            // {
            //     return FindBucketIndexOfDictionaryForAvx2(dictionary, key);
            // }
            // else
            if (Sse2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForSse2(dictionary, key);
            }
            else
            {
                return FindBucketIndexOfDictionaryForFallback(dictionary, key);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForAvx2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var targetGroup = Avx2Group.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Avx2Group.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                            var group = Avx2Group.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                        var group = Avx2Group.Load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForSse2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var targetGroup = Sse2Group.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = Sse2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                            var group = Sse2Group.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                        var group = Sse2Group.load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForFallback<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var targetGroup = FallbackGroup.create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = FallbackGroup.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                            var group = FallbackGroup.load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            // TODO: Iterator and performance, if not influence, iterator would be clearer.
                            while (bitmask.AnyBitSet())
                            {
                                // there must be set bit
                                Debug.Assert(entries != null);
                                var bit = bitmask.LowestSetBitNonzero();
                                bitmask = bitmask.RemoveLowestBit();
                                var index = (probeSeq.pos + bit) & bucketMask;
                                ref var entry = ref entries[index];
                                if (defaultComparer.Equals(key, entry.Key))
                                {
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
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
                        var group = FallbackGroup.load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        // TODO: Iterator and performance, if not influence, iterator would be clearer.
                        while (bitmask.AnyBitSet())
                        {
                            // there must be set bit
                            Debug.Assert(entries != null);
                            var bit = bitmask.LowestSetBitNonzero();
                            bitmask = bitmask.RemoveLowestBit();
                            var index = (probeSeq.pos + bit) & bucketMask;
                            ref var entry = ref entries[index];
                            if (hashComparer.Equals(key, entry.Key))
                            {
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DispatchCopyToArrayFromDictionaryWorker<TKey, TValue>(Dictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            // if (Avx2.IsSupported)
            // {
            //     CopyToArrayFromDictionaryWorkerForAvx2(dictionary, destArray, index);
            // }
            // else
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
        private static unsafe void CopyToArrayFromDictionaryWorkerForAvx2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Avx2Group.Load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Avx2Group.Load(ptr + offset).MatchFull();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForSse2<TKey, TValue>(Dictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Sse2Group.load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Sse2Group.load(ptr + offset).MatchFull();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForFallback<TKey, TValue>(Dictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = FallbackGroup.load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = FallbackGroup.load(ptr + offset).MatchFull();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DispatchFindInsertSlot(int hash, byte[] contorls, int bucketMask)
        {
            // if (Avx2.IsSupported)
            // {
            //     return FindInsertSlotForAvx2(hash, contorls, bucketMask);
            // }
            // else
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
                    var bit = Avx2Group.Load(ptr + probeSeq.pos)
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
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
                        return Avx2Group.Load(ptr)
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
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
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
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
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
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
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
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
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }
    }
}
