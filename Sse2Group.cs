using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    class Sse2BitMask : IBitMask
    {
        // 128 / 8 = 16, so choose ushort
        ushort _data;

        internal Sse2BitMask(ushort data)
        {
            _data = data;
        }

        public override bool any_bit_set()
        {
            return this._data != 0;
        }

        public override bool flip(uint index)
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

        public override IBitMask invert()
        {
            return new Sse2BitMask((ushort)(this._data ^ Sse2GroupInfo.BITMASK_MASK));
        }

        public override int leading_zeros()
        {
            return MostSignificantBit(this._data);
        }

        public override int? lowest_set_bit()
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

        public override int lowest_set_bit_nonzero()
        {
            return this.trailing_zeros();
        }

        public override IBitMask remove_lowest_bit()
        {
            return new Sse2BitMask((ushort)(this._data & (this._data - 1)));
        }

        public override int trailing_zeros()
        {
            return LeastSignificantBit(this._data);
        }

        #region Helper
        private static int LeastSignificantBit(ushort Arg)
        {
            int RetVal = 15;
            if ((Arg & 0x00ff) != 0) { RetVal -= 8; Arg &= 0x00ff; }
            if ((Arg & 0x0f0f) != 0) { RetVal -= 4; Arg &= 0x0f0f; }
            if ((Arg & 0x3333) != 0) { RetVal -= 2; Arg &= 0x3333; }
            if ((Arg & 0x5555) != 0) RetVal -= 1;
            return RetVal;
        }
        private static int LeastSignificantBit2(ushort Arg)
        {
            var tmp = (short)Arg;
            var num = tmp & ~(-tmp);
            num |= num << 1;
            num |= num << 2;
            num |= num << 4;
            num |= num << 8;
            num |= num << 16;
            return ~num;
        }

        private static int MostSignificantBit(ushort Arg)
        {
            int RetVal = 0;
            if ((Arg & 0xff00) != 0) { RetVal += 8; Arg &= 0xff00; }
            if ((Arg & 0xf0f0) != 0) { RetVal += 4; Arg &= 0xf0f0; }
            if ((Arg & 0xcccc) != 0) { RetVal += 2; Arg &= 0xcccc; }
            if ((Arg & 0xaaaa) != 0) RetVal += 1;
            return RetVal;
        }

        #endregion
    }

    class Sse2GroupInfo : IGroupInfo
    {
        // mem::size_of::<Self>() in rust.
        public const uint WIDTH = 128 / 8;

        // mem::align_of::<Self>() in rust.
        public const uint ALIGN_WIDTH = 128 / 8;
        public const uint BITMASK_STRIDE = 1;
        public const ushort BITMASK_MASK = 0xffff;

    }

    // TODO: for 64-bits target, we could use ulong rather than uint to improve performance.
    class Sse2Group : IGroup
    {
        Vector128<byte> _data;

        // the only purpose is to choose the implement.
        internal Sse2Group()
        { }


        internal Sse2Group(Vector128<byte> data)
        {
            _data = data;
        }

        public override byte[] static_empty()
        {
            return new byte[Sse2GroupInfo.WIDTH];
        }

        public override unsafe IGroup load(byte* ptr)
        {
            return new Sse2Group(Sse2.LoadVector128(ptr));
        }

        public override unsafe IGroup load_aligned(byte* ptr)
        {
            // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (Sse2GroupInfo.ALIGN_WIDTH - 1)) == 0);
            return new Sse2Group(Sse2.LoadAlignedVector128(ptr));
        }

        public override unsafe void store_aligned(byte* ptr)
        {
            // uint casting is OK, for ALIGN_WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (Sse2GroupInfo.ALIGN_WIDTH - 1)) == 0);
            Sse2.StoreAligned(ptr, this._data);
        }

        public override IBitMask match_byte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var tmp = Vector128.Create(b);
            var cmp = Sse2.CompareEqual(this._data, tmp);
            return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));

            // let cmp = x86::_mm_cmpeq_epi8(self.0, x86::_mm_set1_epi8(byte as i8));
            // BitMask(x86::_mm_movemask_epi8(cmp) as u16)
        }

        public override IBitMask match_empty()
        {
            return this.match_byte(SwissTableHelper.EMPTY);
        }

        public override IBitMask match_empty_or_deleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Sse2BitMask((ushort)Sse2.MoveMask(this._data));
        }

        public override IBitMask match_full()
        {
            return this.match_empty_or_deleted().invert();
        }

        public override IGroup convert_special_to_empty_and_full_to_deleted()
        {
            var zero = Vector128<sbyte>.Zero;
            // TODO: check whether asXXXX could be removed.
            var special = Sse2.CompareGreaterThan(zero, this._data.AsSByte()).AsByte();
            return new Sse2Group(Sse2.Or(special, Vector128.Create((byte)0x80)));
        }
    }
}