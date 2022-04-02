
## This repo has been deprecated

The implementation is on real dotnet/runtime now to test perf: https://github.com/ShuiRuTian/runtime/tree/swisstable

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
cd src\libraries\System.Collections.Immutable\tests
dotnet build /t:Test

##### performance
$reposRoot = "C:\Code"

$upstreamRuntimeRepoRoot = $reposRoot + "\runtime"
$swisstableRuntimeRepoRoot = $reposRoot + "\runtimeSwisstable"
$performanceRoot = $reposRoot + "\performance"

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

function Analytics-Perf{
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

```
