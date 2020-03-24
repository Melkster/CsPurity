using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsPurity;

namespace CsPurityTests
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public void TestBasicImpurity()
        {
            var file = (@"
                class C1
                {
                    int bar = 42;
                    void foo()
                    {
                        return bar;
                    }
                }
            ");
            Assert.IsFalse(CsPurityAnalyzer.Analyze(file));
        }

        [TestMethod]
        public void TestBasicPurity()
        {
            var file = (@"
                class C1
                {
                    void foo()
                    {
                        int bar = 42;
                        return bar;
                    }
                }
            ");
            Assert.IsTrue(CsPurityAnalyzer.Analyze(file));
        }
    }
}
