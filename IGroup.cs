using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public interface IGroupInfo
    {
        // only a reminder, the children should always hide this filed by using "new"
        public int WIDTH { get; }
    }

    public interface IGroup
    {
        /// Returns a full group of empty bytes, suitable for use as the initial
        /// value for an empty hash table.
        ///
        /// This is guaranteed to be aligned to the group size.
        public abstract byte[] static_empty();

        public unsafe IGroup load(byte* ptr);

        /// Loads a group of bytes starting at the given address, which must be
        /// aligned to `mem::align_of::<Group>()`.
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public unsafe IGroup load_aligned(byte* ptr);


        /// Stores the group of bytes to the given address, which must be
        /// aligned to `mem::align_of::<Group>()`
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public unsafe void store_aligned(byte* ptr);

        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        // #[inline]
        public IBitMask match_byte(byte b);

        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        // #[inline]
        public IBitMask match_empty();

        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        // #[inline]
        public IBitMask match_empty_or_deleted();


        /// Returns a `BitMask` indicating all bytes in the group which are full.
        // #[inline]
        public IBitMask match_full();

        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        // #[inline]
        public IGroup convert_special_to_empty_and_full_to_deleted();
    }
}