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
        Impure,
        Unknown,
        ParametricallyImpure,
        Pure
    } // The order here matters as they are compared with `<`

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

        /// <summary>
        /// Analyzes the purity of the given text.
        /// </summary>
        /// <param name="text"></param>
        /// <returns>A LookupTable containing each method in <paramref
        /// name="text"/>, its dependency set as well as its purity level
        /// </returns>
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

                    if (table.GetPurity(method) == Purity.Unknown)
                    {
                        table.SetPurity(method, Purity.Unknown);
                        table.PropagatePurity(method);
                        tableModified = true;
                    }
                    else if (analyzer.ReadsStaticFieldOrProperty(method))
                    {
                        table.SetPurity(method, Purity.Impure);
                        table.PropagatePurity(method);
                        tableModified = true;
                    }
                }
                workingSet.Calculate();
            }
            return table;
        }

        public bool ReadsStaticFieldOrProperty(MethodDeclarationSyntax method)
        {
            IEnumerable<IdentifierNameSyntax> identifiers = method
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var identifier in identifiers)
            {
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
                    string file = args[textIndex];
                    WriteLine(Analyze(file).ToStringNoDependencySet());
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
                    WriteLine(Analyze(file).ToStringNoDependencySet());
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

    public class LookupTable
    {
        public DataTable table = new DataTable();
        public WorkingSet workingSet;
        public readonly CompilationUnitSyntax root;
        public readonly SemanticModel model;

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


        /// <summary>
        /// Builds the lookup table and calculates each method's dependency
        /// set.
        ///
        /// Because unknown methods don't have a MethodDeclarationSyntax,
        /// unknown methods are discarded and their immediate callers' purity
        /// are set to Unknown.
        /// </summary>
        public void BuildLookupTable()
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDeclaration in methodDeclarations)
            {
                AddMethod(methodDeclaration);
                var dependencies = CalculateDependencies(methodDeclaration);
                foreach (var dependency in dependencies)
                {
                    if (dependency == null) SetPurity(methodDeclaration, Purity.Unknown);
                    else AddDependency(methodDeclaration, dependency);
                }
            }
        }

        /// <summary>
        /// Returns the declaration of the method invoced by `methodInvocation`
        /// If no declaration is found, returns `null`
        /// </summary>
        public MethodDeclarationSyntax GetMethodDeclaration(InvocationExpressionSyntax methodInvocation)
        {
            ISymbol symbol = model.GetSymbolInfo(methodInvocation).Symbol;
            if (symbol == null) return null;

            var declaringReferences = symbol.DeclaringSyntaxReferences;
            if (declaringReferences.Length < 1) return null;

            // not sure if this cast from SyntaxNode to MethodDeclarationSyntax always works
            return (MethodDeclarationSyntax)declaringReferences.Single().GetSyntax();
        }

        public List<MethodDeclarationSyntax> GetDependencies(MethodDeclarationSyntax method)
        {
            return (List<MethodDeclarationSyntax>)GetMethodRow(method)["dependencies"];
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
        public List<MethodDeclarationSyntax> CalculateDependencies(MethodDeclarationSyntax methodDeclaration)
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
                results = results.Union(CalculateDependencies(miDeclaration)).ToList();
            }
            return results;
        }

        /// <summary>
        /// Adds a dependency for a method to the lookup table.
        /// </summary>
        /// <param name="method">The method to add a dependency to</param>
        /// <param name="dependsOnNode">The method that methodNode depends on</param>
        public void AddDependency(MethodDeclarationSyntax method, MethodDeclarationSyntax dependsOnNode)
        {
            AddMethod(method);
            AddMethod(dependsOnNode);
            DataRow row = table
                .AsEnumerable()
                .Where(row => row["identifier"] == method)
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

        public void PropagatePurity(MethodDeclarationSyntax method)
        {
            Purity purity = GetPurity(method);
            foreach (var caller in GetCallers(method))
            {
                SetPurity(caller, purity);
                RemoveDependency(caller, method);
            }
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
                        List<string> resultList = new List<string>();
                        var dependencies = (List<MethodDeclarationSyntax>)item;
                        foreach (var dependency in dependencies)
                        {
                            if (dependency == null) resultList.Add("-");
                            else resultList.Add(dependency.Identifier.ToString());
                        }
                        result += String.Join(", ", resultList);
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

        public string ToStringNoDependencySet()
        {
            string result = "";
            foreach (var row in table.AsEnumerable())
            {
                var identifier = row.Field<MethodDeclarationSyntax>("identifier").Identifier;
                var purity = row.Field<Purity>("purity");
                result += identifier + ":\t" + Enum.GetName(typeof(Purity), purity) + "\n";
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


        /// <summary>
        /// Calculates the working set. The working set is the set of all
        /// methods in the lookup table that have empty dependency sets. A
        /// method can only be in the working set once, so if a method with
        /// empty dependency set has already been in the working set, it is not
        /// re-added.
        /// </summary>
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
