using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using CsPurity;

namespace CsPurityTests
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public void TestMethod1()
        {
            var file = (@"
                class C1
                {
                    int bar = 42;
                    void add()
                    {
                        bar = 43;
                    }
                }
            ");

            Assert.IsTrue(true);
        }
    }
}
