using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;


namespace Benchmarks
{
    // As long as the instance is readonly, it could be inlined.
    [DisassemblyDiagnoser]
    public class HardwareSpecificBranch
    {
        [Benchmark]
        public bool StaticReadonlyInline()
        {
            bool? res = null;
            if (Sse2.IsSupported)
            {
                res = true;
            }
            else
            {
                res = false;
            }
            return res.Value;
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<HardwareSpecificBranch>();
        }
    }
}