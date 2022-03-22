using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        enum InsertionBehavior
        {
            None,
            OverwriteExisting,
            ThrowOnExisting
        }
    }
}