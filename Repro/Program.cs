using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Repro
{
    class Program
    {
        const int Count = 2000;
        static void Main()
        {
            int[] _uniqueValues = ValuesGenerator.ArrayOfUniqueValues<int>(Count);
            Stopwatch sw = Stopwatch.StartNew();
            var res = ActualJob(_uniqueValues);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ActualJob(int[] _uniqueValues)
        {
            bool result = false;
            var collection = new MyDictionary<int, int>();

            for (int i = 0; i < 500000; i++)
            {
                var uniqueValues = _uniqueValues;
                for (int j = 0; j < uniqueValues.Length; j++)
                    collection.TryAdd(uniqueValues[j], uniqueValues[j]);
            }

            return result;
        }
    }
}