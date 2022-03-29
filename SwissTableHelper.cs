using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    static class SwissTableHelper
    {
        // Control byte value for an empty bucket.
        public const byte EMPTY = 0b1111_1111;

        /// Control byte value for a deleted bucket.
        public const byte DELETED = 0b1000_0000;

        /// Checks whether a control byte represents a full bucket (top bit is clear).
        // #[inline]
        public static bool is_full(byte ctrl) => (ctrl & 0x80) == 0;

        /// Checks whether a control byte represents a special value (top bit is set).
        // #[inline]
        public static bool is_special(byte ctrl) => (ctrl & 0x80) != 0;

        /// Checks whether a special control value is EMPTY (just check 1 bit).
        // #[inline]
        public static bool special_is_empty(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return (ctrl & 0x01) != 0;
        }

        /// Primary hash function, used to select the initial bucket to probe from.
        // #[inline]
        // #[allow(clippy::cast_possible_truncation)]
        public static int h1(int hash)
        {
            // On 32-bit platforms we simply ignore the higher hash bits.
            return hash;
        }

        /// Secondary hash function, saved in the low 7 bits of the control byte.
        // #[inline]
        // #[allow(clippy::cast_possible_truncation)]
        public static byte h2(int hash)
        {
            // Grab the top 7 bits of the hash. While the hash is normally a full 64-bit
            // value, some hash functions (such as FxHash) produce a usize result
            // instead, which means that the top 32 bits are 0 on 32-bit platforms.
            var top7 = hash >> 25;
            return (byte)(top7 & 0x7f); // truncation
        }
    }
}