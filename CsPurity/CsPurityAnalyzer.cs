using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Diagnostics;

using static System.Console;

namespace CsPurity
{
    public enum Purity
    {
        Impure,
        ImpureThrowsException,
        Unknown,
        ParametricallyImpure,
        Pure
    } // The order here matters as they are compared with `<`

    public class Analyzer
    {
        readonly public LookupTable lookupTable;
        // Set this to `true` if enums should be considered to be impure.
        readonly public static bool enumsAreImpure = false;

        // All methods in the knownPurities are those that have an already
        // known purity level.
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
        /// <param name="file">The content of the file to analyze</param>
        /// <returns>A LookupTable containing each method in <paramref
        /// name="file"/>, its dependency set as well as its purity level
        /// </returns>
        public static LookupTable Analyze(List<string> files)
        {
            Analyzer analyzer = new Analyzer(files);
            LookupTable table = analyzer.lookupTable;
            WriteLine("Lookup table constructed. Calculating purity levels...");
            WorkingSet workingSet = table.workingSet;
            bool tableModified = true;

            while (tableModified == true)
            {
                tableModified = false;

                foreach (var method in workingSet)
                {
                    // Perform purity checks:

                    Purity currentPurity = table.GetPurity(method);

                    if (
                        currentPurity == Purity.Impure ||
                        currentPurity == Purity.ImpureThrowsException
                    )
                    // If the method's purity already is Impure we simply
                    // propagate it and move on. Checks for Unknown are done in
                    // a later check in this method.
                    {
                        PropagatePurity(method);
                    }
                    else if (PurityIsKnownPrior(method))
                    {
                        SetPurityAndPropagate(method, GetPriorKnownPurity(method));
                    }
                    else if (analyzer.ReadsStaticFieldOrProperty(method))
                    {
                        SetPurityAndPropagate(method, Purity.Impure);
                    }
                    else if (analyzer.ThrowsException(method))
                    {
                        SetPurityAndPropagate(method, Purity.ImpureThrowsException);
                    }
                    else if (table.GetPurity(method) == Purity.Unknown)
                    {
                        PropagatePurity(method);
                    }
                    else if (method.IsInterfaceMethod())
                    // If `method` is an interface method its purity is set to
                    // `Unknown` since we cannot know its implementation. This
                    // could be handled in the future by looking at all
                    // implementations of `method` and setting its purity level
                    // to the level of the impurest implementation.
                    {
                        SetPurityAndPropagate(method, Purity.Unknown);
                    }
                    else if (!method.HasBody())
                    {
                        SetPurityAndPropagate(method, Purity.Unknown);
                    }
                    else if (analyzer.ContainsUnknownIdentifier(method))
                    {
                        SetPurityAndPropagate(method, Purity.Unknown);
                    }
                    else
                    {
                        RemoveMethodFromCallers(method);
                    }
                }
                workingSet.Calculate();
            }
            return table;

            void PropagatePurity(Method method)
            {
                Purity purity = table.GetPurity(method);
                foreach (var caller in table.GetCallers(method))
                {
                    table.SetPurity(caller, purity);
                    table.RemoveDependency(caller, method);
                }
                tableModified = true;
            }

            /// <summary>
            /// Sets <paramref name="method"/>'s purity level to <paramref name="purity"/>.
            ///
            /// Sets <paramref name="tableModified"/> to true.
            /// </summary>
            void SetPurityAndPropagate(Method method, Purity purity)
            {
                table.SetPurity(method, purity);
                PropagatePurity(method);
                tableModified = true;
            }

            // Removes method from callers of method
            void RemoveMethodFromCallers(Method method)
            {
                foreach (var caller in table.GetCallers(method))
                {
                    table.RemoveDependency(caller, method);
                }
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
            return CSharpCompilation
                .Create("AnalysisModel")
                .AddReferences(
                    MetadataReference.CreateFromFile(
                        typeof(string).Assembly.Location
                    )
                )
                .AddSyntaxTrees(trees)
                .GetSemanticModel(tree);
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
            return PurityIsKnownPrior(method.identifier);
        }

        public static bool PurityIsKnownPrior(String methodIdentifier)
        {
            return knownPurities.Exists(m => m.Item1 == methodIdentifier);
        }

        /// <summary>
        /// Gets a list of all identifiers in a method. Excludes any
        /// identifiers found in an [Attribute].
        /// </summary>
        /// <param name="method">The method</param>
        /// <returns>All IdentifierNameSyntax's inside <paramref name="method"/></returns>
        IEnumerable<IdentifierNameSyntax> GetIdentifiers(Method method)
        {
            if (method.declaration == null) return new List<IdentifierNameSyntax>();

            IEnumerable<IdentifierNameSyntax> identifiers = method
                .declaration
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            // Ignore any identifiers found in an [Attribute]
            identifiers = identifiers.Where(
                i => !i.Ancestors().Where(
                    a => a.GetType() == typeof(AttributeListSyntax)
                ).Any()
            );

            return identifiers;
        }

        public bool ReadsStaticFieldOrProperty(Method method)
        {
            IEnumerable<IdentifierNameSyntax> identifiers = GetIdentifiers(method);

            foreach (var identifier in identifiers)
            {
                SemanticModel model = Analyzer.GetSemanticModel(
                    lookupTable.trees,
                    identifier.SyntaxTree.GetRoot().SyntaxTree
                );
                ISymbol symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol == null) break;

                // If enums are considered to be impure we exclude them from
                // the check, as they will be covered by `isStatic`
                bool isEnum = enumsAreImpure ? false : IsEnum(symbol);

                bool isStatic = symbol.IsStatic;
                bool isField = symbol.Kind == SymbolKind.Field;
                bool isProperty = symbol.Kind == SymbolKind.Property;
                bool isMethod = symbol.Kind == SymbolKind.Method;

                if (isStatic && (isField || isProperty) && !isMethod && !isEnum) return true;
            }
            return false;
        }


