using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Repro
{
    class Program
    {
        const int Count = 1000;
        static void Main()
        {
            int[] _uniqueValues = ValuesGenerator.ArrayOfUniqueValues<int>(Count);
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 50000; i++)
            {
                var res = ActualJob(_uniqueValues);
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ActualJob(int[] _uniqueValues)
        {
            bool result = false;
            var collection = new MyDictionary<int, int>();

            var uniqueValues = _uniqueValues;
            for (int j = 0; j < uniqueValues.Length; j++)
                collection.TryAdd(uniqueValues[j], uniqueValues[j]);

            return result;
        }
    }
}