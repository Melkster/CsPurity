using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Data;

using static System.Console;

namespace CsPurity
{
    public enum Purity
    {
        Pure,
        ParametricallyImpure,
        Impure
    }

    public class CsPurityAnalyzer
    {
        /// <summary>
        /// Analyzes the purity of the given text.
        /// </summary>
        /// <param name="text"></param>
        /// <returns>The average purity of all methods in <paramref name="text"/></returns>
        public static double Analyze(string text)
        {
            var result = new List<int>();
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var compilation = CSharpCompilation.Create("HelloWorld")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                ).AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDeclaration in methodDeclarations)
            {
                var identifierNames = methodDeclaration
                    .DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(i => i.Identifier.Text != "var"); // `var` also counts as IdentifierNameSyntax

                foreach (var identifierName in identifierNames)
                {
                    var identifierSymbol = (VariableDeclaratorSyntax)model
                        .GetSymbolInfo(identifierName)
                        .Symbol // TODO: `.Symbol` can be null, for instance when the symbol is a class name
                        .DeclaringSyntaxReferences
                        .Single() // TODO: look at all references
                        .GetSyntax();
                    var methodAncestors = identifierSymbol.Ancestors().OfType<MethodDeclarationSyntax>();
                    bool methodIsPure = false;

                    if (methodAncestors.Any()) methodIsPure = methodAncestors.First() == methodDeclaration;
                    result.Add(Convert.ToInt32(methodIsPure));
                }
            }

            return result.Any() ? result.Average() : 0; // If input text has no methods purity is 0
        }

        static void Main(string[] args)
        {
            var text = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.bar();
                    }

                    int bar()
                    {
                        return 42;
                    }

                    class C2
                    {
                        public static int bar()
                        {
                            return 1;
                        }
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var compilation = CSharpCompilation.Create("HelloWorld")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                ).AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            LookupTable lt = new LookupTable();
            lt.BuildLookupTable(root, model);
            //WriteLine(lt);

            // --- TODO: uncomment this before merge

            //    if (!args.Any())
            //    {
            //        WriteLine("Please provide path to C# file to be analyzed.");
            //    }
            //    else if (args.Contains("--help")) {
            //        WriteLine(@"
            //            Checks purity of C# source file.

            //            -s \t use this flag if input is the C# program as a string, rather than its filepath
            //        ");
            //    }
            //    else if (args.Contains("-s"))
            //    {
            //        //WriteLine("-s was used as flag");
            //        int textIndex = Array.IndexOf(args, "-s") + 1;
            //        if (textIndex < args.Length)
            //        {
            //            //WriteLine(args[textIndex]);
            //            string file = args[textIndex];
            //            WriteLine(Analyze(file));
            //        }
            //        else
            //        {
            //            WriteLine("Missing program string to be parsed as an argument.");
            //        }
            //    }
            //    else
            //    {
            //        try
            //        {
            //            string file = System.IO.File.ReadAllText(args[0]);
            //            WriteLine(Analyze(file));
            //        } catch (System.IO.FileNotFoundException err)
            //        {
            //            WriteLine(err.Message);
            //        } catch
            //        {
            //            WriteLine($"Something went wrong when reading the file {args[0]}");
            //        }
            //    }
        }
    }

    public class LookupTable
    {
        public DataTable table = new DataTable();

        public LookupTable()
        {
            table.Columns.Add("identifier", typeof(MethodDeclarationSyntax));
            table.Columns.Add("dependencies", typeof(List<MethodDeclarationSyntax>));
            table.Columns.Add("purity", typeof(Purity));
        }

        public void BuildLookupTable(CompilationUnitSyntax root, SemanticModel model)
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDeclaration in methodDeclarations)
            {
                var methodInvocations = methodDeclaration
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>();
                foreach (var methodInvocation in methodInvocations)
                {
                    // not sure if this cast from SyntaxNode to MethodDeclarationSyntax always works
                    MethodDeclarationSyntax methodNode = (MethodDeclarationSyntax)model
                        .GetSymbolInfo(methodInvocation)
                        .Symbol
                        .DeclaringSyntaxReferences
                        .Single()
                        .GetSyntax();
                    // TODO
                }
                AddMethod(methodDeclaration);
            }
        }

        public void AddDependency(MethodDeclarationSyntax methodNode, MethodDeclarationSyntax dependsOnNode)
        {
            AddMethod(methodNode);
            AddMethod(dependsOnNode);
            DataRow row = table
                .AsEnumerable()
                .Where(row => row.Field<MethodDeclarationSyntax>("identifier") == methodNode)
                .Single();
            List<MethodDeclarationSyntax> dependencyList = row.Field<List<MethodDeclarationSyntax>>("dependencies");
            if (!dependencyList.Contains(dependsOnNode))
            {
                dependencyList.Add(dependsOnNode);
            }
        }

        public void AddMethod(MethodDeclarationSyntax methodNode)
        {
            if (!HasMethod(methodNode))
            {
                table.Rows.Add(methodNode, new List<MethodDeclarationSyntax>(), Purity.Pure);
            }
        }

        public bool HasMethod(MethodDeclarationSyntax methodNode)
        {
            return table
                .AsEnumerable()
                .Any(row => row.Field<MethodDeclarationSyntax>("identifier") == methodNode);
        }

        public bool HasDependency(MethodDeclarationSyntax methodNode, MethodDeclarationSyntax dependsOnNode)
        {
            return table
                .AsEnumerable()
                .Any(row =>
                    row.Field<MethodDeclarationSyntax>("identifier") == methodNode &&
                    row.Field<List<MethodDeclarationSyntax>>("dependencies").Contains(dependsOnNode)
                );
        }

        public override string ToString()
        {
            string result = "";
            foreach (var row in table.AsEnumerable())
            {
                foreach (var item in row.ItemArray)
                {
                    if (item is MethodDeclarationSyntax)
                    {
                        result += ((MethodDeclarationSyntax)item).Identifier;
                    }
                    else if (item is List<MethodDeclarationSyntax>)
                    {
                        var dependencyList = (List<MethodDeclarationSyntax>)item;
                        foreach (var dependency in dependencyList)
                        {
                            result += dependency.Identifier;
                        }
                        result += ", ";
                    }
                    else
                    {
                        result += item;
                    }
                    result += " | ";
                }
                result += "\n";
            }
            return result;
        }
    }
}
