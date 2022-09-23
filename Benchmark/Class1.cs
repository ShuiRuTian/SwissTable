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
using Newtonsoft.Json.Linq;
using System.Drawing;

namespace Benchmark
{
    // As long as the instance is readonly, it could be inlined.
    //[EtwProfiler(performExtraBenchmarksRun:false)]
    public class GroupAndBitMask
    {
        //[DisassemblyDiagnoser(maxDepth: 3, printSource: true, printInstructionAddresses: false)]
        //[NativeMemoryProfiler]
        //[MemoryDiagnoser]
        public class TryAddDefaultSize
        {
            private int[] _allValues;
            private int[] _found;
            private int[] _notFound;

            private Dictionary<int, int> _dictionary;
            private MyDictionary<int, int> _mydictionary;

            public int Count = 500;

            [GlobalSetup]
            public void Setup()
            {
                _allValues = ValuesGenerator.ArrayOfUniqueValues<int>(Count * 2);
                _found = _allValues.Take(Count).ToArray();
                _notFound = _allValues.Skip(Count).Take(Count).ToArray();
                _dictionary = _found.ToDictionary(x => x, x => x);
                _mydictionary = new MyDictionary<int, int>(_dictionary);
            }

            [Benchmark]
            public Dictionary<int, int> Dictionary_AddWithGivenSize()
            {
                var collection = new Dictionary<int, int>(Count);
                var uniqueValues = _found;
                for (int i = 0; i < uniqueValues.Length; i++)
                    collection.TryAdd(uniqueValues[i], uniqueValues[i]);
                return collection;
            }

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_Dictionary_AddWithGivenSize()
            {
                var collection = new MyDictionary<int, int>(Count);
                var uniqueValues = _found;
                for (int i = 0; i < uniqueValues.Length; i++)
                    collection.TryAdd(uniqueValues[i], uniqueValues[i]);
                return collection;
            }

            [Benchmark]
            public Dictionary<int, int> Dictionary_AddWithEmptySize()
            {
                var collection = new Dictionary<int, int>();
                var uniqueValues = _found;
                for (int i = 0; i < uniqueValues.Length; i++)
                    collection.TryAdd(uniqueValues[i], uniqueValues[i]);
                return collection;
            }

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_Dictionary_AddWithEmptySize()
            {
                var collection = new MyDictionary<int, int>();
                var uniqueValues = _found;
                for (int i = 0; i < uniqueValues.Length; i++)
                    collection.TryAdd(uniqueValues[i], uniqueValues[i]);
                return collection;
            }

            [Benchmark]
            public Dictionary<int, int> Dictionary_AddAndRemove()
            {
                var dictionary = new Dictionary<int, int>();
                foreach (var uniqueKey in _found)
                {
                    dictionary.Add(uniqueKey, uniqueKey);
                }
                foreach (var uniqueKey in _found)
                {
                    dictionary.Remove(uniqueKey);
                }
                return dictionary;
            }

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_AddAndRemove()
            {
                var dictionary = new MyDictionary<int, int>();
                foreach (var uniqueKey in _found)
                {
                    dictionary.Add(uniqueKey, uniqueKey);
                }
                foreach (var uniqueKey in _found)
                {
                    dictionary.Remove(uniqueKey);
                }
                return dictionary;
            }

            [Benchmark]
            public Dictionary<int, int> Dictionary_Index()
            {
                var dictionary = _dictionary;
                var keys = _found;
                for (int i = 0; i < keys.Length; i++)
                    dictionary[keys[i]] = default;
                return dictionary;
            }

            [Benchmark]
            public bool Dictionary_ContainsKeyFalse()
            {
                bool result = default;
                var collection = _dictionary;
                var notFound = _notFound;
                for (int i = 0; i < notFound.Length; i++)
                    result ^= collection.ContainsKey(notFound[i]);
                return result;
            }

            [Benchmark]
            public bool MyDictionary_ContainsKeyFalse()
            {
                bool result = default;
                var collection = _mydictionary;
                var notFound = _notFound;
                for (int i = 0; i < notFound.Length; i++)
                    result ^= collection.ContainsKey(notFound[i]);
                return result;
            }

            [Benchmark]
            public bool Dictionary_ContainsKeyTrue()
            {
                bool result = default;
                var collection = _dictionary;
                var found = _found;
                for (int i = 0; i < found.Length; i++)
                    result ^= collection.ContainsKey(found[i]);
                return result;
            }

            [Benchmark]
            public bool MyDictionary_ContainsKeyTrue()
            {
                bool result = default;
                var collection = _mydictionary;
                var found = _found;
                for (int i = 0; i < found.Length; i++)
                    result ^= collection.ContainsKey(found[i]);
                return result;
            }

            [Benchmark]
            public bool Dictionary_TryGetValue()
            {
                bool result = default;
                var collection = _dictionary;
                var notFound = _notFound;
                for (int i = 0; i < notFound.Length; i++)
                    result ^= collection.TryGetValue(notFound[i], out _);
                return result;
            }

            [Benchmark]
            public bool MyDictionary_TryGetValue()
            {
                bool result = default;
                var collection = _mydictionary;
                var notFound = _notFound;
                for (int i = 0; i < notFound.Length; i++)
                    result ^= collection.TryGetValue(notFound[i], out _);
                return result;
            }

            [Benchmark]
            public int Dictionary_ForEach()
            {
                int result = default;
                var collection = _dictionary;
                foreach (var item in collection)
                    result = item.Value;
                return result;
            }

            [Benchmark]
            public int MyDictionary_ForEach()
            {
                int result = default;
                var collection = _mydictionary;
                foreach (var item in collection)
                    result = item.Value;
                return result;
            }

            [Benchmark]
            public Dictionary<int, int> Dictionary_DefaultConstructor() => new Dictionary<int, int>();

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_DefaultConstructor() => new MyDictionary<int, int>();

            [Benchmark]
            public Dictionary<int, int> Dictionary_ConstructWithGivenSize() => new Dictionary<int, int>(Count);

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_ConstructWithGivenSize() => new MyDictionary<int, int>(Count);

            [Benchmark]
            public Dictionary<int, int> Dictionary_ConstructFromIDictionary() => new Dictionary<int, int>(_dictionary);

            [Benchmark]
            public MyDictionary<int, int> MyDictionary_ConstructFromIDictionary() => new MyDictionary<int, int>(_dictionary);
        }
    }
}