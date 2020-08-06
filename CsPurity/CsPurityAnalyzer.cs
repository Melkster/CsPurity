using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;

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
        readonly public LookupTable lookupTable;

        // All methods in the knownPurities are those that
        public static readonly List<(string, Purity)> knownPurities
            = new List<(string, Purity)>
        {
            ("Console.Read",                        Purity.Impure),
            ("Console.ReadLine",                    Purity.Impure),
            ("Console.ReadKey",                     Purity.Impure),
            ("DateTime.Now",                        Purity.Impure),
            ("DateTimeOffset",                      Purity.Impure),
            ("Random.Next",                         Purity.Impure),
            ("Guid.NewGuid",                        Purity.Impure),
            ("System.IO.Path.GetRandomFileName",    Purity.Impure),
            ("System.Threading.Thread.Start",       Purity.Impure),
            ("Thread.Abort",                        Purity.Impure),
            ("Console.Read",                        Purity.Impure),
            ("Console.ReadLine",                    Purity.Impure),
            ("Console.ReadKey",                     Purity.Impure),
            ("Console.Write",                       Purity.Impure),
            ("Console.WriteLine",                   Purity.Impure),
            ("System.IO.Directory.Create",          Purity.Impure),
            ("Directory.Move",                      Purity.Impure),
            ("Directory.Delete",                    Purity.Impure),
            ("File.Create",                         Purity.Impure),
            ("File.Move",                           Purity.Impure),
            ("File.Delete",                         Purity.Impure),
            ("File.ReadAllBytes",                   Purity.Impure),
            ("File.WriteAllBytes",                  Purity.Impure),
            ("System.Net.Http.HttpClient.GetAsync", Purity.Impure),
            ("HttpClient.PostAsync",                Purity.Impure),
            ("HttpClinet.PutAsync",                 Purity.Impure),
            ("HttpClient.DeleteAsync",              Purity.Impure),
            ("IDisposable.Dispose",                 Purity.Impure),
            ("List.IsCompatibleObject()",           Purity.Pure),
            ("List.Add()",                          Purity.Impure),
            ("List.EnsureCapacity()",               Purity.Impure),
            ("List.GetEnumerator()",                Purity.Pure),
            ("List.GetEnumerator()",                Purity.Pure),
            ("List.GetEnumerator()",                Purity.Pure),
            ("List.TrimExcess()",                   Purity.Pure),
            ("List.Synchronized()",                 Purity.Pure),
            ("SynchronizedList.Add()",              Purity.Impure),
            ("SynchronizedList.GetEnumerator()",    Purity.Pure),
            ("List.Dispose()",                      Purity.Pure),
        };

        public Analyzer(List<string> files)
        {
            var trees = files.Select(f => CSharpSyntaxTree.ParseText(f)).ToList();
            lookupTable = new LookupTable(trees);
        }

        public Analyzer(string file) : this(new List<string> { file }) { }

        /// <summary>
        /// Analyzes the purity of the given text.
        /// </summary>
        /// <param name="text"></param>
        /// <returns>A LookupTable containing each method in <paramref
        /// name="text"/>, its dependency set as well as its purity level
        /// </returns>
        public static LookupTable Analyze(List<string> files)
        {
            Analyzer analyzer = new Analyzer(files);
            LookupTable table = analyzer.lookupTable;
            WorkingSet workingSet = table.workingSet;
            bool tableModified = true;

            while (tableModified == true)
            {
                tableModified = false;

                foreach (var method in workingSet)
                {
                    // Perform purity checks:

                    if (PurityIsKnownPrior(method))
                    {
                        SetPurityAndPropagate(method, GetPriorKnownPurity(method));
                    }
                    else if (table.GetPurity(method) == Purity.Unknown)
                    {
                        SetPurityAndPropagate(method, Purity.Unknown);
                    }
                    else if (analyzer.ReadsStaticFieldOrProperty(method))
                    {
                        SetPurityAndPropagate(method, Purity.Impure);
                    }
                }
                workingSet.Calculate();
            }
            return table;

            /// <summary>
            /// Sets <paramref name="method"/>'s purity level to <paramref name="purity"/>.
            ///
            /// Sets <paramref name="tableModified"/> to true.
            /// </summary>
            void SetPurityAndPropagate(Method method, Purity purity) {
                table.SetPurity(method, purity);
                table.PropagatePurity(method);
                tableModified = true;
            }
        }

        public static LookupTable Analyze(string file)
        {
            return Analyze(new List<string> { file });
        }

        /// <summary>
        /// Builds the semantic model
        /// </summary>
        /// <param name="trees">
        /// All trees including <paramref name="tree"/>
        /// representing all files making up the program to analyze </param>
        /// <param name="tree">The </param>
        /// <returns></returns>
        public static SemanticModel GetSemanticModel(List<SyntaxTree> trees, SyntaxTree tree)
        {
            var result = CSharpCompilation
                .Create("AnalysisModel")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                )
                .AddSyntaxTrees(trees)
                .GetSemanticModel(tree);
            return result;
        }

        public static SemanticModel GetSemanticModel(SyntaxTree tree)
        {
            return GetSemanticModel(new List<SyntaxTree> { tree }, tree);
        }

        /// <summary>
        /// Returns the prior known purity level of <paramref name="method"/>.
        /// If the purity level of <paramref name="method"/> is not known
        /// prior, returns Purity.Unknown;
        /// </summary>
        public static Purity GetPriorKnownPurity(Method method)
        {
            if (!PurityIsKnownPrior(method)) return Purity.Unknown;
            else return knownPurities.Single(m => m.Item1 == method.identifier).Item2;
        }

        /// <summary>
        /// Determines if the purity of <paramref name="method"/> is known in
        /// beforehand.
        ///
        /// Return true if it is, otherwise false.
        /// </summary>
        public static bool PurityIsKnownPrior(Method method)
        {
            return knownPurities.Exists(m => m.Item1 == method.identifier);
        }

        public bool ReadsStaticFieldOrProperty(Method method)
        {
            IEnumerable<IdentifierNameSyntax> identifiers = method
                .declaration
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var identifier in identifiers)
            {
                SemanticModel model = Analyzer.GetSemanticModel(
                    lookupTable.trees,
                    identifier.SyntaxTree.GetRoot().SyntaxTree
                );
                ISymbol symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol == null) break;

                bool isStatic = symbol.IsStatic;
                bool isField = symbol.Kind == SymbolKind.Field;
                bool isProperty = symbol.Kind == SymbolKind.Property;
                bool isMethod = symbol.Kind == SymbolKind.Method;

                if (isStatic && (isField || isProperty) && !isMethod) return true;
            }
            return false;
        }

        static void Main(string[] args)
        {
            if (!args.Any())
            {
                WriteLine("Please provide path(s) to the directory C# file(s) to be analyzed.");
            }
            else if (args.Contains("--help"))
            {
                WriteLine(
                    "Checks purity of C# source files in provided directory.\n\n" +

                    "Options:\n" +
                    "  --string\tUse this flag if input is one C# file as a string.\n" +
                    "  --file  \tUse this flag if input is the path to each file to be analyzed."
                );
            }
            else if (args.Contains("--string"))
            {
                int textIndex = Array.IndexOf(args, "--string") + 1;
                if (textIndex < args.Length)
                {
                    string file = args[textIndex];
                    WriteLine(Analyze(file)
                        .StripMethodsNotDeclaredInAnalyzedFiles()
                        .ToStringNoDependencySet());
                }
                else
                {
                    WriteLine("Missing program string to be parsed as an argument.");
                }
            }
            else if (args.Contains("--file"))
            {
                try
                {
                    List<string> files = args.Skip(1).Select(
                        a => System.IO.File.ReadAllText(a)
                    ).ToList();

                    WriteLine(Analyze(files)
                        .StripMethodsNotDeclaredInAnalyzedFiles()
                        .ToStringNoDependencySet());
                }
                catch (System.IO.FileNotFoundException err)
                {
                    WriteLine(err.Message);
                }
                catch (Exception err)
                {
                    WriteLine($"Something went wrong when reading the file(s)" +
                        $":\n\n{err.Message}");
                }
            }
            else
            {
                try
                {
                    List<string> files = Directory.GetFiles(
                        args[0],
                        "*.cs",
                        SearchOption.AllDirectories
                    ).ToList();

                    WriteLine(Analyze(files)
                        .StripMethodsNotDeclaredInAnalyzedFiles()
                        .ToStringNoDependencySet());
                }
                catch (System.IO.FileNotFoundException err)
                {
                    WriteLine(err.Message);
                }
                catch (Exception err)
                {
                    WriteLine($"Something went wrong when reading the file(s)" +
                        $":\n\n{err.Message}");
                }
            }
        }
    }

    public class LookupTable
    {
        public DataTable table = new DataTable();
        public WorkingSet workingSet;
        public readonly List<SyntaxTree> trees;

        public LookupTable()
        {
            table.Columns.Add("identifier", typeof(Method));
            table.Columns.Add("dependencies", typeof(List<Method>));
            table.Columns.Add("purity", typeof(Purity));
        }

        public LookupTable(List<SyntaxTree> trees)
            : this()
        {
            this.trees = trees;

            BuildLookupTable();
            this.workingSet = new WorkingSet(this);
        }

        public LookupTable(SyntaxTree tree) : this(new List<SyntaxTree> { tree }) { }

        // Creates a LookupTable with the content of `table`
        public LookupTable(DataTable table, LookupTable lt)
        {
            this.trees = lt.trees;
            this.table = table.Copy();
        }

        public LookupTable Copy()
        {
            return new LookupTable(table, this);
        }

        /// <summary>
        /// Builds the lookup table and calculates each method's dependency
        /// set.
        /// </summary>
        public void BuildLookupTable()
        {
            foreach (var tree in trees)
            {
                var methodDeclarations = tree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();
                foreach (var methodDeclaration in methodDeclarations)
                {
                    Method method = new Method(methodDeclaration);
                    AddMethod(method);
                    var dependencies = CalculateDependencies(method);
                    foreach (var dependency in dependencies)
                    {
                        AddDependency(method, dependency);
                    }
                }
            }
        }

        public List<Method> GetDependencies(Method method)
        {
            return (List<Method>)GetMethodRow(method)["dependencies"];
        }

        /// <summary>
        /// Recursively computes a list of all unique methods that a method
        /// depends on. If any method doesn't have a known declaration, its
        /// purity level is set to `Unknown`.
        /// </summary>
        /// <param name="method">The method</param>
        /// <returns>
        /// A list of all *unique* MethodDeclarationSyntaxes that <paramref
        /// name="method"/> depends on. If <paramref name="method"/> lacks a
        /// known declaration, returns an empty list.
        /// </returns>
        public List<Method> CalculateDependencies(Method method)
        {
            List<Method> results = new List<Method>();

            // If `method` doesn't have a known declaration we cannot calculate
            // its dependencies
            if (!method.HasKnownDeclaration())
            {
                AddMethod(method);
                SetPurity(method, Purity.Unknown);
                return results;
            };

            var methodInvocations = method
                .declaration
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();
            if (!methodInvocations.Any()) return results;

            foreach (var invocation in methodInvocations)
            {
                // TODO: not sure if this is gonna work:
                SemanticModel model = Analyzer.GetSemanticModel(
                    trees,
                    invocation.SyntaxTree.GetRoot().SyntaxTree
                );
                results.Add(new Method(invocation, model));
                results = results.Union(
                    CalculateDependencies(new Method(invocation, model))
                ).ToList();
            }
            return results;
        }

        /// <summary>
        /// Adds a dependency for a method to the lookup table.
        /// </summary>
        /// <param name="method">The method to add a dependency to</param>
        /// <param name="dependsOnNode">The method that methodNode depends on</param>
        public void AddDependency(Method method, Method dependsOnNode)
        {
            AddMethod(method);
            AddMethod(dependsOnNode);
            DataRow row = table
                .AsEnumerable()
                .Where(row => row["identifier"].Equals(method))
                .Single();
            List<Method> dependencies = row
                .Field<List<Method>>("dependencies");
            if (!dependencies.Contains(dependsOnNode))
            {
                dependencies.Add(dependsOnNode);
            }
        }

        public void RemoveDependency(Method methodNode, Method dependsOnNode)
        {
            if (!HasMethod(methodNode))
            {
                throw new System.Exception(
                    $"Method '{methodNode}' does not exist in lookup table"
                );
            }
            else if (!HasMethod(dependsOnNode))
            {
                throw new System.Exception(
                    $"Method '{dependsOnNode}' does not exist in lookup table"
                );
            }
            else if (!HasDependency(methodNode, dependsOnNode))
            {
                throw new System.Exception(
                    $"Method '{methodNode}' does not depend on '{dependsOnNode}'"
                );
            }
            DataRow row = table
                .AsEnumerable()
                .Where(row => row["identifier"].Equals(methodNode))
                .Single();
            row.Field<List<Method>>("dependencies").Remove(dependsOnNode);
        }

        public bool HasDependency(Method method, Method dependsOn)
        {
            return table
                .AsEnumerable()
                .Any(row =>
                    row["identifier"].Equals(method) &&
                    row.Field<List<Method>>("dependencies").Contains(dependsOn)
                );
        }

        /// <summary>
        /// Adds method to the lookup table if it is not already in the lookup
        /// table
        /// </summary>
        /// <param name="methodNode">The method to add</param>
        public void AddMethod(Method methodNode)
        {
            if (!HasMethod(methodNode))
            {
                table.Rows.Add(methodNode, new List<Method>(), Purity.Pure);
            }
        }

        public void RemoveMethod(Method methodNode)
        {
            if (!HasMethod(methodNode))
            {
                throw new System.Exception(
                    $"Method '{methodNode}' does not exist in lookup table"
                );
            }
            else
            {
                table
                    .AsEnumerable()
                    .Where(row => row["identifier"].Equals(methodNode))
                    .Single()
                    .Delete();
            }
        }

        public bool HasMethod(Method methodNode)
        {
            return table
                .AsEnumerable()
                .Any(row => row["identifier"].Equals(methodNode));
        }

        public Purity GetPurity(Method method)
        {
            return (Purity)GetMethodRow(method)["purity"];
        }

        public void SetPurity(Method method, Purity purity)
        {
            GetMethodRow(method)["purity"] = purity;
        }

        public void PropagatePurity(Method method)
        {
            Purity purity = GetPurity(method);
            foreach (var caller in GetCallers(method))
            {
                SetPurity(caller, purity);
                RemoveDependency(caller, method);
            }
        }

        public LookupTable GetKnownPurities()
        {
            DataTable result = table
                .AsEnumerable()
                .Where(row => (Purity)row["purity"] != (Purity.Unknown))
                .CopyToDataTable();
            return new LookupTable(result, this);
        }

        DataRow GetMethodRow(Method method)
        {
            return table
                .AsEnumerable()
                .Where(row => row["identifier"].Equals(method))
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
        public List<Method> GetAllImpureMethods(List<Method> workingSet)
        {
            List<Method> impureMethods = new List<Method>();
            foreach (var method in workingSet)
            {
                if (GetPurity(method).Equals(Purity.Impure))
                {
                    impureMethods.Add(method);
                }
            }
            return impureMethods;
        }

        public List<Method> GetCallers(Method method)
        {
            List<Method> result = new List<Method>();
            foreach (var row in table.AsEnumerable())
            {
                List<Method> dependencies = row
                    .Field<List<Method>>("dependencies");
                if (dependencies.Contains(method))
                {
                    result.Add(row.Field<Method>("identifier"));
                }
            }
            return result;
        }


        /// <summary>
        /// Removes all methods in the lookup table that were not declared in
        /// any of the analyzed files.
        /// </summary>
        /// <returns>
        /// A new lookup table stripped of all methods who's declaration is not
        /// in any of the the syntax trees.
        /// </returns>
        public LookupTable StripMethodsNotDeclaredInAnalyzedFiles()
        {
            // TODO: write tests for this method
            LookupTable result = Copy();
            List<Method> methods = new List<Method>();
            foreach (var tree in trees)
            {
                var methodDeclarations = tree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();
                foreach (var methodDeclaration in methodDeclarations)
                {
                    methods.Add(new Method(methodDeclaration));
                }
            }
            foreach (var row in table.AsEnumerable())
            {
                var method = row.Field<Method>("identifier");
                if (!methods.Contains(method)) result.RemoveMethod(method);
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
                    if (item is Method method)
                    {
                        result += method;
                    }
                    else if (item is List<Method>)
                    {
                        List<string> resultList = new List<string>();
                        var dependencies = (List<Method>)item;
                        foreach (var dependency in dependencies)
                        {
                            if (dependency == null) resultList.Add("-");
                            else resultList.Add(dependency.ToString());
                        }
                        result += String.Join(", ", resultList);
                    }
                    else
                    {
                        result += item;
                    }
                    result += " | ";
                }
                result += "; \n";
            }
            return result;
        }

        public string ToStringNoDependencySet()
        {
            int printoutWidth = 80;
            string result = FormatTwoColumn("METHOD", "PURITY LEVEL")
                + new string('-', printoutWidth + 13)
                + "\n";
            foreach (var row in table.AsEnumerable())
            {
                string identifier = row.Field<Method>("identifier").ToString();
                var purity = row.Field<Purity>("purity");
                result += FormatTwoColumn(identifier, Enum.GetName(typeof(Purity), purity));
            }
            return result;

            string FormatTwoColumn(string item1, string item2)
            {
                int spaceWidth;
                if (printoutWidth - item1.Length <= 0) spaceWidth = 0;
                else spaceWidth = printoutWidth - item1.Length;

                string spaces = new String(' ', spaceWidth);
                return $"{item1} {spaces}{item2}\n";
            }
        }
    }


    public class Method
    {
        public string identifier;
        public MethodDeclarationSyntax declaration;

        /// <summary>
        /// If <paramref name="methodInvocation"/>'s declaration was found <see
        /// cref="declaration"/> is set to that and  <see cref="identifier"/>
        /// set to null instead.
        ///
        /// If no declaration was found, <see cref="declaration"/> is set to
        /// null and <see cref="identifier"/> set to <paramref
        /// name="methodInvocation"/>'s identifier instead.
        /// <param name="methodInvocation"></param>
        /// <param name="model"></param>
        public Method(InvocationExpressionSyntax methodInvocation, SemanticModel model)
        {

            ISymbol symbol = model.GetSymbolInfo(methodInvocation).Symbol;
            if (symbol == null)
            {
                SetIdentifier(methodInvocation);
                return;
            };

            var declaringReferences = symbol.DeclaringSyntaxReferences;
            if (declaringReferences.Length < 1)
            {
                SetIdentifier(methodInvocation);
                return;
            };

            // not sure if this cast from SyntaxNode to MethodDeclarationSyntax always works
            declaration = (MethodDeclarationSyntax)declaringReferences
                .Single()
                .GetSyntax();
        }

        public Method(MethodDeclarationSyntax declaration)
            : this(declaration.Identifier.Text)
        {
            this.declaration = declaration;
        }

        public Method(string identifier)
        {
            this.identifier = identifier;
        }

        void SetIdentifier(InvocationExpressionSyntax methodInvocation)
        {
            identifier = methodInvocation.Expression.ToString();
            identifier = Regex.Replace(identifier, @"[\s,\n]+", "");
        }

        public bool HasKnownDeclaration()
        {
            return declaration != null;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is Method)) return false;
            else
            {
                Method m = obj as Method;
                if (HasKnownDeclaration() && m.HasKnownDeclaration())
                {
                    return m.declaration == declaration;
                }
                else if (!HasKnownDeclaration() && !m.HasKnownDeclaration())
                {
                    return m.identifier == identifier;
                }
                else
                {
                    return false;
                }
            };
        }

        public override int GetHashCode()
        {
            if (HasKnownDeclaration()) return declaration.GetHashCode();
            else return identifier.GetHashCode();
        }

        public override string ToString()
        {
            if (HasKnownDeclaration()) {
                SyntaxToken classIdentifier = declaration
                    .Ancestors()
                    .OfType<ClassDeclarationSyntax>()
                    .First()
                    .Identifier;
                string className = classIdentifier.Text;
                string returnType = declaration.ReturnType.ToString();
                string methodName = declaration.Identifier.Text;
                return $"{returnType} {className}.{methodName}";
            }
            else return identifier;
        }
    }

    public class WorkingSet : List<Method>
    {
        private readonly LookupTable lookupTable;
        private readonly List<Method> history = new List<Method>();
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
                Method identifier = row.Field<Method>("identifier");
                List<Method> dependencies = row
                    .Field<List<Method>>("dependencies");
                if (!dependencies.Any() && !history.Contains(identifier))
                {
                    this.Add(identifier);
                    history.Add(identifier);
                }
            }
        }
    }
}
