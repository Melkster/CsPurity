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

        public static DataTable InitializeLookupTable()
        {
            DataTable lookupTable = new DataTable();
            lookupTable.Columns.Add("identifier", typeof(string));
            lookupTable.Columns.Add("dependencies", typeof(List<string>));
            lookupTable.Columns.Add("purity", typeof(Purity));
            return lookupTable;
        }

        public static DataTable BuildLookupTable(CompilationUnitSyntax root)
        {
            DataTable lookupTable = InitializeLookupTable();
            lookupTable.Rows.Add("foo", new List<string> { "bar" }, Purity.Pure);
            lookupTable.Rows.Add("bar", new List<string>(), Purity.Pure);
            return lookupTable;
        }

        static void Main(string[] args)
        {
            if (!args.Any())
            {
                WriteLine("Please provide path to C# file to be analyzed.");
            }
            else if (args.Contains("--help")) {
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
                } catch (System.IO.FileNotFoundException err)
                {
                    WriteLine(err.Message);
                } catch
                {
                    WriteLine($"Something went wrong when reading the file {args[0]}");
                }
            }
        }
    }
}
