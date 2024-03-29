
## This repo has been deprecated

The implementation is on real dotnet/runtime now to test perf: https://github.com/ShuiRuTian/runtime/tree/swisstable

## Why I need this repo?
cross-environemnt is hard and tricky.
We need a CLR to run C# program on. And there is an updated Dictionary implementation, which we want to use in the test.
The test is C# code, no suprising. But the program that starts the test is also C# which use the updated Dictionary.
Dictionary is widely used, so the program might just crashes as long as there is some error :/

### Resources

write-safe-efficient-code: https://docs.microsoft.com/en-us/dotnet/csharp/write-safe-efficient-code
Span source code: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs
current HashTable of C#: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs

### C# Optimisation 
Some optimise of C# current HashTable implementation:
1. (C# specific) https://github.com/dotnet/runtime/issues/10050

### not that good in rust implementation
it is well-documentated, however, like C and C++, it prefers abbr. We should try to make everything clear like crystle

### difference

### Optimisation in my implementation step by step
1. boxing and unboxing
For performance, we should use struct as possiable as we could. But we also want abstract to have type check.
``` cs
IGroup static Foo(){
    // Do something...
    // Finally return a new one with new data, we hide the data here.
    return Sse2Group(); // Sse2Group is a struct, and this method is in Sse2Group too.
}
```
Is it ok to call method Chainly like this?
At first, I thought CLR will inline, after inline it will find Sse2Group is also struct so there will be no boxing.
However, I am wrong. So I have to write some more ugly code.
After fixing, this gives a huge perf improvement.
In my test case, reduce time from 780 to 620

2. Inline more
[MethodImpl(MethodImplOptions.AggressiveInlining)]
It is proven this is pretty powerful and need to be controled carefully
This might introduce +200 or -200 easily.

3. Not yet readonly variable and readonly parameter
``` cs
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public Avx2BitMask match_byte(byte b)
{
    // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
    var compareValue = Vector256.Create(b);
    var cmp = Avx2.CompareEqual(this._data, compareValue);
    return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
}
```
Looks good, right?
However, this method is called in a loop.
We could not mark `b` and `compareValue` as readonly, so it always create a new one when call this method.
Have to add some more method and not use this friendly method.
this reduce about 20.

4. pointer
This is dangerous, however, we might need it to avoid edge check.

5. inline in Value type and reference type
This is used in C# now.
If there is no provided comparer. 
``` cs
EqualityComparer<TKey>.Default.Equals(a,b)
```
This will be inlined for value type, 
but for reference type it is harmful.
Treat it differently
``` cs
if (hashComparer == null)
{
    if (typeof(TKey).IsValueType) { }
    else {}
}
```
this reduce about 40

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

### clone the working dotnet/runtime
git clone --single-branch --branch implement-swisstable-as-hashmap https://github.com/ShuiRuTian/runtime.git --depth=1

###

``` ps1

###### runtime

# full build command
./build.cmd clr+libs+libs.tests -rc release -lc release

# iterate command for debug
./build.cmd clr.corelib+clr.nativecorelib+libs.pretest -rc Release

# iterate command for perf-test
./build.cmd clr.corelib+clr.nativecorelib+libs.pretest -rc Release -lc Release

# run test for collections
cd src\libraries\System.Collections\tests
dotnet build /t:Test

# close tired compile 
$env:DOTNET_TieredCompilation = 0

##### performance
$reposRoot = "D:\MyRepo"

$upstreamRuntimeRepoRoot = $reposRoot + "\runtime"
$swisstableRuntimeRepoRoot = $reposRoot + "\runtimeSwisstable"
$performanceRoot = $reposRoot + "\performance"

$swisstableRuntimeCollectionTest = $swisstableRuntimeRepoRoot + "\src\libraries\System.Collections\tests"

$microbenchmarksFullPath = $performanceRoot + "\src\benchmarks\micro"
$ResultsComparerFullPath = $performanceRoot + "\src\tools\ResultsComparer"

$perfFolderRoot = $reposRoot + "\SwissTablePerf"
$upstreamPerf = $perfFolderRoot + "\before"
$swisstablePerf = $perfFolderRoot + "\after"
$swisstablePerf1 = $perfFolderRoot + "\after1"

$coreRunRelativePath = "\artifacts\bin\testhost\net7.0-windows-Release-x64\shared\Microsoft.NETCore.App\7.0.0\CoreRun.exe"
$upstreamCoreRun = $upstreamRuntimeRepoRoot + $coreRunRelativePath
$swisstableCoreRun = $swisstableRuntimeRepoRoot + $coreRunRelativePath

$dictionaryFilter = "System.Collection*.Dictionary"

function Build-IterSwisstableDebug{
    [CmdletBinding()]
    param ()
    pushd
    cd $swisstableRuntimeRepoRoot
    ./build.cmd clr.corelib+clr.nativecorelib+libs.pretest -rc Release
    popd
}

function Build-IterSwisstableRelease{
    [CmdletBinding()]
    param ()
    pushd
    cd $swisstableRuntimeRepoRoot
    ./build.cmd clr.corelib+clr.nativecorelib+libs.pretest -rc Release  -lc Release
    popd
}

function Test-Collections{
    [CmdletBinding()]
    param ()
    dotnet build /t:Test
}

function Generate-PerfAnalytics{
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$DestinationPerfFolder,
        [Parameter(Mandatory)]
        [string]$CoreRunPath
    )
    pushd
    cd $microbenchmarksFullPath
    dotnet run -c Release -f net7.0 `
        --artifacts $DestinationPerfFolder `
        --coreRun $CoreRunPath `
        --filter $dictionaryFilter
    popd
}

function Generate-PerfAnalyticsForBefore {
    [CmdletBinding()]
    param ()
    Generate-PerfAnalytics $upstreamPerf $upstreamCoreRun
}

function Generate-PerfAnalyticsForNow {
    [CmdletBinding()]
    param ()
    Generate-PerfAnalytics $swisstablePerf $swisstableCoreRun
}

function Generate-PerfAnalyticsForNow1 {
    [CmdletBinding()]
    param ()
    Generate-PerfAnalytics $swisstablePerf1 $swisstableCoreRun
}

function Analytics-PerfBaseUpstream{
    [CmdletBinding()]
    param ()
    pushd
    cd $ResultsComparerFullPath
    dotnet run --base $upstreamPerf --diff $swisstablePerf --threshold 2%
    popd
}

function Analytics-PerfIter{
    [CmdletBinding()]
    param ()
    pushd
    cd $ResultsComparerFullPath
    dotnet run --base $swisstablePerf --diff $swisstablePerf1 --threshold 2%
    popd
}

function Analytics-PerfIterBaseUpstream{
    [CmdletBinding()]
    param ()
    pushd
    cd $ResultsComparerFullPath
    dotnet run --base $upstreamPerf --diff $swisstablePerf1 --threshold 2%
    popd
}

function Generate-SwisstablePerfDetailedAnalyticsForCase{
    # generate assembly and etw
    [CmdletBinding()]
    param ()
    pushd
    cd $microbenchmarksFullPath
    dotnet run -c Release -f net7.0 --filter System.Collections.TryAddDefaultSize*.Dictionary --coreRun $swisstableCoreRun --profiler ETW 
    popd
}
```
