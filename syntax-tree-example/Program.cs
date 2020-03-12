// Example from https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis#traversing-trees
using System;
using System.Linq;
using static System.Console;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace masters_thesis
{
    class Program
    {

        // TODO
        static void FindDeclaration(SyntaxNode variable, CompilationUnitSyntax root)
        {
            var fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>();

            foreach (var fieldDeclarator in fieldDeclarations)
                Console.WriteLine(fieldDeclarator.FirstAncestorOrSelf<VariableDeclaratorSyntax>());

            //if (fieldDeclarations.Where(fieldDeclaration => fieldDeclaration.Contains());
        }


        public static bool CompilationLookUpSymbols(SyntaxTree tree, CSharpSyntaxNode currentNode, string symbolToFind)
        {
            var compilation = CSharpCompilation.Create("dummy", new[] { tree });
            var model = compilation.GetSemanticModel(tree);
            var symbol = model.LookupSymbols(currentNode.SpanStart, name: symbolToFind);
            return model.LookupSymbols(currentNode.SpanStart, name: symbolToFind).Any();
        }

        private static void Main()
        {
            var tree = CSharpSyntaxTree.ParseText(@"
                using System;

                namespace TestApp
                {
                    class C1
                    {
                        int bar = 42;
                        void add()
                        {
                            bar = 3;
                            double x = 1 + 1;
                        }
                    }
                }
            ");
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var variableDeclarations = root.DescendantNodes().OfType<VariableDeclarationSyntax>();

            // foreach (var node in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            // Console.WriteLine(node.DescendantNodes(node => node.IsKind(IdentifierName));

            //FindDeclaration()

            // Console.WriteLine(root.DescendantNodes().ToList().Count());

            // foreach (var node in root.DescendantNodes())
            //    Console.WriteLine(node.GetType());

            // foreach (var foo in root.DescendantNodes().OfType<UsingDirectiveSyntax>())

            var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>();
            IdentifierNameSyntax foo = identifiers.First();
            //Console.WriteLine(foo);

            // Console.Write(CompilationLookUpSymbols(tree, f oo, "foo"));

            // Console.WriteLine("Declare variables:");

            // foreach (var variableDeclaration in variableDeclarations)
            //     Console.WriteLine(variableDeclaration.Variables.First().Identifier.Value);

            // var variableAssignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>();

            // Console.WriteLine("Assign variables:");
            // foreach (var variableAssignment in variableAssignments)
            //     Console.WriteLine($"Left: {variableAssignment.Left}, Right: {variableAssignment.Right}");

            // foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            //     Console.WriteLine($"Member: {member}");

            // ---

            var compilation = CSharpCompilation.Create("HelloWorld").AddReferences(MetadataReference.CreateFromFile(typeof(string).Assembly.Location)).AddSyntaxTrees(tree);
            SemanticModel model = compilation.GetSemanticModel(tree);

            // Use the syntax tree to find "using System;"
            UsingDirectiveSyntax usingSystem = root.Usings[0];
            NameSyntax systemName = usingSystem.Name;

            // Use the semantic model for symbol information:
            SymbolInfo nameInfo = model.GetSymbolInfo(systemName);

            Console.WriteLine(nameInfo);
        }
    }
}
