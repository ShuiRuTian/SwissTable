using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    /// <summary>
    /// A bit mask which contains the result of a `Match` operation on a `Group` and
    /// allows iterating through them.
    ///
    /// The bit mask is arranged so that low-order bits represent lower memory
    /// addresses for group match results.
    ///
    /// For implementation reasons, the bits in the set may be sparsely packed, so
    /// that there is only one bit-per-byte used (the high bit, 7). If this is the
    /// case, `BITMASK_STRIDE` will be 8 to indicate a divide-by-8 should be
    /// performed on counts/indices to normalize this difference. `BITMASK_MASK` is
    /// similarly a mask of all the actually-used bits.
    /// </summary>
    public interface IBitMask
    {
        /// <summary>
        /// Returns a new `BitMask` with all bits inverted.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        // #[must_use]
        public IBitMask invert();

        /// <summary>
        /// Flip the bit in the mask for the entry at the given index.
        ///
        /// Returns the bit's previous state.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        // #[cfg(feature = "raw")]
        public bool flip(uint index);

        /// <summary>
        /// Returns a new `BitMask` with the lowest bit removed.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        // #[must_use]
        public IBitMask remove_lowest_bit();

        /// <summary>
        /// Returns whether the `BitMask` has at least one set bit.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public bool any_bit_set();

        /// <summary>
        /// Returns the first set bit in the `BitMask`, if there is one.

        /// </summary>
        /// <returns></returns>
        // #[inline]
        public int? lowest_set_bit();

        /// <summary>
        /// Returns the first set bit in the `BitMask`, if there is one. The
        /// bitmask must not be empty.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        // #[cfg(feature = "nightly")]
        public int lowest_set_bit_nonzero();

        // // #[inline]
        // // #[cfg(not(feature = "nightly"))]
        // public abstract uint lowest_set_bit_nonzero();

        /// <summary>
        /// Returns the number of trailing zeroes in the `BitMask`.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public int trailing_zeros();

        /// <summary>
        /// Returns the number of leading zeroes in the `BitMask`.
        /// </summary>
        /// <returns></returns>
        // #[inline]
        public int leading_zeros();
    }
}