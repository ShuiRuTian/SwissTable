using System.Collections.Generic;
using Xunit;

namespace SwissTableTest
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            var myDictionary = new MyDictionary<string, string>();
            myDictionary.Add("1","2");
        }
    }
}