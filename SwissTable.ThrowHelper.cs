namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        static class ThrowHelper
        {
            public static void ThrowSerializationException<T>(T any)
            {
                throw new Exception();
            }
            public static void ThrowKeyNotFoundException<T>(T any)
            {
                throw new Exception();
            }
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
            public static void IfNullAndNullsAreIllegalThenThrow<T>(object? anyO, T any)
            {
                throw new Exception();
            }
            public static void ThrowWrongValueTypeArgumentException<T, U>(T any1, U any2)
            {
                throw new Exception();
            }
            public static void ThrowWrongKeyTypeArgumentException<T, U>(T any1, U any2)
            {
                throw new Exception();
            }
        }
    }
}