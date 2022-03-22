using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public interface ITriviaInfo
    {
        public int WIDTH { get; }
    }

    public interface IGroup
    {
        /// <summary>
        /// Returns a full group of empty bytes, suitable for use as the initial
        /// value for an empty hash table.
        ///
        /// This is guaranteed to be aligned to the group size.
        /// </summary>
        /// <returns></returns>
        public abstract byte[] static_empty();

        public unsafe IGroup load(byte* ptr);

        /// <summary>
        /// Loads a group of bytes starting at the given address, which must be
        /// aligned to `mem::align_of::<Group>()`.
        /// </summary>
        /// <param name="ptr"></param>
        /// <returns></returns>
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public unsafe IGroup load_aligned(byte* ptr);


        /// <summary>
        /// Stores the group of bytes to the given address, which must be
        /// aligned to `mem::align_of::<Group>()`
        /// </summary>
        /// <param name="ptr"></param>
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        public unsafe void store_aligned(byte* ptr);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        // #[inline]
        public IBitMask match_byte(byte b);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public IBitMask match_empty();

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public IBitMask match_empty_or_deleted();


        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are full.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public IBitMask match_full();

        /// <summary>
        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public IGroup convert_special_to_empty_and_full_to_deleted();
    }
}