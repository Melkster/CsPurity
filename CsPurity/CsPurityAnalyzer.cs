using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static System.Console;

namespace CsPurity
{
    public class CsPurityAnalyzer
    {
        public static bool Analyze(string text)
        {
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
                var identifierNames = methodDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>();
                foreach (var identifierName in identifierNames)
                {
                    var identifierSymbol = (VariableDeclaratorSyntax)model
                        .GetSymbolInfo(identifierName)
                        .Symbol
                        .DeclaringSyntaxReferences.Single() // TODO: look at all references
                        .GetSyntax();
                    var methodAncestors = identifierSymbol.Ancestors().OfType<MethodDeclarationSyntax>();
                    bool methodIsPure = false;

                    if (methodAncestors.Any()) methodIsPure = methodAncestors.First() == methodDeclaration;
                    return methodIsPure;
                }
            }
            return true;
        }

        static void Main(string[] args)
        {
            var file = (@"
                namespace TestApp
                {
                    class C1
                    {
                        string bar = ""subtract"";
                        string subtract()
                        {
                            return bar;
                        }

                        class C2
                        {
                            string add()
                            {
                                string foo = ""add"";
                                return foo;
                            }
                        }
                    }
                }
            ");

            Analyze(file);
        }
    }
}
