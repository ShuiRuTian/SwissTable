using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        struct FallbackBitMask : IBitMask
        {
            // 128 / 8 = 16, so choose ushort
            // Or maybe we could use `int` with only lowset 16 bits and some trick?
            nuint _data;
            private readonly nuint BITMASK_MASK => unchecked((nuint)0x8080_8080_8080_8080);

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
            public bool flip(uint index)
            {
                // NOTE: The + BITMASK_STRIDE - 1 is to set the high bit.
                // let mask = 1 << (index * BITMASK_STRIDE + BITMASK_STRIDE - 1);
                // But for now, we could calculate mask is just 1 << index

                nuint mask = (nuint)(1 << (int)index);
                var tmp = this._data ^ mask;
                this._data = (ushort)tmp;
                // The bit was set if the bit is now 0.
                return (this._data & mask) == 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int leading_zeros()
            {
                Debug.Assert(this._data is nuint);
                // Notice this maigc number `16`, it is calcualted by the length of type of `LeadingZeroCount`(uint, so 32) and Groud::width(16 for this struct)
                // So it is 32 - 16 = 16
                return Numerics.BitOperations.LeadingZeroCount(this._data) - 16;
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
                return Numerics.BitOperations.TrailingZeroCount(this._data);
            }
        }

        /// <summary>
        /// All property should be fixed as long as the hardware is specific.
        /// We could use getter for all getter only properties like `WIDTH`.
        /// However, we use a differnt design: As long as the properties are only used in the 
        /// correspond hardware design, we only use `const`
        /// </summary>
        class FallbackTriviaInfo : ITriviaInfo
        {
            public unsafe int WIDTH => sizeof(nuint);
        }

        // TODO: for 64-bits target, we could use ulong rather than uint to improve performance.
        struct FallbackGroup : IGroup
        {
            private const uint ALIGN_WIDTH = 128 / 8;
            private const uint BITMASK_STRIDE = 1;

            private nuint repeat(byte b)
            {
                nuint res = 0;
                for (int i = 0; i < _groupInfo.WIDTH; i++)
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
                return Enumerable.Repeat(EMPTY, _groupInfo.WIDTH).ToArray();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe IGroup load(byte* ptr)
            {
                return new FallbackGroup(Unsafe.ReadUnaligned<uint>(ptr));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe IGroup load_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (ALIGN_WIDTH - 1)) == 0);
                return new FallbackGroup(Unsafe.Read<uint>(ptr));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void store_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (ALIGN_WIDTH - 1)) == 0);
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
}