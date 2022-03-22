using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Benchmarks
{
    /// Pretty interesting. Struct and Class behave differently for interface call and generic restrict call.
    /// Anyway, this proves struct could be inlined for generic restrict call.
    [DisassemblyDiagnoser]
    public class GenericInline
    {
        public interface IA
        {
            public int Foo();
        }

        struct SA : IA
        {
            public int Foo() { return 1; }
        }

        class CA : IA
        {
            public int Foo() { return 2; }
        }

        public int VirtualCall_Parent(IA ia)
        {
            return ia.Foo();
        }

        public int VirtualCall_Generic<T>(T t) where T : IA
        {
            return t.Foo();
        }

        [Benchmark]
        public int StructInstanceCall()
        {
            var sa = new SA();
            return sa.Foo();
        }

        [Benchmark]
        public int StructVirtualCallParent()
        {
            var sa = new SA();
            return VirtualCall_Parent(sa);
        }

        [Benchmark]
        public int StructVirtualCallGeneric()
        {
            var sa = new SA();
            return VirtualCall_Generic(sa);
        }

        [Benchmark]
        public int ClassInstanceCall()
        {
            var ca = new CA();
            return ca.Foo();
        }

        [Benchmark]
        public int ClassVirtualCallParent()
        {
            var ca = new CA();
            return VirtualCall_Parent(ca);
        }

        [Benchmark]
        public int ClassVirtualCallGeneric()
        {
            var ca = new CA();
            return VirtualCall_Generic(ca);
        }

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<GenericInline>();
        }
    }
}
