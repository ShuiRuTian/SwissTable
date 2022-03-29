using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        struct Sse2BitMask : IBitMask
        {
            private const ushort BITMASK_MASK = 0xffff;

            // 128 / 8 = 16, so choose ushort
            // Or maybe we could use `int` with only lowset 16 bits and some trick?
            ushort _data;

            internal Sse2BitMask(ushort data)
            {
                _data = data;
            }

            /// Returns a new `BitMask` with all bits inverted.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IBitMask invert()
            {
                return new Sse2BitMask((ushort)(this._data ^ BITMASK_MASK));
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

                var mask = 1 << (int)index;
                var tmp = this._data ^ mask;
                this._data = (ushort)tmp;
                // The bit was set if the bit is now 0.
                return (this._data & mask) == 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int leading_zeros()
            {
                Debug.Assert(this._data is ushort);
                // Notice this maigc number `16`, it is calcualted by the length of type of `LeadingZeroCount`(uint, so 32) and Groud::width(16 for this struct)
                // So it is 32 - 16 = 16
                return BitOperations.LeadingZeroCount(this._data) - 16;
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
                return new Sse2BitMask((ushort)(this._data & (this._data - 1)));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int trailing_zeros()
            {
                return BitOperations.TrailingZeroCount(this._data);
            }
        }

        struct Sse2Group : IGroup
        {
            public int WIDTH => 128 / 8;

            private const uint BITMASK_STRIDE = 1;
            Vector128<byte> _data;

            internal Sse2Group(Vector128<byte> data)
            {
                _data = data;
            }

            public byte[] static_empty()
            {
                return Enumerable.Repeat(EMPTY, this.WIDTH).ToArray();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe IGroup load(byte* ptr)
            {
                return new Sse2Group(Sse2.LoadVector128(ptr));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe IGroup load_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
                return new Sse2Group(Sse2.LoadAlignedVector128(ptr));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void store_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
                Sse2.StoreAligned(ptr, this._data);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IBitMask match_byte(byte b)
            {
                // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
                var compareValue = Vector128.Create(b);
                var cmp = Sse2.CompareEqual(this._data, compareValue);
                return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IBitMask match_empty()
            {
                return this.match_byte(EMPTY);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IBitMask match_empty_or_deleted()
            {
                // A byte is EMPTY or DELETED iff the high bit is set
                return new Sse2BitMask((ushort)Sse2.MoveMask(this._data));
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
        }
    }
}