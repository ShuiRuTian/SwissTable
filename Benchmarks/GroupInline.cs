using System;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace System.Collections.Generic
{
    [DisassemblyDiagnoser/* (maxDepth: 3, printSource: true) */]
    // [DryJob]
    public class GroupInlineTest
    {
        private static readonly IGroup _sseGroup = new Sse2Group();
        private static readonly IGroup _fallbackGroup = new FallbackGroup();

        [Benchmark]
        unsafe public int? SseGroup()
        {
            var defaultArray = _sseGroup.static_empty();
            int? res;
            fixed (byte* ptr = &defaultArray[0])
            {
                res = _sseGroup.load(ptr).match_empty_or_deleted().lowest_set_bit();
            }
            return res;
        }

        [Benchmark]
        unsafe public int? FallbackGroup()
        {
            var defaultArray = _fallbackGroup.static_empty();
            int? res;
            fixed (byte* ptr = &defaultArray[0])
            {
                res = _fallbackGroup.load(ptr).match_empty_or_deleted().lowest_set_bit();
            }
            return res;
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<GroupInlineTest>();
        }
    }
}