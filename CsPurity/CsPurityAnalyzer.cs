using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Data;

using static System.Console;
using System.Runtime.CompilerServices;

namespace CsPurity
{
    public enum Purity
    {
        Impure,
        ParametricallyImpure,
        Pure
    } // The order here matters as they are compared with `<`

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
            Analyzer analyzer = new Analyzer(text);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);
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
    }

    public class Analyzer
    {

        readonly public CompilationUnitSyntax root;
        readonly public SemanticModel model;
        readonly public LookupTable lookupTable;

        public Analyzer(string text)
        {
            var tree = CSharpSyntaxTree.ParseText(text);
            this.root = (CompilationUnitSyntax)tree.GetRoot();
            this.model = GetSemanticModel(tree);
            this.lookupTable = new LookupTable(root, model);
        }

        public static LookupTable Analyze(string text)
        {
            Analyzer analyzer = new Analyzer(text);
            LookupTable table = analyzer.lookupTable;
            WorkingSet workingSet = table.workingSet;
            bool tableModified = true;

            while (tableModified == true)
            {
                tableModified = false;

                foreach (var method in workingSet)
                {
                    // Perform checks:
                    if (analyzer.ReadsStaticFieldOrProperty(method))
                    {
                        table.SetPurity(method, Purity.Impure);
                        foreach (var caller in table.GetCallers(method))
                        {
                            table.SetPurity(caller, Purity.Impure);
                            table.RemoveDependency(caller, method);
                            tableModified = true;
                        }
                    }
                }
                workingSet.Calculate();
            }
            return table;
        }

        public bool ReadsStaticFieldOrProperty(MethodDeclarationSyntax method)
        {
            IEnumerable<MemberAccessExpressionSyntax> memberAccessExpressions = method
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>();
            foreach (var expression in memberAccessExpressions)
            {
                ISymbol symbol = model.GetSymbolInfo(expression).Symbol;
                bool isStatic = symbol.IsStatic;
                bool isField = symbol.Kind == SymbolKind.Field;
                bool isProperty = symbol.Kind == SymbolKind.Property;
                bool isMethod = symbol.Kind == SymbolKind.Method;
                if (isStatic && (isField || isProperty) && !isMethod) return true;
            }

            if (memberAccessExpressions.Any()) return false;

            IEnumerable<ExpressionStatementSyntax> expressionStatements = method
                .DescendantNodes()
                .OfType<ExpressionStatementSyntax>();

            foreach (var expression in expressionStatements)
            {
                //IdentifierNameSyntax identifier = expression
                var identifier = expression
                    .DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Single();

                ISymbol symbol = model.GetSymbolInfo(identifier).Symbol;
                bool isStatic = symbol.IsStatic;
                bool isField = symbol.Kind == SymbolKind.Field;
                bool isProperty = symbol.Kind == SymbolKind.Property;
                bool isMethod = symbol.Kind == SymbolKind.Method;
                if (isStatic && (isField || isProperty) && !isMethod) return true;
            }

            return false;
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
    }

    public class LookupTable
    {
        public DataTable table = new DataTable();
        public WorkingSet workingSet;
        readonly CompilationUnitSyntax root;
        readonly SemanticModel model;

        public LookupTable()
        {
            table.Columns.Add("identifier", typeof(MethodDeclarationSyntax));
            table.Columns.Add("dependencies", typeof(List<MethodDeclarationSyntax>));
            table.Columns.Add("purity", typeof(Purity));
        }

        public LookupTable(CompilationUnitSyntax root, SemanticModel model) : this()
        {
            this.root = root;
            this.model = model;

            BuildLookupTable();
            this.workingSet = new WorkingSet(this);
        }

        public void BuildLookupTable()
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDeclaration in methodDeclarations)
            {
                AddMethod(methodDeclaration);
                var dependencies = GetDependencies(methodDeclaration);
                foreach (var dependency in dependencies)
                {
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
                .Where(row => row["identifier"] == methodNode)
                .Single();
            List<MethodDeclarationSyntax> dependencies = row
                .Field<List<MethodDeclarationSyntax>>("dependencies");
            if (!dependencies.Contains(dependsOnNode))
            {
                dependencies.Add(dependsOnNode);
            }
        }

        public void RemoveDependency(MethodDeclarationSyntax methodNode, MethodDeclarationSyntax dependsOnNode)
        {
            if (!HasMethod(methodNode))
            {
                throw new System.Exception(
                    $"Method '{methodNode.Identifier}' does not exist in lookup table"
                );
            }
            else if (!HasMethod(dependsOnNode))
            {
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
                .Where(row => row["identifier"] == methodNode)
                .Single();
            row.Field<List<MethodDeclarationSyntax>>("dependencies").Remove(dependsOnNode);
        }

        public bool HasDependency(MethodDeclarationSyntax methodNode, MethodDeclarationSyntax dependsOnNode)
        {
            return table
                .AsEnumerable()
                .Any(row =>
                    row["identifier"] == methodNode &&
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
                .Any(row => row["identifier"] == methodNode);
        }

        public Purity GetPurity(MethodDeclarationSyntax method)
        {
            return (Purity)GetMethodRow(method)["purity"];
        }

        public void SetPurity(MethodDeclarationSyntax method, Purity purity)
        {
            GetMethodRow(method)["purity"] = purity;
        }

        DataRow GetMethodRow(MethodDeclarationSyntax method)
        {
            return table
                .AsEnumerable()
                .Where(row => row["identifier"] == method)
                .Single();
        }

        /// <summary>
        /// Gets all methods in the working set that are marked `Impure` in the
        /// lookup table.
        /// </summary>
        /// <param name="workingSet">The working set</param>
        /// <returns>
        /// All methods in <paramref name="workingSet"/> are marked `Impure`
        /// </returns>
        public List<MethodDeclarationSyntax> GetAllImpureMethods(List<MethodDeclarationSyntax> workingSet)
        {
            List<MethodDeclarationSyntax> impureMethods = new List<MethodDeclarationSyntax>();
            foreach (var method in workingSet)
            {
                if (GetPurity(method).Equals(Purity.Impure))
                {
                    impureMethods.Add(method);
                }
            }
            return impureMethods;
        }

        public List<MethodDeclarationSyntax> GetCallers(MethodDeclarationSyntax method)
        {
            List<MethodDeclarationSyntax> result = new List<MethodDeclarationSyntax>();
            foreach (var row in table.AsEnumerable())
            {
                List<MethodDeclarationSyntax> dependencies = row
                    .Field<List<MethodDeclarationSyntax>>("dependencies");
                if (dependencies.Contains(method))
                {
                    result.Add(row.Field<MethodDeclarationSyntax>("identifier"));
                }
            }
            return result;
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
                        var dependencies = (List<MethodDeclarationSyntax>)item;
                        foreach (var dependency in dependencies)
                        {
                            if (dependency == null) result += "-";
                            else result += dependency.Identifier;
                            result += ", ";
                        }
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

    public class WorkingSet : List<MethodDeclarationSyntax>
    {
        private readonly LookupTable lookupTable;
        private readonly List<MethodDeclarationSyntax> history =
            new List<MethodDeclarationSyntax>();
        public WorkingSet(LookupTable lookupTable)
        {
            this.lookupTable = lookupTable;
            Calculate();
        }

        public void Calculate()
        {
            this.Clear();

            foreach (var row in lookupTable.table.AsEnumerable())
            {
                MethodDeclarationSyntax identifier = row.Field<MethodDeclarationSyntax>("identifier");
                List<MethodDeclarationSyntax> dependencies = row
                    .Field<List<MethodDeclarationSyntax>>("dependencies");
                if (!dependencies.Any() && !history.Contains(identifier))
                {
                    this.Add(identifier);
                    history.Add(identifier);
                }
            }
        }
    }
}
