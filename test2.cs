using System;
using System.Runtime.CompilerServices;
using System.Threading;

unsafe class Program
{
    static void Main()
    {
        while (true)
        {
            Example();
            Thread.Sleep(1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Example()
    {
        Guid g;
        Console.WriteLine(*&g);
    }
}