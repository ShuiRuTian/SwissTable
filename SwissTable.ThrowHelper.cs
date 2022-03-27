namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        static class ThrowHelper
        {
            public static void ThrowArgumentOutOfRangeException()
            {
                throw new Exception();
            }
            public static void ThrowAddingDuplicateWithKeyArgumentException()
            {
                throw new Exception();
            }
            public static void ThrowArgumentNullException()
            {
                throw new Exception();
            }
            public static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
            {
                throw new Exception();
            }
        }
    }
}