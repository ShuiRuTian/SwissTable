// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    internal struct Avx2BitMask : IBitMask<Avx2BitMask>
    {
        private const uint BITMASK_MASK = 0xffff_ffff;

        // 256 / 8 = 32, so choose uint
        internal readonly uint _data;

        internal Avx2BitMask(uint data)
        {
            _data = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask Invert()
        {
            return new Avx2BitMask((this._data ^ BITMASK_MASK));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyBitSet()
        {
            return this._data != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LeadingZeros()
        {
            return BitOperations.LeadingZeroCount(this._data);
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
        public Avx2BitMask RemoveLowestBit()
        {
            return new Avx2BitMask(this._data & (this._data - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrailingZeros()
        {
            return BitOperations.TrailingZeroCount(this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask And(Avx2BitMask bitMask)
        {
            return new Avx2BitMask((this._data & bitMask._data));
        }
    }

    // TODO: suppress default initialization.
    internal struct Avx2Group : IGroup<Avx2BitMask, Avx2Group>
    {
        // 256 bits(_data length) / 8 (byte bits) = 32 bytes
        public static int WIDTH => 256 / 8;

        private readonly Vector256<byte> _data;

        internal Avx2Group(Vector256<byte> data)
        {
            _data = data;
        }

        public static readonly byte[] StaticEmpty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group Load(byte* ptr)
        {
            return new Avx2Group(Avx2.LoadVector256(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group LoadAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new Avx2Group(Avx2.LoadAlignedVector256(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Avx2.StoreAligned(ptr, this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchByte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var compareValue = Vector256.Create(b);
            var cmp = Avx2.CompareEqual(this._data, compareValue);
            return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
        }

        private static readonly Avx2Group EmptyGroup = Create(SwissTableHelper.EMPTY);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchEmpty()
        {
            return this.MatchGroup(EmptyGroup);
            // return this.match_byte(SwissTableHelper.EMPTY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Avx2BitMask((uint)Avx2.MoveMask(this._data));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2Group convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let special = 0 > byte = 1111_1111 (true) or 0000_0000 (false)
            //   1111_1111 | 1000_0000 = 1111_1111
            //   0000_0000 | 1000_0000 = 1000_0000
            // byte: 0x80_u8 as i8
            var zero = Vector256<sbyte>.Zero;
            zero.As<sbyte, byte>();
            // TODO: check whether asXXXX could be removed.
            var special = Avx2.CompareGreaterThan(zero, this._data.AsSByte()).AsByte();
            return new Avx2Group(Avx2.Or(special, Vector256.Create((byte)0x80)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Avx2Group Create(byte b)
        {
            return new Avx2Group(Vector256.Create(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchGroup(Avx2Group group)
        {
            var cmp = Avx2.CompareEqual(this._data, group._data);
            return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
        }
    }
}
