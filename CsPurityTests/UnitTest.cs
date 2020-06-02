using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsPurity;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Data;
using System.Collections.Generic;
using System;
using System.Linq;

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

        [TestMethod]
        public void TestBuildLookupTable()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar();
                    }

                    int bar()
                    {
                        return 42;
                    }
                }
            ");
            DataTable lookupTable = CsPurityAnalyzer.InitializeLookupTable();
            lookupTable.Rows.Add("foo", new List<string> { "bar" }, Purity.Pure);
            lookupTable.Rows.Add("bar", new List<string>(), Purity.Pure);

            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            Assert.IsTrue(TablesAreEqual(lookupTable, CsPurityAnalyzer.BuildLookupTable(root)));
        }

        static bool TablesAreEqual(DataTable table1, DataTable table2)
        {
            if (table1.Rows.Count != table1.Rows.Count) return false;

            for (int i = 0; i < table1.Rows.Count; i++)
            {
                if (!RowsAreEqual(table1.Rows[i], table2.Rows[i])) return false;
            }
            return true;

            static bool RowsAreEqual(DataRow row1, DataRow row2)
            {
                return
                    row1.Field<string>("identifier") == row2.Field<string>("identifier") &&
                    row1.Field<Purity>("purity") == row2.Field<Purity>("purity") &&
                    ListsAreEqual(row1.Field<List<string>>("dependencies"),
                        row2.Field<List<string>>("dependencies")
                    );
            }

            static bool ListsAreEqual(List<string> list1, List<string> list2)
            {
                list1.Sort(); // The order of the list shouldn't matter
                list2.Sort();
                foreach (string item1 in list1)
                {
                    foreach (string item2 in list2)
                    {
                        if (item1 != item2) return false;
                    }
                }
                return true;
            }
        }
    }
}
