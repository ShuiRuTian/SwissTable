using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Collections.Generic;

namespace Benchmark
{
    // As long as the instance is readonly, it could be inlined.
    [DisassemblyDiagnoser(maxDepth: 3, printSource: false, printInstructionAddresses: false)]
    public class GroupAndBitMask
    {
        byte[] data;
        [GlobalSetup]
        public void InitializeContainsValue()
        {
            data = new byte[100];
        }

        [Benchmark]
        public unsafe IBitMask AggressiveLineForRuntime()
        {
            fixed (byte* ptr = &data[0])
            {
                IBitMask bitMask = new Sse2Group().load(ptr)
                    .match_full()
                    .remove_lowest_bit()
                    .remove_lowest_bit()
                    .remove_lowest_bit();
                return bitMask;
            }
        }
    }
}