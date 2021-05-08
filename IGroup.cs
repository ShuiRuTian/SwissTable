using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public abstract class IGroupInfo{
        public int WIDTH;
    }

    public abstract class IGroup
    {
        /// Returns a full group of empty bytes, suitable for use as the initial
        /// value for an empty hash table.
        ///
        /// This is guaranteed to be aligned to the group size.
        public abstract byte[] static_empty();

        public abstract unsafe IGroup load(byte* ptr);

        /// Loads a group of bytes starting at the given address, which must be
        /// aligned to `mem::align_of::<Group>()`.
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public abstract unsafe IGroup load_aligned(byte* ptr);


        /// Stores the group of bytes to the given address, which must be
        /// aligned to `mem::align_of::<Group>()`
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public abstract unsafe void store_aligned(byte* ptr);

        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        // #[inline]
        public abstract IBitMask match_byte(byte b);

        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        // #[inline]
        public abstract IBitMask match_empty();

        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        // #[inline]
        public abstract IBitMask match_empty_or_deleted();


        /// Returns a `BitMask` indicating all bytes in the group which are full.
        // #[inline]
        public abstract IBitMask match_full();

        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        // #[inline]
        public abstract IGroup convert_special_to_empty_and_full_to_deleted();
    }
}