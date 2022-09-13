// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    internal struct Sse2BitMask : IBitMask<Sse2BitMask>
    {
        private const ushort BITMASK_MASK = 0xffff;

        // 128 / 8 = 16, so choose ushort
        internal readonly ushort _data;

        internal Sse2BitMask(ushort data)
        {
            _data = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask Invert()
        {
            return new Sse2BitMask((ushort)(this._data ^ BITMASK_MASK));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyBitSet()
        {
            return this._data != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LeadingZeros()
        {
            // maigc number `16`
            // type of `this._data` is `short`
            // however, it will be tranfrom to `uint` implicitly
            // Delete the additional length
            return BitOperations.LeadingZeroCount(this._data) - 16;
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
        public Sse2BitMask RemoveLowestBit()
        {
            return new Sse2BitMask((ushort)(this._data & (this._data - 1)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrailingZeros()
        {
            return BitOperations.TrailingZeroCount(this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask And(Sse2BitMask bitMask)
        {
            return new Sse2BitMask((ushort)(this._data & bitMask._data));
        }
    }

    // TODO: suppress default initialization.
    internal struct Sse2Group : IGroup<Sse2BitMask, Sse2Group>
    {
        // 128 bits(_data length) / 8 (byte bits) = 16 bytes
        public static int WIDTH => 128 / 8;

        private readonly Vector128<byte> _data;

        internal Sse2Group(Vector128<byte> data)
        {
            _data = data;
        }

        public static readonly byte[] static_empty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Sse2Group load(byte* ptr)
        {
            return new Sse2Group(Sse2.LoadVector128(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Sse2Group load_aligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 16, so checking lowest 4 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new Sse2Group(Sse2.LoadAlignedVector128(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 16, so checking lowest 4 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Sse2.StoreAligned(ptr, this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchByte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var compareValue = Vector128.Create(b);
            var cmp = Sse2.CompareEqual(this._data, compareValue);
            return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));
        }

        private static readonly Sse2Group EmptyGroup = Create(SwissTableHelper.EMPTY);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchEmpty()
        {
            return this.MatchGroup(EmptyGroup);
            //return this.MatchByte(SwissTableHelper.EMPTY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Sse2BitMask((ushort)Sse2.MoveMask(this._data));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2Group convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let special = 0 > byte = 1111_1111 (true) or 0000_0000 (false)
            //   1111_1111 | 1000_0000 = 1111_1111
            //   0000_0000 | 1000_0000 = 1000_0000
            // byte: 0x80_u8 as i8
            var zero = Vector128<sbyte>.Zero;
            zero.As<sbyte, byte>();
            // TODO: check whether asXXXX could be removed.
            var special = Sse2.CompareGreaterThan(zero, this._data.AsSByte()).AsByte();
            return new Sse2Group(Sse2.Or(special, Vector128.Create((byte)0x80)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Sse2Group Create(byte b)
        {
            return new Sse2Group(Vector128.Create(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchGroup(Sse2Group group)
        {
            var cmp = Sse2.CompareEqual(this._data, group._data);
            return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));
        }
    }
}
