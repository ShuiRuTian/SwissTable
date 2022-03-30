// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{
    internal interface IGroup
    {
        /// <summary>
        /// Returns a full group of empty bytes, suitable for use as the initial
        /// value for an empty hash table.
        /// </summary>
        /// <returns></returns>
        abstract byte[] static_empty();

        /// <summary>
        /// The bytes that the group ocupies
        /// </summary>
        int WIDTH { get; }

        unsafe IGroup load(byte* ptr);

        /// <summary>
        /// Loads a group of bytes starting at the given address, which must be
        /// aligned to the WIDTH
        /// </summary>
        /// <param name="ptr"></param>
        /// <returns></returns>
        unsafe IGroup load_aligned(byte* ptr);


        /// <summary>
        /// Stores the group of bytes to the given address, which must be
        /// aligned to WIDTH
        /// </summary>
        /// <param name="ptr"></param>
        unsafe void store_aligned(byte* ptr);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        IBitMask match_byte(byte b);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        /// </summary>
        /// <returns></returns>
        IBitMask match_empty();

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        /// </summary>
        /// <returns></returns>
        IBitMask match_empty_or_deleted();


        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are full.
        /// </summary>
        /// <returns></returns>
        IBitMask match_full();

        /// <summary>
        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        /// </summary>
        /// <returns></returns>
        IGroup convert_special_to_empty_and_full_to_deleted();
    }
}
