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
        private static void Main()
        {
            var tree = CSharpSyntaxTree.ParseText(@"
            class C1 {
                private int var1;
                public string var2;
 
                void action1(int parameter1, String parameter2)
                {
                    var o1 = new C1();
                    int res = add(parameter1, parameter2);
                    int var3;
                    var3=var1*var1;
                    var2=""Completed"";
                }

                int add(int left, int right) {
                    return left + right;
                }
            }
            ");
            var root = (CompilationUnitSyntax) tree.GetRoot();
            var variableDeclarations = root.DescendantNodes().OfType<VariableDeclarationSyntax>();

            Console.WriteLine("Declare variables:");

            foreach (var variableDeclaration in variableDeclarations)
                Console.WriteLine(variableDeclaration.Variables.First().Identifier.Value);

            var variableAssignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>();

            Console.WriteLine("Assign variables:");

            foreach (var variableAssignment in variableAssignments)
                Console.WriteLine($"Left: {variableAssignment.Left}, Right: {variableAssignment.Right}");

            foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                Console.WriteLine($"Member: {member}");
        }
    }
}
