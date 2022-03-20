using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
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
    public interface IBitMask
    {
        /// Returns a new `BitMask` with all bits inverted.
        // #[inline]
        // #[must_use]
        public IBitMask invert();

        /// Flip the bit in the mask for the entry at the given index.
        ///
        /// Returns the bit's previous state.
        // #[inline]
        // #[allow(clippy::cast_ptr_alignment)]
        // #[cfg(feature = "raw")]
        public bool flip(uint index);

        /// Returns a new `BitMask` with the lowest bit removed.
        // #[inline]
        // #[must_use]
        public IBitMask remove_lowest_bit();

        /// Returns whether the `BitMask` has at least one set bit.
        // #[inline]
        public bool any_bit_set();

        /// Returns the first set bit in the `BitMask`, if there is one.
        // #[inline]
        public int? lowest_set_bit();

        /// Returns the first set bit in the `BitMask`, if there is one. The
        /// bitmask must not be empty.
        // #[inline]
        // #[cfg(feature = "nightly")]
        public int lowest_set_bit_nonzero();

        // // #[inline]
        // // #[cfg(not(feature = "nightly"))]
        // public abstract uint lowest_set_bit_nonzero();

        /// Returns the number of trailing zeroes in the `BitMask`.
        // #[inline]
        public int trailing_zeros();

        /// Returns the number of leading zeroes in the `BitMask`.
        // #[inline]
        public int leading_zeros();
    }
}