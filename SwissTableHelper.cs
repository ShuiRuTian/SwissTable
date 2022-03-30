// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

#pragma warning disable CA1810 // Initialize reference type static fields inline

namespace System.Collections.Generic
{
    internal static class SwissTableHelper
    {
        static SwissTableHelper()
        {
            if (Sse2.IsSupported)
            {
                _group = default(Sse2Group);
            }
            _group = default(FallbackGroup);
        }

        // The abstract that hides all detailed implementations.
        // Detect the hardware and set this value in the static constructor.
        public static readonly IGroup _group;

        /// Control byte value for an empty bucket.
        public const byte EMPTY = 0b1111_1111;

        /// Control byte value for a deleted bucket.
        public const byte DELETED = 0b1000_0000;

        /// Checks whether a control byte represents a full bucket (top bit is clear).
        public static bool is_full(byte ctrl) => (ctrl & 0x80) == 0;

        /// Checks whether a control byte represents a special value (top bit is set).
        public static bool is_special(byte ctrl) => (ctrl & 0x80) != 0;

        /// Checks whether a special control value is EMPTY (just check 1 bit).
        public static bool special_is_empty(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return (ctrl & 0x01) != 0;
        }

        /// Primary hash function, used to select the initial bucket to probe from.
        public static int h1(int hash)
        {
            return hash;
        }

        /// Secondary hash function, saved in the low 7 bits of the control byte.
        public static byte h2(int hash)
        {
            // Grab the top 7 bits of the hash.
            // cast to uint to use `shr` rahther than `sar`, which makes sure the top bit of returned byte is 0.
            var top7 = (uint)hash >> 25;
            return (byte)top7;
        }
    }
}
