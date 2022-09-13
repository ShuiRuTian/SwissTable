// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{

    /// After C#11, `static_empty`, `create`, `load` and `load_aligned` should become static abstract mehod
    internal interface IGroup<BitMaskImpl, GroupImpl>
        where BitMaskImpl : struct, IBitMask<BitMaskImpl>
        where GroupImpl : struct, IGroup<BitMaskImpl, GroupImpl>
    {
        ///// <summary>
        ///// Returns a full group of empty bytes, suitable for use as the initial
        ///// value for an empty hash table.
        ///// </summary>
        ///// <returns></returns>
        ////byte[] static_empty { get; }

        ///// <summary>
        ///// The bytes that the group data ocupies
        ///// </summary>
        ///// <remarks>
        ///// The implementation should have `readonly` modifier
        ///// </remarks>
        ////int WIDTH { get; }

        ////unsafe GroupImpl load(byte* ptr);

        ///// <summary>
        ///// Loads a group of bytes starting at the given address, which must be
        ///// aligned to the WIDTH
        ///// </summary>
        ///// <param name="ptr"></param>
        ///// <returns></returns>
        ////unsafe GroupImpl load_aligned(byte* ptr);

        /// <summary>
        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        /// </summary>
        /// <returns></returns>
        GroupImpl convert_special_to_empty_and_full_to_deleted();

        /// <summary>
        /// Stores the group of bytes to the given address, which must be
        /// aligned to WIDTH
        /// </summary>
        /// <param name="ptr"></param>
        unsafe void StoreAligned(byte* ptr);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        BitMaskImpl MatchByte(byte b);

        // <summary>
        // Returns a `GroupImpl` with given byte brodcast.
        // </summary>
        // <param name="group"></param>
        // <returns></returns>
        // match_byte is good enough, however, we do not have readonly parameter now,
        // so we need add this as an optimsation.
        // GroupImpl create(byte b);

        // match_byte is good enough, however, we do not have readonly parameter now,
        // so we need add this as an optimsation.
        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group is matched with another group
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        BitMaskImpl MatchGroup(GroupImpl group);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchEmpty();

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchEmptyOrDeleted();


        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are full.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchFull();
    }
}
