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

        public static SemanticModel GetSemanticModel(SyntaxTree tree)
        {
            var model = CSharpCompilation.Create("assemblyName")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                 )
                .AddSyntaxTrees(tree)
                .GetSemanticModel(tree);
            return model;
        }

        static void Main(string[] args)
        {
            if (!args.Any())
            {
                WriteLine("Please provide path to C# file to be analyzed.");
            }
            else if (args.Contains("--help"))
            {
                WriteLine(@"
                    Checks purity of C# source file.

                    -s \t use this flag if input is the C# program as a string, rather than its filepath
                ");
            }
            else if (args.Contains("-s"))
            {
                //WriteLine("-s was used as flag");
                int textIndex = Array.IndexOf(args, "-s") + 1;
                if (textIndex < args.Length)
                {
                    //WriteLine(args[textIndex]);
                    string file = args[textIndex];
                    WriteLine(Analyze(file));
                }
                else
                {
                    WriteLine("Missing program string to be parsed as an argument.");
                }
            }
            else
            {
                try
                {
                    string file = System.IO.File.ReadAllText(args[0]);
                    WriteLine(Analyze(file));
                }
                catch (System.IO.FileNotFoundException err)
                {
                    WriteLine(err.Message);
                }
                catch
                {
                    WriteLine($"Something went wrong when reading the file {args[0]}");
                }
            }
        }

        public Boolean ReadsStaticField(MethodDeclarationSyntax method)
        {
            return false;
        }
    }

    public class LookupTable
    {
        public DataTable table = new DataTable();
        readonly CompilationUnitSyntax  root;
        readonly SemanticModel model;

        public LookupTable(CompilationUnitSyntax root, SemanticModel model)
        {
            this.root = root;
            this.model = model;

            table.Columns.Add("identifier", typeof(MethodDeclarationSyntax));
            table.Columns.Add("dependencies", typeof(List<MethodDeclarationSyntax>));
            table.Columns.Add("purity", typeof(Purity));
        }

        public void BuildLookupTable()
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDeclaration in methodDeclarations)
            {
                AddMethod(methodDeclaration);
                var dependencies = GetDependencies(methodDeclaration);
                foreach (var dependency in dependencies) {
                    AddDependency(methodDeclaration, dependency);
                }
            }
        }

        /// <summary>
        /// Returns the declaration of the method invoced by `methodInvocation`
        /// If no declaration is found, returns `null`
        /// </summary>
        public MethodDeclarationSyntax GetMethodDeclaration(InvocationExpressionSyntax methodInvocation)
        {
            // not sure if this cast from SyntaxNode to MethodDeclarationSyntax always works
            return (MethodDeclarationSyntax)model
                .GetSymbolInfo(methodInvocation)
                .Symbol
                ?.DeclaringSyntaxReferences
                .Single()
                .GetSyntax();
        }

        /// <summary>
        /// Recursively computes a list of all unique methods that a method
        /// depends on
        /// </summary>
        /// <param name="methodDeclaration">The method</param>
        /// <returns>
        /// A list of all *unique* MethodDeclarationSyntaxes that <paramref
        /// name="methodDeclaration"/> depends on. If any method's
        /// implementation was not found, that method is represented as null in
        /// the list.
        /// </returns>
        public List<MethodDeclarationSyntax> GetDependencies(MethodDeclarationSyntax methodDeclaration)
        {
            List<MethodDeclarationSyntax> results = new List<MethodDeclarationSyntax>();
            if (methodDeclaration == null)
            {
                results.Add(null); // if no method implementaiton was found,
                return results;    // add `null` to results as an indication
            };

            var methodInvocations = methodDeclaration
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();
            if (!methodInvocations.Any()) return results;
            foreach (var mi in methodInvocations)
            {
                MethodDeclarationSyntax miDeclaration = GetMethodDeclaration(mi);
                results.Add(miDeclaration);
                results = results.Union(GetDependencies(miDeclaration)).ToList();
            }
            return results;
        }


        /// <summary>
        /// Adds a dependency for a method to the lookup table
        /// </summary>
        /// <param name="methodNode">The method to add a dependency to</param>
        /// <param name="dependsOnNode">The method that methodNode depends on</param>
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

        public void RemoveDependency(MethodDeclarationSyntax methodNode, MethodDeclarationSyntax dependsOnNode)
        {
            if (!HasMethod(methodNode)) {
                throw new System.Exception(
                    $"Method '{methodNode.Identifier}' does not exist in lookup table"
                );
            }
            else if (!HasMethod(dependsOnNode)) {
                throw new System.Exception(
                    $"Method '{dependsOnNode.Identifier}' does not exist in lookup table"
                );
            }
            else if (!HasDependency(methodNode, dependsOnNode))
            {
                throw new System.Exception(
                    $"Method '{methodNode.Identifier}' does not depend on '{dependsOnNode.Identifier}'"
                );
            }
            DataRow row = table
                .AsEnumerable()
                .Where(row => row.Field<MethodDeclarationSyntax>("identifier") == methodNode)
                .Single();
            row.Field<List<MethodDeclarationSyntax>>("dependencies").Remove(dependsOnNode);
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

        /// <summary>
        /// Adds method to the lookup table if it is not already in the lookup
        /// table
        /// </summary>
        /// <param name="methodNode">The method to add</param>
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
                            if (dependency == null) result += "-";
                            else result += dependency.Identifier;
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