        /// <summary>
        /// Checks if the methods contains an identifier with an unknown
        /// implementation.
        /// </summary>
        /// <param name="method">The method to check</param>
        /// <returns>
        /// False if <paramref name="method"/> has a known implementation or if
        /// it contained in the `knownPurities` list of known purities,
        /// otherwise true.
        /// </returns>
        public bool ContainsUnknownIdentifier(Method method)
        {
            IEnumerable<IdentifierNameSyntax> identifiers = GetIdentifiers(method);

            foreach (var identifier in identifiers)
            {
                // If the identifier is a parameter it cannot count as unknown
                if (identifier.Parent.Kind() == SyntaxKind.Parameter) continue;

                SemanticModel model = Analyzer.GetSemanticModel(
                    lookupTable.trees,
                    identifier.SyntaxTree.GetRoot().SyntaxTree
                );
                ISymbol symbol = model.GetSymbolInfo(identifier).Symbol;

                if (symbol == null) {
                    // Check if the invocation that `symbol` is part of exists
                    // in `knownPurities`, otherwise it's an unknown identifier
                    var invocation = identifier
                        .Ancestors()
                        .OfType<InvocationExpressionSyntax>()
                        ?.FirstOrDefault()
                        ?.Expression
                        ?.ToString();
                    if (!PurityIsKnownPrior(invocation)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines if a symbol is an enumeration.
        /// </summary>
        /// <param name="symbol">
        /// The symbol to check whether or not it is an enumeration.
        /// </param>
        /// <returns>
        /// True if <paramref name="symbol"/> is of the type Enum, otherwise
        /// false.
        /// </returns>
        bool IsEnum(ISymbol symbol)
        {
            if (symbol.ContainingType == null) return false;
            else return symbol.ContainingType.TypeKind == TypeKind.Enum;
        }

        /// <summary>
        /// Determines if method throws an exception.
        ///
        /// Return true if <paramref name="method"/> throws an exception,
        /// otherwise false.
        /// </summary>
        public bool ThrowsException(Method method)
        {
            if (method.declaration == null) return false;

            IEnumerable<ThrowStatementSyntax> throws = method
                .declaration
                .DescendantNodes()
                .OfType<ThrowStatementSyntax>();
            return throws.Any();
        }

        public static void AnalyzeAndPrint(List<string> files)
        {
            AnalyzeAndPrint(files, false);
        }

        public static void AnalyzeAndPrint(List<string> files, bool pureAttributesOnly)
        {
            LookupTable lt = Analyze(files)
                .StripMethodsNotDeclaredInAnalyzedFiles()
                .StripInterfaceMethods();
            WriteLine(lt.ToStringNoDependencySet(pureAttributesOnly));
            WriteLine("Method purity ratios:");
            if (pureAttributesOnly)
            {
                lt.PrintPurityRatiosPureAttributesOnly();
            } else
            {
                lt.PrintPurityRatios();
            }
        }

        public static void AnalyzeAndPrint(string file)
        {
            AnalyzeAndPrint(file, false);
        }

        public static void AnalyzeAndPrint(string file, bool pureAttributesOnly)
        {
            AnalyzeAndPrint(new List<string> { file }, pureAttributesOnly);
        }

        static void Main(string[] args)
        {
            var watch = Stopwatch.StartNew();
            bool pureAttributesOnly = false;
            List<string> validFlags = new List<string>
            {
                "--help",
                "--h",
                "-h",
                "--string",
                "--files",
                "--pure-attribute"
            };
            IEnumerable<string> unrecognizedFlags = args
                .Where(a => a.Length > 2)
                .Where(a => a.Substring(2) == "--")
                .Where(a => !validFlags.Contains(a));

            if (args.Contains("--pure-attribute")) {
                pureAttributesOnly = true;
                args = args.Except(new string[] { "--pure-attribute" }).ToArray();
            }

            if (!args.Any())
            {
                WriteLine("Please provide path(s) to the directory of C# file(s) to be analyzed.");
            }
            else if (unrecognizedFlags.Any()) {
                WriteLine($"Unknown option: {unrecognizedFlags.First()}\n" +
                    $"Try using the flag --help for more information.");
            }
            else if (args.Contains("--help") || args.Contains("-h") || args.Contains("--h"))
            {
                WriteLine(
                    "Checks purity of C# source files in provided directory.\n\n" +

                    "Usage: cspurity [options] <path to directory>.\n\n" +

                    "Options:\n" +
                    "  --string\t\tProvide argument in the form of the content of one C# file as a string.\n" +
                    "  --files \t\tProvide arguments as the paths to individual files to be analyzed.\n" +
                    "  --pure-attribute \tOnly output purity of methods that have the [Pure] attribute."
                );
            }
            else if (args.Contains("--string"))
            {
                int flagIndex = Array.IndexOf(args, "--string") + 1;
                if (flagIndex < args.Length)
                {
                    string file = args[flagIndex];
                    AnalyzeAndPrint(file, pureAttributesOnly);
                }
                else
                {
                    WriteLine("Missing program string to be parsed as an argument.");
                }
            }
            else if (args.Contains("--files"))
            {
                try
                {
                    int flagIndex = Array.IndexOf(args, "--files") + 1;
                    List<string> files = args.Skip(flagIndex).Select(
                        a => File.ReadAllText(a)
                    ).ToList();

                    AnalyzeAndPrint(files, pureAttributesOnly);
                }
                catch (FileNotFoundException err)
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
                    ).Select(a => File.ReadAllText(a)).ToList();

                    AnalyzeAndPrint(files, pureAttributesOnly);
                }
                catch (FileNotFoundException err)
                {
                    WriteLine(err.Message);
                }
                catch (Exception err)
                {
                    WriteLine($"Something went wrong when reading the file(s)" +
                        $":\n\n{err.Message}");
                }
            }

            watch.Stop();
            var minutes = watch.Elapsed.Minutes;
            var seconds = watch.Elapsed.Seconds;
            WriteLine($"Time taken: {minutes} min, {seconds} sec");
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

        public LookupTable(List<SyntaxTree> trees) : this()
        {
            this.trees = trees;

            BuildLookupTable();
            workingSet = new WorkingSet(this);
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

                    // Ignore interface methods which also show up as
                    // MethodDeclarationSyntaxes
                    if (!method.IsInterfaceMethod())
                    {
                        AddMethod(method);
                        WriteLine($"Calculating dependencies for {method}.");
                        var dependencies = CalculateDependencies(method);
                        foreach (var dependency in dependencies)
                        {
                            AddDependency(method, dependency);
                        }
                    }
                }
            }
        }

        // This method is private since dependencies get removed after
        // calculating purities. See method CalculateDependencies().
        private List<Method> GetDependencies(Method method)
        {
            return (List<Method>)GetMethodRow(method)["dependencies"];
        }

        /// <summary>
        /// Computes a list of all unique methods that a method depends on. If
        /// any method doesn't have a known declaration, its purity level is
        /// set to `Unknown`. If an interface method invocation was found, the
        /// invoker's purity is set to `Unknown` since the invoked method could
        /// have any implementation.
        /// </summary>
        /// <param name="method">The method</param>
        /// <returns>
        /// A list of all unique Methods that <paramref name="method"/>
        /// depends on.
        /// </returns>
        public List<Method> CalculateDependencies(Method method)
        {
            List<Method> result = new List<Method>();

            SemanticModel model = Analyzer.GetSemanticModel(
                trees,
                method.GetRoot().SyntaxTree
            );

            // If the method is a delegate or local function we simply
            // ignore it
            if (method.isDelegateFunction || method.isLocalFunction)
            {
                return result;
            }

            // If the method doesn't have a known declaration we cannot
            // calculate its dependencies, and so we ignore it
            if (!method.HasKnownDeclaration())
            {
                AddMethod(method);
                SetPurity(method, Purity.Unknown);
                return result;
            }

            var methodInvocations = method
                .declaration
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();
            if (!methodInvocations.Any()) return result;

            model = Analyzer.GetSemanticModel(
                trees,
                method.GetRoot().SyntaxTree
            );

            foreach (var invocation in methodInvocations)
            {
                Method invoked = new Method(invocation, model);

                // Excludes delegate and local functions
                if (invoked.isLocalFunction || invoked.isDelegateFunction)
                {
                    continue;
                }

                // Handles recursive calls. Don't continue analyzing
                // invoked method if it is equal to `method` or if it is in
                // `invocations` (which means that it was called recursively)
                if (invoked.Equals(method))
                {
                    continue;
                }

                if (!result.Contains(invoked))
                {
                    result.Add(invoked);
                }
            }
            return result;
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
                throw new Exception(
                    $"Method '{methodNode}' does not exist in lookup table"
                );
            }
            else if (!HasMethod(dependsOnNode))
            {
                throw new Exception(
                    $"Method '{dependsOnNode}' does not exist in lookup table"
                );
            }
            else if (!HasDependency(methodNode, dependsOnNode))
            {
                throw new Exception(
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
                throw new Exception(
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


        /// <summary>
        /// Sets the purity of <paramref name="method"/> to <paramref
        /// name="purity"/> if <paramref name="purity"/> is less pure than
        /// <paramref name="method"/>'s previous purity.
        /// </summary>
        /// <param name="method">The method</param>
        /// <param name="purity">The new purity</param>
        public void SetPurity(Method method, Purity purity)
        {
            if (purity < GetPurity(method)) {
                GetMethodRow(method)["purity"] = purity;
            }
        }

        public LookupTable GetMethodsWithKnownPurities()
        {
            DataTable result = table
                .AsEnumerable()
                .Where(row => (Purity)row["purity"] != (Purity.Unknown))
                .CopyToDataTable();
            return new LookupTable(result, this);
        }

        public DataRow GetMethodRow(Method method)
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

        /// <summary>
        /// Removes all interface methods from the lookup table, i.e. methods
        /// declared in interfaces which therefore lack implementation.
        /// </summary>
        /// <returns>A lookup table stripped of all interface methods.</returns>
        public LookupTable StripInterfaceMethods()
        {
            LookupTable result = Copy();
            List<Method> interfaceMethods = result
                .table
                .AsEnumerable()
                .Where(row => row.Field<Method>("identifier").IsInterfaceMethod())
                .Select(row => row.Field<Method>("identifier"))
                .ToList();
            foreach (Method method in interfaceMethods)
            {
                result.RemoveMethod(method);
            }
            return result;
        }

        public int CountMethods()
        {
            return table.Rows.Count;
        }


        /// <summary>
        /// Counts the number of methods in the lookup table with the attribute
        /// [Pure], or without the [Pure] attribute.
        /// </summary>
        /// <param name="havePureAttribute">
        /// Determines if the methods should have the [Pure] attribute or not
        /// </param>
        /// <returns>
        /// The number of methods with the [Pure] attribute, if <paramref
        /// name="havePureAttribute"/> is true, otherwise the number of methods
        /// without the [Pure] attribute.
        /// </returns>
        public int CountMethods(bool havePureAttribute)
        {
            return table.AsEnumerable().Where(row =>
            {
                Method method = row.Field<Method>("identifier");
                return method.HasPureAttribute() && havePureAttribute ||
                    !method.HasPureAttribute() && !havePureAttribute;
            }).Count();
        }

        /// <summary>
        /// Counts the number of methods with a given purity level.
        /// </summary>
        /// <param name="purity">The purity level</param>
        /// <returns>
        /// The number of methods with the purity level <paramref
        /// name="purity"/>.
        /// </returns>
        public int CountMethodsWithPurity(Purity purity)
        {
            return table
                .AsEnumerable()
                .Where(row => row.Field<Purity>("purity") == (purity))
                .Count();
        }

        /// <summary>
        /// Counts the number of methods with a given purity level and only
        /// those either with, or without the [Pure] attribute.
        /// </summary>
        /// <param name="purity">The purity level</param>
        /// <param name="hasPureAttribute">
        /// Determines if the methods should have the [Pure] attribute or not
        /// </param>
        /// <returns>
        /// The number of methods with the purity level <paramref
        /// name="purity"/> and the [Pure] attribute if <paramref
        /// name="hasPureAttribute"/> is true, otherwise the number of methods
        /// with the purity level <paramref name="purity"/> but with no [Pure]
        /// attribute.
        /// </returns>
        public int CountMethodsWithPurity(Purity purity, bool hasPureAttribute)
        {
            return table.AsEnumerable().Where(row =>
            {
                bool hasPurity = row.Field<Purity>("purity") == purity;
                bool methodHasPureAttribute = row.Field<Method>("identifier")
                    .HasPureAttribute();

                return hasPurity && (
                    methodHasPureAttribute && hasPureAttribute ||
                    !methodHasPureAttribute && !hasPureAttribute
                );
            }).Count();
        }

        public int CountFalsePositives()
        {
            return CountMethodsWithPurity(Purity.Pure, false);
        }

        public int CountFalseNegatives()
        {
            return CountMethodsWithPurity(Purity.Impure, true);
        }

        /// <summary>
        /// Prints purity ratios.
        /// </summary>
        public void PrintPurityRatios()
        {
            int methodsCount = CountMethods();
            double impures = CountMethodsWithPurity(Purity.Impure)
                + CountMethodsWithPurity(Purity.ImpureThrowsException);
            double pures = CountMethodsWithPurity(Purity.Pure);
            double unknowns = CountMethodsWithPurity(Purity.Unknown);
            WriteLine(
                $"Impure: {impures}/{methodsCount}, Pure: {pures}/{methodsCount}, Unknown: {unknowns}/{methodsCount}"
            );
        }

        /// <summary>
        /// Prints purity ratios including only methods with the [Pure]
        /// attribute.
        /// </summary>
        public void PrintPurityRatiosPureAttributesOnly()
        {
            int methodsCount = CountMethods(true);
            double impures = CountMethodsWithPurity(Purity.Impure, true)
                + CountMethodsWithPurity(Purity.ImpureThrowsException, true);
            double pures = CountMethodsWithPurity(Purity.Pure, true);
            double unknowns = CountMethodsWithPurity(Purity.Unknown, true);
            WriteLine(
                $"Impure: {impures}/{methodsCount}, Pure: {pures}/{methodsCount}, Unknown: {unknowns}/{methodsCount}"
            );
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
                    else if (item is List<Method> methods)
                    {
                        List<string> resultList = new List<string>();
                        var dependencies = methods;
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
            return ToStringNoDependencySet(false);
        }

        /// <summary>
        /// Formats the lookup table as a string
        /// </summary>
        /// <param name="pureAttributeOnly">
        /// Determines if only [Pure]
        /// attributes should be included in the string
        /// </param>
        /// <returns>
        /// The lookup table formatted as a string. If <paramref
        /// name="pureAttributeOnly"/> is true, only methods with the [Pure]
        /// attribute are included in the string, otherwise all methods are
        /// included.
        /// </returns>
        public string ToStringNoDependencySet(bool pureAttributeOnly)
        {
            int printoutWidth = 80;
            string result = FormatTwoColumn("METHOD", "PURITY LEVEL")
                + new string('-', printoutWidth + 13)
                + "\n";
            foreach (var row in table.AsEnumerable())
            {
                Method identifierMethod = row.Field<Method>("identifier");
                string identifier = identifierMethod.ToString();
                string purity = row.Field<Purity>("purity").ToString();

                if (!pureAttributeOnly || pureAttributeOnly && identifierMethod.HasPureAttribute())
                {
                    result += FormatTwoColumn(identifier, purity);
                }
            }
            return result;

            string FormatTwoColumn(string identifier, string purity)
            {
                int spaceWidth;
                if (printoutWidth - identifier.Length <= 0) spaceWidth = 0;
                else spaceWidth = printoutWidth - identifier.Length;

                string spaces = new String(' ', spaceWidth);
                return $"{identifier} {spaces}{purity}\n";
            }
        }
    }

    public class Method
    {
        public string identifier;
        public MethodDeclarationSyntax declaration;
        public bool isLocalFunction = false;
        public bool isDelegateFunction = false;

        /// <summary>
        /// If <paramref name="methodInvocation"/>'s declaration was found <see
        /// cref="declaration"/> is set to that and  <see cref="identifier"/>
        /// set to null instead.
        ///
        /// If no declaration was found, <see cref="declaration"/> is set to
        /// null and <see cref="identifier"/> set to <paramref
        /// name="methodInvocation"/>'s identifier instead.
        ///
        /// If the method is a local function, i.e. declared inside a method,
        /// isLocalFunction is set to true, otherwise it is false.
        ///
        /// <param name="methodInvocation"></param>
        /// <param name="model"></param>
        public Method(InvocationExpressionSyntax methodInvocation, SemanticModel model)
        {
            ISymbol symbol = model.GetSymbolInfo(methodInvocation).Symbol;
            if (symbol == null)
            {
                SetIdentifier(methodInvocation);
                return;
            }

            var declaringReferences = symbol.DeclaringSyntaxReferences;
            var methodSymbol = (IMethodSymbol)symbol;
            if (declaringReferences.Length < 1)
            {
                SetIdentifier(methodInvocation);
            }
            else if (methodSymbol.MethodKind == MethodKind.LocalFunction)
            {
                // Handles local functions
                isLocalFunction = true;
                identifier = "*local function*";
            }
            else if (methodSymbol.MethodKind == MethodKind.DelegateInvoke)
            {
                // Handles delegates
                identifier = "*delegate invocation";
                isDelegateFunction = true;
            }
            else if (declaringReferences.Single().GetSyntax().Kind() == SyntaxKind.DelegateDeclaration)
            {
                // Handles the case of `BeginInvoke` and `EndInvoke`
                identifier = "*delegate invocation*";
                isDelegateFunction = true;
            }
            else
            {
                // Not sure if this cast from SyntaxNode to
                // `MethodDeclarationSyntax` always works
                declaration = (MethodDeclarationSyntax)declaringReferences
                    .Single()
                    .GetSyntax();
            }
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

        public SyntaxNode GetRoot()
        {
            return declaration?.SyntaxTree.GetRoot();
        }

        public bool HasEqualSyntaxTreeTo(Method method)
        {
            return GetRoot().Equals(method.GetRoot());
        }

        /// <summary>
        /// Checks if method is an interface method, ie a method declared
        /// inside an interface.
        /// </summary>
        /// <returns>
        /// True if method is an interace method, otherwise false.
        /// </returns>
        public bool IsInterfaceMethod()
        {
            if (declaration == null) return false;
            else return declaration
                .Parent
                .Kind()
                .Equals(SyntaxKind.InterfaceDeclaration);
        }

        /// <summary>
        /// Determines if method has a [Pure] attribute.
        /// </summary>
        /// <returns>
        /// True if method has a [Pure] attribute, otherwise false.
        /// </returns>
        public bool HasPureAttribute()
        {
            return declaration.DescendantNodes().OfType<AttributeListSyntax>().Where(
                attributeList => attributeList.Attributes.Where(
                    attribute => attribute.Name.ToString().ToLower() == "pure"
                ).Any()
            ).Any();
        }

        public bool HasBody()
        {
            return declaration?.Body != null;
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
            }
        }

        public override int GetHashCode()
        {
            if (HasKnownDeclaration()) return declaration.GetHashCode();
            else return identifier.GetHashCode();
        }

        public override string ToString()
        {
            if (!HasKnownDeclaration()) return identifier;

            var classAncestors = declaration
                .Ancestors()
                .OfType<ClassDeclarationSyntax>();

            if (classAncestors.Any())
            {
                SyntaxToken classIdentifier = classAncestors.First().Identifier;
                string className = classIdentifier.Text;
                string returnType = declaration.ReturnType.ToString();
                string methodName = declaration.Identifier.Text;
                string pureAttribute = HasPureAttribute() ? "[Pure] " : "";
                return $"{pureAttribute}{returnType} {className}.{methodName}";
            }

            // If no ancestor is a class declaration, look for struct
            // declarations
            var structAncestors = declaration
                .Ancestors()
                .OfType<StructDeclarationSyntax>();

            if (structAncestors.Any())
            {
                SyntaxToken structIdentifier = structAncestors.First().Identifier;
                string structName = structIdentifier.Text;
                string returnType = declaration.ReturnType.ToString();
                string methodName = declaration.Identifier.Text;
                return $"(struct) {returnType} {structName}.{methodName}";
            }
            return "*no identifier found*";
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
            Clear();

            foreach (var row in lookupTable.table.AsEnumerable())
            {
                Method identifier = row.Field<Method>("identifier");
                List<Method> dependencies = row.Field<List<Method>>("dependencies");
                if (!dependencies.Any() && !history.Contains(identifier))
                {
                    Add(identifier);
                    history.Add(identifier);
                }
            }
        }
    }
}
