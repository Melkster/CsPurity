// Example from https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis#syntax-walkers
using System;
using System.Linq;
using static System.Console;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SyntaxWalker
{
    class Program
    {
        const string programText =
            @"using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;

        namespace TopLevel
        {
            using Microsoft;
            using System.ComponentModel;

            namespace Child1
            {
                using Microsoft.Win32;
                using System.Runtime.InteropServices;

                class Foo { }
            }

            namespace Child2
            {
                using System.CodeDom;
                using Microsoft.CSharp;

                class Bar { }
            }
        }";

        static void Main(string[] args)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(programText);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            var collector = new UsingCollector();
            collector.Visit(root);
            foreach (var directive in collector.Usings)
            {
                WriteLine(directive.Name);
            }
        }
    }
}
