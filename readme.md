write-safe-efficient-code: https://docs.microsoft.com/en-us/dotnet/csharp/write-safe-efficient-code
Span source code: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs
current HashTable of C#: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs

### C# Optimisation 
Some optimise of C# current HashTable implementation:
1. (C# specific) https://github.com/dotnet/runtime/issues/10050

### not that good in rust implementation
it is well-documentated, however, like C and C++, it prefers abbr. We should try to make everything clear like crystle

### difference

#### Find
Find bucket to insert and find bucket with specific key is differnt.

#### grow when Insert
> When calling "constructor", both implementations choose to not allocate memory unless it is necessary
C#:
1. check whehter initialized, if not, initialize
(here is sure there is always free space to insert)
2. (insert/throw error) according to parameter, return
3. update internal data, resize if no free space, then insert
4. use `NonRandomizedStringEqualityComparer`(comparer decide the hash code of KEY), and resize

Rust:
(control bytes always allocate, so we could always search, even if the real place holder is not )
1. search insert index
2. load ctrl byte
3. if not enough space and byte is EMPTY, resize and search insert index again
4. update inernal data
5. insert

### how to run BenchMark by dotnet CLI
example:
``` ps
dotnet run -c Release -p:StartupObject=Benchmarks.GenericInline
```