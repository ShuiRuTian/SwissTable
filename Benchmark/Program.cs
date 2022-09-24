using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Configs;

namespace Benchmark
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IConfig config = new DebugInProcessConfig();
            BenchmarkRunner.Run(typeof(Program).Assembly);
            //BenchmarkSwitcher
            //    .FromAssembly(typeof(Program).Assembly)
            //    .Run(args);
        }
    }
}