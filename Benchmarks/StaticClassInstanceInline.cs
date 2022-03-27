using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Benchmarks
{
    // As long as the instance is readonly, it could be inlined.
    [DisassemblyDiagnoser]
    public class StaticClassInstanceInline
    {

        public interface IGroupInfo
        {
            public int WIDTH { get; }
            public nuint BITMASK_MASK { get; }
        }

        class Sse2GroupInfo : IGroupInfo
        {
            // mem::size_of::<Self>() in rust.
            public int WIDTH => 128 / 8;

            // mem::align_of::<Self>() in rust.
            public const uint ALIGN_WIDTH = 128 / 8;
            public const uint BITMASK_STRIDE = 1;
            public nuint BITMASK_MASK => unchecked((nuint)0x8080_8080_8080_8080);
        }

        private static IGroupInfo _groupInfo = new Sse2GroupInfo();
        private static readonly IGroupInfo _groupInfo2 = new Sse2GroupInfo();

        [Benchmark]
        public nuint StaticInline()
        {
            return _groupInfo.BITMASK_MASK;
        }

        [Benchmark]
        public nuint StaticReadonlyInline()
        {
            return _groupInfo2.BITMASK_MASK;
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<StaticClassInstanceInline>();
        }
    }
}