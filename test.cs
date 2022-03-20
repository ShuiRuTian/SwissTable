using System;
public class C
{
    public interface IA
    {
        public int VFoo() { return 1; }
        public int Foo();
    }
    struct SA : IA
    {
        public int Foo() { return 2; }

        public int VFoo()
        {
            throw new NotImplementedException();
        }
    }
    class CA : IA
    {
        public int Foo() { return 2; }
    }
    public static void VirtualCall_Parent(IA ia)
    {
        ia.Foo();
        ia.VFoo();
    }

    public static void VirtualCall_Generic<T>(T t) where T : IA
    {
        t.Foo();
        t.VFoo();
    }

    public static void main()
    {
        var sa = new SA();
        var ca = new CA();
        sa.Foo();
        sa.VFoo();
        ca.Foo();
        VirtualCall_Parent(sa);
        VirtualCall_Parent(ca);
    }
}

// C.VirtualCall_Parent(IA)
//     L0000: push ebp
//     L0001: mov ebp, esp
//     L0003: sub esp, 0xc
//     L0006: mov [ebp-4], ecx
//     L0009: cmp dword ptr [0x1a6dc190], 0
//     L0010: je short L0017
//     L0012: call 0x65694bc0
//     L0017: nop
//     L0018: mov ecx, [ebp-4]
//     L001b: call dword ptr [0x1a6d7000]
//     L0021: mov [ebp-8], eax
//     L0024: nop
//     L0025: mov ecx, [ebp-4]
//     L0028: call dword ptr [0x1a6d7004]
//     L002e: mov [ebp-0xc], eax
//     L0031: nop
//     L0032: nop
//     L0033: mov esp, ebp
//     L0035: pop ebp
//     L0036: ret

// C.VirtualCall_Generic[[C+SA, _]](SA)
//     L0000: push ebp
//     L0001: mov ebp, esp
//     L0003: sub esp, 8
//     L0006: cmp dword ptr [0x1a6dc190], 0
//     L000d: je short L0014
//     L000f: call 0x65694bc0
//     L0014: nop
//     L0015: lea ecx, [ebp+8]
//     L0018: call dword ptr [0x1a6dcacc]
//     L001e: mov [ebp-4], eax
//     L0021: nop
//     L0022: lea ecx, [ebp+8]
//     L0025: call dword ptr [0x1a6dcae8]
//     L002b: mov [ebp-8], eax
//     L002e: nop
//     L002f: nop
//     L0030: mov esp, ebp
//     L0032: pop ebp
//     L0033: ret 4