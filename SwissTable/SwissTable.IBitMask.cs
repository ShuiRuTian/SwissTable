// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    // The generic is for performance.
    // All implementations should be struct for performance, however, if the returned type is interface, there will be boxing and unboxing.
    // And inline will not work too.
    // So we need to pass a implementation.
    internal interface IBitMask<BitMaskImpl> where BitMaskImpl : struct, IBitMask<BitMaskImpl>
    {
        /// <summary>
        /// Returns a new `BitMask` with all bits inverted.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl invert();

        /// <summary>
        /// Returns a new `BitMask` with the lowest bit removed.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl remove_lowest_bit();

        /// <summary>
        /// Returns a new `BitMask` with the internal data logic and.
        /// </summary>
        /// <param name="bitMask"> must be the same type with caller</param>
        /// <returns></returns>
        BitMaskImpl And(BitMaskImpl bitMask);

        /// <summary>
        /// Returns whether the `BitMask` has at least one set bit.
        /// </summary>
        /// <returns></returns>
        bool any_bit_set();

        /// <summary>
        /// Returns the first set bit in the `BitMask`, if there is one.
        /// TODO: use negative rather than nullable to represent no bit set.
        /// </summary>
        /// <returns>
        /// negative means not any bit is set.
        /// </returns>
        int lowest_set_bit();

        /// <summary>
        /// Returns the first set bit in the `BitMask`, if there is one. The
        /// bitmask must not be empty.
        /// </summary>
        /// <returns></returns>
        // #[cfg(feature = "nightly")]
        int lowest_set_bit_nonzero();

        /// <summary>
        /// Returns the number of trailing zeroes in the `BitMask`.
        /// </summary>
        /// <returns></returns>
        int trailing_zeros();

        /// <summary>
        /// Returns the number of leading zeroes in the `BitMask`.
        /// </summary>
        /// <returns></returns>
        int leading_zeros();
    }
}
