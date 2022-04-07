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
        public Avx2BitMask invert()
        {
            return new Avx2BitMask((this._data ^ BITMASK_MASK));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool any_bit_set()
        {
            return this._data != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int leading_zeros()
        {
            return BitOperations.LeadingZeroCount(this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int lowest_set_bit()
        {
            if (this._data == 0)
            {
                return -1;
            }
            else
            {
                return this.lowest_set_bit_nonzero();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int lowest_set_bit_nonzero()
        {
            return this.trailing_zeros();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask remove_lowest_bit()
        {
            return new Avx2BitMask(this._data & (this._data - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int trailing_zeros()
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
        [Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2207:Initialize value type static fields inline", Justification = "The doc says not to suppress this, but how to fix?")]
        static Avx2Group()
        {
            WIDTH = 256 / 8;
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            static_empty = res;
        }

        // 128 bits(_data length) / 8 (byte bits) = 16 bytes
        public static readonly int WIDTH;

        private readonly Vector256<byte> _data;

        internal Avx2Group(Vector256<byte> data)
        {
            _data = data;
        }

        public static readonly byte[] static_empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group load(byte* ptr)
        {
            return new Avx2Group(Avx2.LoadVector256(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group load_aligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new Avx2Group(Avx2.LoadAlignedVector256(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void store_aligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Avx2.StoreAligned(ptr, this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask match_byte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var compareValue = Vector256.Create(b);
            var cmp = Avx2.CompareEqual(this._data, compareValue);
            return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask match_empty()
        {
            return this.match_byte(SwissTableHelper.EMPTY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask match_empty_or_deleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Avx2BitMask((uint)Avx2.MoveMask(this._data));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask match_full()
        {
            return this.match_empty_or_deleted().invert();
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
    }
}
