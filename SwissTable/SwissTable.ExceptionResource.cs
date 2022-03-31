namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        static class ExceptionResource
        {
            public const string NotSupported_KeyCollectionSet = "";
            public const string NotSupported_ValueCollectionSet = "";
            public const string Arg_RankMultiDimNotSupported = "";
            public const string Arg_NonZeroLowerBound = "";
            public const string Arg_ArrayPlusOffTooSmall = "";
            public const string Serialization_MissingKeys = "";
            public const string Serialization_NullKey = "";
        }
        static class ExceptionArgument
        {
            public const string dictionary = "";
            public const string collection = "";
            public const string capacity = "";
            public const string info = "";
            public const string key = "";
            public static TValue value = default;
            public const string array = "";
        }
    }
}