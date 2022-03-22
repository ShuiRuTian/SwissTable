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
        }

        class Sse2GroupInfo : IGroupInfo
        {
            // mem::size_of::<Self>() in rust.
            public int WIDTH => 128 / 8;

            // mem::align_of::<Self>() in rust.
            public const uint ALIGN_WIDTH = 128 / 8;
            public const uint BITMASK_STRIDE = 1;
            public const ushort BITMASK_MASK = 0xffff;
        }

        private static IGroupInfo _groupInfo = new Sse2GroupInfo();
        private static readonly IGroupInfo _groupInfo2 = new Sse2GroupInfo();

        [Benchmark]
        public int StaticInline()
        {
            return _groupInfo.WIDTH;
        }

        [Benchmark]
        public int StaticReadonlyInline()
        {
            return _groupInfo2.WIDTH;
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<StaticClassInstanceInline>();
        }
    }
}