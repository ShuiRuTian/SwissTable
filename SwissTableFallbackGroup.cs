using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    struct FallbackBitMask : IBitMask
    {
        // 128 / 8 = 16, so choose ushort
        // Or maybe we could use `int` with only lowset 16 bits and some trick?
        nuint _data;
        private readonly nuint BITMASK_MASK => unchecked((nuint)0x8080_8080_8080_8080);

        private const int BITMASK_SHIFT = 3;

        internal FallbackBitMask(nuint data)
        {
            _data = data;
        }

        /// Returns a new `BitMask` with all bits inverted.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBitMask invert()
        {
            return new FallbackBitMask((nuint)(this._data ^ BITMASK_MASK));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool any_bit_set()
        {
            return this._data != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int leading_zeros()
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
        public int? lowest_set_bit()
        {
            if (this._data == 0)
            {
                return null;
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
        public IBitMask remove_lowest_bit()
        {
            return new FallbackBitMask((nuint)(this._data & (this._data - 1)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int trailing_zeros()
        {
            return BitOperations.TrailingZeroCount(this._data) >> BITMASK_SHIFT;
        }
    }

    struct FallbackGroup : IGroup
    {
        public unsafe int WIDTH => sizeof(nuint);

        private nuint repeat(byte b)
        {
            nuint res = 0;
            for (int i = 0; i < this.WIDTH; i++)
            {
                res <<= 8;
                res &= b;
            }
            return res;
        }

        nuint _data;

        internal FallbackGroup(nuint data)
        {
            _data = data;
        }

        public byte[] static_empty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe IGroup load(byte* ptr)
        {
            return new FallbackGroup(Unsafe.ReadUnaligned<nuint>(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe IGroup load_aligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new FallbackGroup(Unsafe.Read<nuint>(ptr));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void store_aligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Unsafe.Write(ptr, this._data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBitMask match_byte(byte b)
        {
            // This algorithm is derived from
            // https://graphics.stanford.edu/~seander/bithacks.html##ValueInWord
            var cmp = this._data ^ this.repeat(b);
            var res = unchecked((cmp - (nuint)0x0101_0101_0101_0101) & ~cmp & (nuint)0x8080_8080_8080_8080);
            return new FallbackBitMask(res);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBitMask match_empty()
        {
            // If the high bit is set, then the byte must be either:
            // 1111_1111 (EMPTY) or 1000_0000 (DELETED).
            // So we can just check if the top two bits are 1 by ANDing them.
            return new FallbackBitMask(this._data & this._data << 1 & unchecked((nuint)0x8080_8080_8080_8080));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBitMask match_empty_or_deleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new FallbackBitMask(this._data & unchecked((nuint)0x8080_8080_8080_8080));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBitMask match_full()
        {
            return this.match_empty_or_deleted().invert();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IGroup convert_special_to_empty_and_full_to_deleted()
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
    }
}