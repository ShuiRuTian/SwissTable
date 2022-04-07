using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Diagnostics;
using BenchmarkDotNet.Diagnostics.Windows.Configs;

namespace Benchmark
{
    // As long as the instance is readonly, it could be inlined.
    [EtwProfiler(performExtraBenchmarksRun:false)]
    [DisassemblyDiagnoser(maxDepth: 3, printSource: false, printInstructionAddresses: false)]
    public class GroupAndBitMask
    {
        //[DisassemblyDiagnoser(maxDepth: 3, printSource: true, printInstructionAddresses: false)]
        [NativeMemoryProfiler]
        [MemoryDiagnoser]
        public class TryAddDefaultSize
        {
            private int[] _uniqueValues;

            public int Count = 30000;

            [GlobalSetup]
            public void Setup() => _uniqueValues = ValuesGenerator.ArrayOfUniqueValues<int>(Count);


            [Benchmark]
            public MyDictionary<int, int> Dictionary()
            {
                var collection = new MyDictionary<int, int>(Count);
                var uniqueValues = _uniqueValues;
                for (int i = 0; i < uniqueValues.Length; i++)
                    collection.TryAdd(uniqueValues[i], uniqueValues[i]);
                return collection;
            }

            //byte[] data;
            //[GlobalSetup]
            //public void InitializeContainsValue()
            //{
            //    data = new byte[128 + 16];
            //    Array.Fill(data, SwissTableHelper.EMPTY);
            //}

            //[Benchmark]
            //public unsafe int GenericInline()
            //{
            //    var tmp = SwissTableHelper.DispatchFindInsertSlot(1234, data);
            //    return tmp;
            //}
        }
    }
}