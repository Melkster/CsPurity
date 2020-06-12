using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsPurity;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

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

        //[TestMethod]
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
            var compilation = CSharpCompilation.Create("HelloWorld")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                ).AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            LookupTable lookupTable1 = new LookupTable(root, model);
            lookupTable1.BuildLookupTable();

            LookupTable lookupTable2 = new LookupTable(null, null);
            lookupTable2.AddMethod(GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(GetMethodDeclaration("bar", root));

            Assert.IsTrue(TablesAreEqual(lookupTable2.table, lookupTable1.table));
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
            var compilation = CSharpCompilation.Create("HelloWorld")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                ).AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            LookupTable lookupTable1 = new LookupTable(root, model);
            lookupTable1.BuildLookupTable();

            LookupTable lookupTable2 = new LookupTable(null, null);
            lookupTable2.AddMethod(GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(GetMethodDeclaration("bar", root));

            Assert.IsTrue(TablesAreEqual(lookupTable2.table, lookupTable1.table));
        }

        // Rows need to be in the same order in both tables
        static bool TablesAreEqual(DataTable table1, DataTable table2)
        {
            if (table1.Rows.Count != table1.Rows.Count) return false;

            for (int i = 0; i < table1.Rows.Count; i++)
            {
                if (!RowsAreEqual(table1.Rows[i], table2.Rows[i])) return false;
            }
            return true;

            // Depenency fields can be in different order
            static bool RowsAreEqual(DataRow row1, DataRow row2)
            {
                return
                    row1.Field<MethodDeclarationSyntax>("identifier") == row2.Field<MethodDeclarationSyntax>("identifier") &&
                    row1.Field<Purity>("purity") == row2.Field<Purity>("purity") &&
                    row1.Field<List<MethodDeclarationSyntax>>("dependencies").SequenceEqual(row2.Field<List<MethodDeclarationSyntax>>("dependencies"));
            }
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
            var methodDeclaration = GetMethodDeclaration("foo", root);

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
            var fooDeclaration = GetMethodDeclaration("foo", root);
            var barDeclaration = GetMethodDeclaration("bar", root);

            LookupTable lookupTable = new LookupTable(null, null);
            lookupTable.AddMethod(fooDeclaration);
            lookupTable.AddDependency(fooDeclaration, barDeclaration);

            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Console.WriteLine(lookupTable);
        }

        MethodDeclarationSyntax GetMethodDeclaration(string name, SyntaxNode root)
        {
            return root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == name)
                .Single();
        }
    }
}
