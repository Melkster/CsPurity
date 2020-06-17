using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsPurity;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

using static System.Console;

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
        public void HandleImmplicitlyTypedVariables()
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
        public void TestReadsStaticField()
        {
            var file = (@"
            class C1
            {
                int foo()
                {
                    bar();
                    var hej = ""hej"";
                    return 42 + C2.StaticValue;
                }

                int bar() {
                    return 1;
                }
            }

            class C2
            {
                public static int StaticValue = 1;
            }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = GetMethodDeclaration("foo", root);
            Analyzer analyzer = new Analyzer(file);
            Assert.IsTrue(analyzer.ReadsStaticField(fooDeclaration));
        }

        public static MethodDeclarationSyntax GetMethodDeclaration(string name, SyntaxNode root)
        {
            return root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == name)
                .Single();
        }

        // Rows need to be in the same order in both tables
        public static bool TablesAreEqual(DataTable table1, DataTable table2)
        {
            if (table1.Rows.Count != table1.Rows.Count) return false;

            for (int i = 0; i < table1.Rows.Count; i++)
            {
                if (!RowsAreEqual(table1.Rows[i], table2.Rows[i])) return false;
            }
            return true;

            // Dependency fields can be in different order
            static bool RowsAreEqual(DataRow row1, DataRow row2)
            {
                return
                    row1.Field<MethodDeclarationSyntax>("identifier") == row2.Field<MethodDeclarationSyntax>("identifier") &&
                    row1.Field<Purity>("purity") == row2.Field<Purity>("purity") &&
                    HaveEqualElements(
                        row1.Field<List<MethodDeclarationSyntax>>("dependencies"),
                        row2.Field<List<MethodDeclarationSyntax>>("dependencies")
                    );
            }
        }

        public static bool HaveEqualElements(IEnumerable<Object> list1, IEnumerable<Object> list2)
        {
            if (list1.Count() != list2.Count()) return false;
            foreach (var item in list1)
            {
                if (!list2.Contains(item)) return false;
            }
            return true;
        }
    }

    [TestClass]
    public class LookupTableTest
    {

        [TestMethod]
        public void TestGetDependencies()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar();
                    }

                    int bar() {
                        return ""bar"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResult = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Last();
            var expectedResultList = new List<MethodDeclarationSyntax> { expectedResult };

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResultList
                )
            );
        }

        [TestMethod]
        public void TestGettingMultipleDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        baz();
                        return bar();
                    }

                    string bar()
                    {
                        return ""bar"";
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingNestedDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar();
                    }

                    string bar()
                    {
                        return baz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingMultipleNestedDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar() + baz();
                    }

                    string bar()
                    {
                        return far() + faz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }

                    void far()
                    {
                        return ""far"";
                    }

                    void faz()
                    {
                        return ""faz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingMethodDependency()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        C2 c2 = new C2();
                        return c2.bar();
                    }
                }

                class C2
                {
                    public string bar()
                    {
                        return ""bar"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingDependenciesWithSameNames()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return C2.bar() + bar();
                    }

                    string bar()
                    {
                        return ""bar"";
                    }
                }

                class C2
                {
                    public static string bar()
                    {
                        return ""bar"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingMultipleIdenticalDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar() + baz();
                    }

                    string bar()
                    {
                        return ""bar"" + baz() + baz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .ToList();

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingBuiltInMethod()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        Console.WriteLine();
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(root, model);
            var fooDependencies = lt.GetDependencies(fooDeclaration);
            var expectedResultList = new List<MethodDeclarationSyntax> { null };

            Assert.IsTrue(
                UnitTest.HaveEqualElements(
                    fooDependencies,
                    expectedResultList
                )
            );
        }

        [TestMethod]
        public void TestBuildLookupTable1()
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
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            LookupTable lookupTable1 = new LookupTable(root, model);
            lookupTable1.BuildLookupTable();

            LookupTable lookupTable2 = new LookupTable(null, null);
            lookupTable2.AddMethod(UnitTest.GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(UnitTest.GetMethodDeclaration("bar", root));
            lookupTable2.AddDependency(
                UnitTest.GetMethodDeclaration("foo", root),
                UnitTest.GetMethodDeclaration("bar", root)
            );

            Assert.IsTrue(UnitTest.TablesAreEqual(lookupTable2.table, lookupTable1.table));
        }

        [TestMethod]
        public void TestBuildLookupTable2()
        {
            var file = (@"
                class C2
                {
                    int foo()
                    {
                        C2 c2 = new C2();
                        return c2.bar();
                    }
                }

                class C2
                {
                    int bar()
                    {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            LookupTable lookupTable1 = new LookupTable(root, model);
            lookupTable1.BuildLookupTable();

            LookupTable lookupTable2 = new LookupTable(null, null);
            lookupTable2.AddMethod(UnitTest.GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(UnitTest.GetMethodDeclaration("bar", root));
            lookupTable2.AddDependency(
                UnitTest.GetMethodDeclaration("foo", root),
                UnitTest.GetMethodDeclaration("bar", root)
            );

            Assert.IsTrue(UnitTest.TablesAreEqual(lookupTable2.table, lookupTable1.table));
        }

        [TestMethod]
        public void TestHasMethod()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return 42;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var methodDeclaration = UnitTest.GetMethodDeclaration("foo", root);

            LookupTable lookupTable = new LookupTable(null, null);
            lookupTable.AddMethod(methodDeclaration);

            Assert.IsTrue(lookupTable.HasMethod(methodDeclaration));
        }

        [TestMethod]
        public void TestAddDependency()
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
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = UnitTest.GetMethodDeclaration("foo", root);
            var barDeclaration = UnitTest.GetMethodDeclaration("bar", root);

            LookupTable lookupTable = new LookupTable(null, null);
            lookupTable.AddMethod(fooDeclaration);
            lookupTable.AddDependency(fooDeclaration, barDeclaration);

            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, barDeclaration));
        }

        [TestMethod]
        public void TestRemoveDependency()
        {
            var file = (@"
                    class C1
                    {
                        int foo()
                        {
                            return bar() + C2.baz();
                        }

                        int bar()
                        {
                            return 42 + C2.baz();
                        }
                    }

                    class C2
                    {
                        public static int baz()
                        {
                            return 42;
                        }
                    }
                ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = UnitTest.GetMethodDeclaration("foo", root);
            var barDeclaration = UnitTest.GetMethodDeclaration("bar", root);
            var bazDeclaration = UnitTest.GetMethodDeclaration("baz", root);

            LookupTable lookupTable = new LookupTable(null, null);
            lookupTable.AddMethod(fooDeclaration);
            lookupTable.AddDependency(fooDeclaration, barDeclaration);
            lookupTable.AddDependency(fooDeclaration, bazDeclaration);
            lookupTable.AddDependency(barDeclaration, bazDeclaration);
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(fooDeclaration, barDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(fooDeclaration, bazDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(barDeclaration, bazDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(barDeclaration, bazDeclaration));


            var model = Analyzer.GetSemanticModel(tree);
            LookupTable lookupTable2 = new LookupTable(root, model);
            lookupTable2.BuildLookupTable();

            Assert.IsTrue(lookupTable2.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable2.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable2.HasDependency(barDeclaration, bazDeclaration));
        }
    }
}
