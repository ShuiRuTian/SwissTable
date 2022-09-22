// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    internal struct FallbackBitMask : IBitMask<FallbackBitMask>
    {
        // Why use nuint/nint?
        // For 64 bit platform, we could compare 8 buckets at one time,
        // For 32 bit platform, we could compare 4 buckets at one time.
        // And it might be faster to access data for it is aligned, but not sure.
        private readonly nuint _data;

        private static nuint BITMASK_MASK => unchecked((nuint)0x8080_8080_8080_8080);

        private const int BITMASK_SHIFT = 3;

        internal FallbackBitMask(nuint data)
        {
            _data = data;
        }

        /// Returns a new `BitMask` with all bits inverted.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask Invert()
        {
            return new FallbackBitMask(this._data ^ BITMASK_MASK);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyBitSet()
        {
            return this._data != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LeadingZeros()
        {
#if TARGET_64BIT
            return BitOperations.LeadingZeroCount(this._data) >> BITMASK_SHIFT;
#else
            // maigc number `32`
            // type of `this._data` is `nunit`
            // however, it will be tranfrom to `ulong` implicitly
            // So it is 64 - 32 = 32
            return (BitOperations.LeadingZeroCount(this._data) - 32) >> BITMASK_SHIFT;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LowestSetBit()
        {
            if (this._data == 0)
            {
                return -1;
            }
            else
            {
                return this.LowestSetBitNonzero();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LowestSetBitNonzero()
        {
            return this.TrailingZeros();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask RemoveLowestBit()
        {
            return new FallbackBitMask(this._data & (this._data - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrailingZeros()
        {
            return BitOperations.TrailingZeroCount(this._data) >> BITMASK_SHIFT;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask And(FallbackBitMask bitMask)
        {
            return new FallbackBitMask(this._data & bitMask._data);
        }
    }

    internal struct FallbackGroup : IGroup<FallbackBitMask, FallbackGroup>
    {
        public static unsafe int WIDTH => sizeof(nuint);

        public static readonly byte[] static_empty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FallbackGroup load(byte* ptr)
        {
            return new FallbackGroup(Unsafe.ReadUnaligned<nuint>(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FallbackGroup load_aligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new FallbackGroup(Unsafe.Read<nuint>(ptr));
        }

        private static nuint repeat(byte b)
        {
            nuint res = 0;
            for (int i = 0; i < WIDTH; i++)
            {
                res <<= 8;
                res &= b;
            }
            return res;
        }

        private readonly nuint _data;

        internal FallbackGroup(nuint data)
        {
            _data = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackGroup convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let full = 1000_0000 (true) or 0000_0000 (false)
            //   !1000_0000 + 1 = 0111_1111 + 1 = 1000_0000 (no carry)
            //   !0000_0000 + 0 = 1111_1111 + 0 = 1111_1111 (no carry)
            nuint full = ~this._data & unchecked((nuint)0x8080_8080_8080_8080);
            var q = (full >> 7);
            var w = ~full + q;
            return new FallbackGroup(w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Unsafe.Write(ptr, this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchByte(byte b)
        {
            // This algorithm is derived from
            // https://graphics.stanford.edu/~seander/bithacks.html##ValueInWord
            var cmp = this._data ^ repeat(b);
            var res = unchecked((cmp - (nuint)0x0101_0101_0101_0101) & ~cmp & (nuint)0x8080_8080_8080_8080);
            return new FallbackBitMask(res);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchEmpty()
        {
            // If the high bit is set, then the byte must be either:
            // 1111_1111 (EMPTY) or 1000_0000 (DELETED).
            // So we can just check if the top two bits are 1 by ANDing them.
            return new FallbackBitMask(this._data & this._data << 1 & unchecked((nuint)0x8080_8080_8080_8080));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new FallbackBitMask(this._data & unchecked((nuint)0x8080_8080_8080_8080));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        public static FallbackGroup create(byte b)
        {
            throw new NotImplementedException();
        }

        public FallbackBitMask MatchGroup(FallbackGroup group)
        {
            throw new NotImplementedException();
        }
    }
}
