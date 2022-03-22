write-safe-efficient-code: https://docs.microsoft.com/en-us/dotnet/csharp/write-safe-efficient-code
Span source code: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs
current HashTable of C#: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs

### how to run BenchMark by dotnet CLI
``` ps
dotnet run -c Release -p:StartupObject=Benchmarks.GenericInline.Program
```