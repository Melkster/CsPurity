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
                    int foo()
                    {
                        return bar;
                    }
                }
            ");
            Assert.AreEqual(0, CsPurityAnalyzer.Analyze(file));
        }

        [TestMethod]
        public void TestBasicPurity()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        int bar = 42;
                        return bar;
                    }
                }
            ");
            Assert.AreEqual(1, CsPurityAnalyzer.Analyze(file));
        }

        /// <summary>
        /// Empty input or input with no methods should have no purity.
        /// </summary>
        [TestMethod]
        public void TestNoMethodsInInput()
        {
            var file1 = ("");
            var file2 = ("foo");
            var file3 = (@"
                namespace TestSpace
                {
                    class TestClass { }
                }
            ");
            Assert.AreEqual(0, CsPurityAnalyzer.Analyze(file1));
            Assert.AreEqual(0, CsPurityAnalyzer.Analyze(file2));
            Assert.AreEqual(0, CsPurityAnalyzer.Analyze(file3));
        }

        /// <summary>
        /// Analyzer should handle local implicitly typed variables.
        ///
        /// Because `var` counts as an IdentifierNameSyntax this initially
        /// caused problems with the code.
        /// </summary>
        [TestMethod]
        public void HandleImmplicitlytTypedVariables()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        var bar = 42;
                        return bar;
                    }
                }
            ");
            Assert.AreEqual(1, CsPurityAnalyzer.Analyze(file));
        }
    }
}
