using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Diagnostics;

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
            data = new byte[128 + 16];
            Array.Fill(data, SwissTableHelper.EMPTY);
        }

        [Benchmark]
        public unsafe int GenericInline()
        {
            var tmp = SwissTableHelper.DispatchFindInsertSlot(1234, data);
            return tmp;
        }
    }
}