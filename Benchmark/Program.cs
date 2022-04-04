using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Benchmark
{
    // As long as the instance is readonly, it could be inlined.
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args);
        }
    }
}