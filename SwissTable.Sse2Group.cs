using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        struct Sse2BitMask : IBitMask
        {
            // 128 / 8 = 16, so choose ushort
            // Or maybe we could use `int` with only lowset 16 bits and some trick?
            ushort _data;

            internal Sse2BitMask(ushort data)
            {
                _data = data;
            }

            /// Returns a new `BitMask` with all bits inverted.
            public IBitMask invert()
            {
                return new Sse2BitMask((ushort)(this._data ^ Sse2TriviaInfo.BITMASK_MASK));
            }

            public bool any_bit_set()
            {
                return this._data != 0;
            }

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

            public int leading_zeros()
            {
                Debug.Assert(this._data is ushort);
                // Notice this maigc number `16`, it is calcualted by the length of type of `LeadingZeroCount`(uint, so 32) and Groud::width(16 for this struct)
                // So it is 32 - 16 = 16
                return Numerics.BitOperations.LeadingZeroCount(this._data) - 16;
            }

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

            public int lowest_set_bit_nonzero()
            {
                return this.trailing_zeros();
            }

            public IBitMask remove_lowest_bit()
            {
                return new Sse2BitMask((ushort)(this._data & (this._data - 1)));
            }

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
        class Sse2TriviaInfo : ITriviaInfo
        {
            // mem::size_of::<Self>() in rust.
            public int WIDTH => 128 / 8;

            // mem::align_of::<Self>() in rust.
            public const uint ALIGN_WIDTH = 128 / 8;
            public const uint BITMASK_STRIDE = 1;
            public const ushort BITMASK_MASK = 0xffff;
        }

        // TODO: for 64-bits target, we could use ulong rather than uint to improve performance.
        struct Sse2Group : IGroup
        {
            Vector128<byte> _data;

            internal Sse2Group(Vector128<byte> data)
            {
                _data = data;
            }

            public byte[] static_empty()
            {
                return new byte[_groupInfo.WIDTH];
            }

            public unsafe IGroup load(byte* ptr)
            {
                return new Sse2Group(Sse2.LoadVector128(ptr));
            }

            public unsafe IGroup load_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (Sse2TriviaInfo.ALIGN_WIDTH - 1)) == 0);
                return new Sse2Group(Sse2.LoadAlignedVector128(ptr));
            }

            public unsafe void store_aligned(byte* ptr)
            {
                // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
                Debug.Assert(((uint)ptr & (Sse2TriviaInfo.ALIGN_WIDTH - 1)) == 0);
                Sse2.StoreAligned(ptr, this._data);
            }

            public IBitMask match_byte(byte b)
            {
                // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
                var compareValue = Vector128.Create(b);
                var cmp = Sse2.CompareEqual(this._data, compareValue);
                return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));

                // let cmp = x86::_mm_cmpeq_epi8(self.0, x86::_mm_set1_epi8(byte as i8));
                // BitMask(x86::_mm_movemask_epi8(cmp) as u16)
            }

            public IBitMask match_empty()
            {
                return this.match_byte(EMPTY);
            }

            public IBitMask match_empty_or_deleted()
            {
                // A byte is EMPTY or DELETED iff the high bit is set
                return new Sse2BitMask((ushort)Sse2.MoveMask(this._data));
            }

            public IBitMask match_full()
            {
                return this.match_empty_or_deleted().invert();
            }

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