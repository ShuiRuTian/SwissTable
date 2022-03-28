namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        static class ThrowHelper
        {
            public static void ThrowArgumentOutOfRangeException<T>(T any)
            {
                throw new Exception();
            }
            public static void ThrowAddingDuplicateWithKeyArgumentException()
            {
                throw new Exception();
            }
            public static void ThrowArgumentNullException<T>(T any)
            {
                throw new Exception();
            }
            public static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
            {
                throw new Exception();
            }
            public static void ThrowNotSupportedException<T>(T key)
            {
                throw new Exception();
            }
            public static void ThrowArgumentException<T>(T key)
            {
                throw new Exception();
            }
            public static void ThrowIndexArgumentOutOfRange_NeedNonNegNumException()
            {
                throw new Exception();
            }
            public static void ThrowArgumentException_Argument_InvalidArrayType()
            {
                throw new Exception();
            }
            public static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
            {
                throw new Exception();
            }
            public static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
            {
                throw new Exception();
            }
        }
    }
}