write-safe-efficient-code: https://docs.microsoft.com/en-us/dotnet/csharp/write-safe-efficient-code
Span source code: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs
current HashTable of C#: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs

### C# Optimisation 
Some optimise of C# current HashTable implementation:
1. (C# specific) https://github.com/dotnet/runtime/issues/10050

### not that good in rust implementation
it is well-documentated, however, like C and C++, it prefers abbr. We should try to make everything clear like crystle


### how to run BenchMark by dotnet CLI
example:
``` ps
dotnet run -c Release -p:StartupObject=Benchmarks.GenericInline
```