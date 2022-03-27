using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Benchmarks
{
    /// Pretty interesting. Struct and Class behave differently for interface call and generic restrict call.
    /// Anyway, this proves struct could be inlined for generic restrict call.
    [DisassemblyDiagnoser]
    public class StructInline
    {
        public struct Foo
        {
            public int a;
            public int b;
        }

        public class StructContainer
        {
            Foo foo;

            public void Update()
            {
                this.foo.a += 1;
                this.foo.b += 1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update2()
            {
                ref var tmp = ref foo;
                tmp.a += 1;
                tmp.b += 1;
            }
        }

        public class NormalContainer
        {
            int a;
            int b;
            public void Update()
            {
                this.a += 1;
                this.b += 1;
            }
        }

        [Benchmark]
        public StructContainer StructContainerUpdate()
        {
            var sc = new StructContainer();
            sc.Update();
            return sc;
        }

        [Benchmark]
        public StructContainer StructContainerUpdate2()
        {
            var sc = new StructContainer();
            sc.Update2();
            return sc;
        }

        [Benchmark]
        public NormalContainer NormalContainerUpdate()
        {
            var nc = new NormalContainer();
            nc.Update();
            return nc;
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<StructInline>();
        }
    }
}
