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
        // Set this to true if enums should be considered to be impure.
        readonly public static bool enumsAreImpure = false;
        // Determines if exceptions should be considered impure or not.
        public static bool exceptionsAreImpure = true;

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
            ("List.TrimExcess()",                   Purity.Pure),
            ("List.Synchronized()",                 Purity.Pure),
            ("SynchronizedList.Add()",              Purity.Impure),
            ("SynchronizedList.GetEnumerator()",    Purity.Pure),
            ("List.Dispose()",                      Purity.Pure),
        };

        public Analyzer(IEnumerable<string> files)
        {
            var trees = files
                .Select(f => CSharpSyntaxTree.ParseText(f))
                .ToList();
            lookupTable = new LookupTable(trees);
        }

        public Analyzer(string file) : this(new List<string> { file }) { }

        /// <summary>
        /// Analyzes the purity of the given text.
        /// </summary>
        /// <param name="file">The content of the file to analyze</param>
        /// <returns>
        /// A LookupTable containing each method in <paramref name="file"/>,
        /// its dependency set as well as its purity level. If
        /// <see cref="exceptionsAreImpure"/> is false then exceptions will be
        /// ignored.
        /// </returns>
        public static LookupTable Analyze(IEnumerable<string> files)
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
                    else if (exceptionsAreImpure && analyzer.ThrowsException(method))
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
        /// Builds a semantic model.
        /// </summary>
        /// <param name="trees">
        /// All trees including <paramref name="tree"/> representing all files
        /// making up the program to analyze
        /// </param>
        /// <param name="tree"></param>
        /// <returns></returns>
        public static SemanticModel GetSemanticModel(IEnumerable<SyntaxTree> trees, SyntaxTree tree)
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
        /// implementation. Exceptions are not considered unknown.
        /// </summary>
        /// <param name="method">The method to check</param>
        /// <returns>
        /// False if <paramref name="method"/> has a known implementation or if
        /// it contained in the <see cref="knownPurities"/> list of known
        /// purities, otherwise true.
        /// </returns>
        public bool ContainsUnknownIdentifier(Method method)
        {
            IEnumerable<IdentifierNameSyntax> identifiers = GetIdentifiers(method);

            foreach (var identifier in identifiers)
            {
                // If the identifier is a parameter it cannot count as unknown
                if (identifier.Parent.Kind() == SyntaxKind.Parameter) continue;

                // Exceptions are not considered unknown
                if (isException(identifier)) continue;

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

            static bool isException(IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text == "Exception"
                    && identifier.Parent.Kind() == SyntaxKind.ObjectCreationExpression;
            }
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

        static void AnalyzeAndPrintEvaluate(IEnumerable<string> files)
        {
            LookupTable lt = Analyze(files)
                .StripMethodsNotDeclaredInAnalyzedFiles()
                .StripInterfaceMethods();

            WriteLine();
            WriteLine($"Methods with [Pure] attribute:");
            WriteLine();
            WriteLine($"  Pure: {lt.CountMethodsWithPurity(Purity.Pure, true)}");
            WriteLine($"  Impure");
            WriteLine($"    - Throws exception: " +
                lt.CountMethodsWithPurity(Purity.ImpureThrowsException, true));
            WriteLine($"    - Other: {lt.CountMethodsWithPurity(Purity.Impure, true)}");
            WriteLine($"  Unknown: {lt.CountMethodsWithPurity(Purity.Unknown, true)}");
            WriteLine($"  Total: {lt.CountMethods(true)}");
            WriteLine();
            WriteLine($"Methods without [Pure] attribute:");
            WriteLine();
            WriteLine($"  Pure: {lt.CountMethodsWithPurity(Purity.Pure, false)}");
            WriteLine($"  Impure: " + lt.CountMethodsWithPurity(
                new Purity[] {Purity.Impure, Purity.ImpureThrowsException}, false)
            );
            WriteLine($"  Unknown: {lt.CountMethodsWithPurity(Purity.Unknown, false)}");
            WriteLine($"  Total: {lt.CountMethods(false)}");
            WriteLine();
            WriteLine($"Total number of methods: {lt.CountMethods()}");
        }

        static void AnalyzeAndPrintEvaluateExceptionsArePure(IEnumerable<string> files)
        {
            LookupTable lt = Analyze(files)
                .StripMethodsNotDeclaredInAnalyzedFiles()
                .StripInterfaceMethods();

            WriteLine();
            WriteLine($"Methods with [Pure] attribute:");
            WriteLine();
            WriteLine($"  Pure: {lt.CountMethodsWithPurity(Purity.Pure, true)}");
            WriteLine($"  Impure: {lt.CountMethodsWithPurity(Purity.Impure, true)}");
            WriteLine($"  Unknown: {lt.CountMethodsWithPurity(Purity.Unknown, true)}");
            WriteLine($"  Total: {lt.CountMethods(true)}");
            WriteLine();
            WriteLine($"Methods without [Pure] attribute:");
            WriteLine();
            WriteLine($"  Pure: {lt.CountMethodsWithPurity(Purity.Pure, false)}");
            WriteLine($"  Impure: " + lt.CountMethodsWithPurity(Purity.Impure, false));
            WriteLine($"  Unknown: {lt.CountMethodsWithPurity(Purity.Unknown, false)}");
            WriteLine($"  Total: {lt.CountMethods(false)}");
            WriteLine();
            WriteLine($"Total number of methods: {lt.CountMethods()}");
        }

        public static void AnalyzeAndPrint(IEnumerable<string> files, bool pureAttributesOnly)
        {
            LookupTable lt = Analyze(files)
                .StripMethodsNotDeclaredInAnalyzedFiles()
                .StripInterfaceMethods();
            WriteLine(lt.ToStringNoDependencySet(pureAttributesOnly));
            WriteLine("Method purity ratios:");
            if (!exceptionsAreImpure)
            {
                WriteLine(lt.GetFalsePositivesAndNegativesExceptionsArePure());
            }
            else if (pureAttributesOnly)
            {
                WriteLine(lt.GetPurityRatiosPureAttributesOnly());
            }
            else
            {
                WriteLine(lt.GetPurityRatios());
                WriteLine(lt.GetFalsePositivesAndNegatives());
            }
        }

        public static void AnalyzeAndPrint(
            IEnumerable<string> files,
            bool pureAttributesOnly,
            bool evaluate
        )
        {
            if (evaluate) {
                if (exceptionsAreImpure) AnalyzeAndPrintEvaluate(files);
                else AnalyzeAndPrintEvaluateExceptionsArePure(files);
            }
            else AnalyzeAndPrint(files, pureAttributesOnly);
        }

        public static void AnalyzeAndPrint(string file, bool pureAttributesOnly, bool evaluate)
        {
            AnalyzeAndPrint(new List<string> { file }, pureAttributesOnly, evaluate);
        }

        delegate void FlagHandlerCallback();


        /// <summary>
        /// If <paramref name="args"/> contains <paramref name="flag"/>
        /// <paramref name="callback"/> is run and <paramref name="flag"/> is
        /// removed from <paramref name="args"/>.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <param name="flag">Flag, including leading `--`</param>
        /// <param name="callback">
        /// Function to be called if <paramref name="args"/> contain
        /// <paramref name="flag"/>
        /// </param>
        /// <returns>
        /// <paramref name="args"/> with <paramref name="flag"/> removed
        /// </returns>
        static string[] FlagHandler(string[] args, string flag, FlagHandlerCallback callback)
        {
            if (args.Contains(flag)) {
                callback();
                args = args.Except(new string[] { flag }).ToArray();
            }
            return args;
        }

        static void Main(string[] args)
        {
            var watch = Stopwatch.StartNew();
            bool pureAttributesOnly = false;
            bool evaluate = false;
            List<string> validFlags = new List<string>
            {
                "--help",
                "--h",
                "-h",
                "--string",
                "--files",
                "--pure-attribute",
                "--evaluate",
                "--ignore-exceptions"
            };
            IEnumerable<string> unrecognizedFlags = args
                .Where(a => a.Length > 2)
                .Where(a => a[2..] == "--")
                .Where(a => !validFlags.Contains(a));

            args = FlagHandler(
                args, "--pure-attribute", () => pureAttributesOnly = true
            );
            args = FlagHandler(
                args, "--evaluate", () => evaluate = true
            );
            args = FlagHandler(
                args, "--ignore-exceptions", () => exceptionsAreImpure = false
            );

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
                    "  --files\t\tProvide arguments as the paths to individual files to be analyzed.\n" +
                    "  --pure-attribute\tOnly output purity of methods that have the [Pure] attribute.\n" +
                    "  --evaluate\t\tEvaluate the implementaiton by outputting in terms of true \n" +
                    "            \t\tand false negatives based on the [Pure] attribute."
                );
            }
            else if (args.Contains("--string"))
            {
                int flagIndex = Array.IndexOf(args, "--string") + 1;
                if (flagIndex < args.Length)
                {
                    string file = args[flagIndex];
                    AnalyzeAndPrint(file, pureAttributesOnly, evaluate);
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
                    IEnumerable<string> files = args.Skip(flagIndex).Select(
                        a => File.ReadAllText(a)
                    );

                    AnalyzeAndPrint(files, pureAttributesOnly, evaluate);
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
                     IEnumerable<string> files = args.Select(arg =>
                         Directory.GetFiles(
                            arg,
                            "*.cs",
                            SearchOption.AllDirectories
                        )
                     ).SelectMany(files => files)
                     .Select(a => File.ReadAllText(a));

                    AnalyzeAndPrint(files, pureAttributesOnly, evaluate);
                }
                catch (FileNotFoundException err)
                {
                    WriteLine(err.Message);
                }
                catch (Exception err)
                {
                    WriteLine($"Something went wrong when reading the file(s)" +
                        $":\n\n{err}");
                }
            }

            watch.Stop();
            var hours = watch.Elapsed.Hours;
            var minutes = watch.Elapsed.Minutes;
            var seconds = watch.Elapsed.Seconds;
            string hoursText = hours > 0 ? $"{hours} hours, " : "";

            WriteLine($"\nTime taken: {hoursText}{minutes} min, {seconds} sec");
        }
    }

    public class LookupTable
    {
        public DataTable table = new DataTable();
        public WorkingSet workingSet;
        public readonly IEnumerable<SyntaxTree> trees;

        public LookupTable()
        {
            table.Columns.Add("identifier", typeof(Method));
            table.Columns.Add("dependencies", typeof(IEnumerable<Method>));
            table.Columns.Add("purity", typeof(Purity));
        }

        public LookupTable(IEnumerable<SyntaxTree> trees) : this()
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
                        WriteLine($"Calculating dependencies for {method}.");
                        var dependencies = CalculateDependencies(method);
                        AddMethod(method);
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
        private IEnumerable<Method> GetDependencies(Method method)
        {
            return GetMethodRow(method).Field<IEnumerable<Method>>("dependencies");
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
        public IEnumerable<Method> CalculateDependencies(Method method)
        {
            // If the dependencies have already been computed, return them
            if (HasMethod(method) && GetDependencies(method).Any())
            {
                return GetDependencies(method);
            }

            Stack<Method> result = new Stack<Method>();
            SemanticModel model = Analyzer.GetSemanticModel(
                trees,
                method.GetRoot().SyntaxTree
            );

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

            foreach (var invocation in methodInvocations.Distinct())
            {
                Method invoked = new Method(invocation, model);

                if (invoked.isLocalFunction || invoked.isDelegateFunction)
                {
                    // Excludes delegate and local functions
                    continue;
                }
                else if (invoked.Equals(method))
                {
                    // Handles recursive calls. Don't continue analyzing
                    // invoked method if it is equal to the one being analyzed
                    continue;
                }
                else result.Push(invoked);
            }
            return result.Distinct();
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
                    row.Field<IEnumerable<Method>>("dependencies").Contains(dependsOn)
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
        /// Gets all methods from a list that are marked `Impure` in the lookup
        /// table.
        /// </summary>
        /// <param name="methods">The list of methods</param>
        /// <returns>
        /// All methods in <paramref name="methods"/> are marked `Impure`
        /// </returns>
        public IEnumerable<Method> GetAllImpureMethods(IEnumerable<Method> methods)
        {
            return methods.Where(m => GetPurity(m).Equals(Purity.Impure));
        }

        /// <summary>
        /// Gets all callers to a given method, i.e. that depend on it.
        /// </summary>
        /// <param name="method">The method</param>
        /// <returns>
        /// All methods that depend on <paramref name="method"/>.
        /// </returns>
        public IEnumerable<Method> GetCallers(Method method)
        {
            return table.AsEnumerable().Where(
                r => r.Field<IEnumerable<Method>>("dependencies").Contains(method)
            ).Select(r => r.Field<Method>("identifier"));
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
            return GetMethodsWithPurity(purity, hasPureAttribute).Count();
        }

        public int CountMethodsWithPurity(Purity[] purities, bool hasPureAttribute)
        {
            return GetMethodsWithPurity(purities, hasPureAttribute).Count();
        }

        public int CountFalsePositives()
        {
            return CountMethodsWithPurity(Purity.Pure, false);
        }

        public int CountFalseNegatives()
        {
            return CountMethodsWithPurity(
                new Purity[] { Purity.Impure, Purity.ImpureThrowsException },
                true
            );
        }

        public IEnumerable<Method> GetMethodsWithPurity(Purity purity, bool hasPureAttribute)
        {
            return GetMethodsWithPurity(new Purity[] { purity }, hasPureAttribute);
        }

        public IEnumerable<Method> GetMethodsWithPurity(Purity[] purities, bool hasPureAttribute)
        {
            return table.AsEnumerable().Where(row =>
            {
                bool hasPurity = purities.Contains(row.Field<Purity>("purity"));
                bool methodHasPureAttribute = row.Field<Method>("identifier")
                    .HasPureAttribute();

                return hasPurity && (
                    methodHasPureAttribute && hasPureAttribute ||
                    !methodHasPureAttribute && !hasPureAttribute
                );
            }).Select(r => r.Field<Method>("identifier"));
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

        /// <summary>
        /// Formats purity ratios into a string.
        /// </summary>
        /// <returns>
        /// Purity ratios formatted into a string.
        /// </returns>
        public string GetPurityRatios()
        {
            int methodsCount = CountMethods();
            double impures = CountMethodsWithPurity(Purity.Impure)
                + CountMethodsWithPurity(Purity.ImpureThrowsException);
            double pures = CountMethodsWithPurity(Purity.Pure);
            double unknowns = CountMethodsWithPurity(Purity.Unknown);

            return $"Impure: {impures}/{methodsCount}, Pure: {pures}/" +
                $"{methodsCount}, Unknown: {unknowns}/{methodsCount}";
        }

        public string GetFalsePositivesAndNegatives()
        {
            int throwExceptionCount = CountMethodsWithPurity(Purity.ImpureThrowsException, true);
            int otherImpuresCount = CountMethodsWithPurity(Purity.Impure, true);
            var falseNegatives = GetMethodsWithPurity(
                new Purity[] { Purity.Impure, Purity.ImpureThrowsException }, true
            );
            var falsePositives = GetMethodsWithPurity(Purity.Pure, false);

            string falseNegativesText = falseNegatives.Any() ?
                $"These methods were classified as impure (false negatives):\n\n" +
                string.Join("\n", falseNegatives.Select(m => "  " + m)) + $"\n\n"
                : "False negatives:\n";
            string falsePositivesText = falsePositives.Any() ?
                $"These methods were classified as pure (false positives):\n\n" +
                string.Join("\n", falsePositives.Select(m => "  " + m)) + $"\n\n"
                : "False positives:\n";

            return "\n" + falseNegativesText +

                $"  Amount: {CountFalseNegatives()}\n" +
                $"   - Throw exceptions: {throwExceptionCount}\n" +
                $"   - Other: {otherImpuresCount}\n\n" +

                falsePositivesText +

                $"  Amount: {CountFalsePositives()}";
        }

        public string GetFalsePositivesAndNegativesExceptionsArePure()
        {
            int impuresCount = CountMethodsWithPurity(Purity.Impure, true);
            var falseNegatives = GetMethodsWithPurity(Purity.Impure, true);
            var falsePositives = GetMethodsWithPurity(Purity.Pure, false);

            string falseNegativesText = falseNegatives.Any() ?
                $"These methods were classified as impure (false negatives):\n\n" +
                string.Join("\n", falseNegatives.Select(m => "  " + m)) + $"\n\n"
                : "False negatives:\n";
            string falsePositivesText = falsePositives.Any() ?
                $"These methods were classified as pure (false positives):\n\n" +
                string.Join("\n", falsePositives.Select(m => "  " + m)) + $"\n\n"
                : "False positives:\n";

            return "\n" + falseNegativesText +

                $"  Amount: {impuresCount}\n\n" +

                falsePositivesText +

                $"  Amount: {CountFalsePositives()}";
        }

        public static string FormatListLinewise<T>(IEnumerable<T> items)
        {
            return string.Join("\n", items);
        }

        /// <summary>
        /// Formats purity ratios into a string, including only methods with
        /// the [Pure] attribute.
        /// </summary>
        /// <returns>
        /// Purity ratios formatted into a string, including only methods with
        /// the [Pure] attribute.
        /// </returns>
        public string GetPurityRatiosPureAttributesOnly()
        {
            int methodsCount = CountMethods(true);
            double impures = CountMethodsWithPurity(Purity.Impure, true)
                + CountMethodsWithPurity(Purity.ImpureThrowsException, true);
            double pures = CountMethodsWithPurity(Purity.Pure, true);
            double unknowns = CountMethodsWithPurity(Purity.Unknown, true);
            return $"Impure: {impures}/{methodsCount}, Pure: " +
                $"{pures}/{methodsCount}, Unknown: {unknowns}/{methodsCount}";
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
                    else if (item is IEnumerable<Method> methods)
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
            else if (
                methodSymbol.MethodKind == MethodKind.DelegateInvoke ||
                declaringReferences.Single().GetSyntax().Kind() == SyntaxKind.DelegateDeclaration
            )
            {
                // Handles delegates, including the case of the methods
                // BeginInvoke and EndInvoke
                identifier = "*delegate invocation";
                isDelegateFunction = true;
            }
            else if (
                declaringReferences.Single().GetSyntax().Kind()
                    == SyntaxKind.ConversionOperatorDeclaration
            )
            {
                // Handles the rare case where GetSyntax() returns the operator
                // for an implicit conversion instead of the invoked method
                identifier = "*conversion operator*";
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

            string returnType = declaration.ReturnType.ToString();
            string methodName = declaration.Identifier.Text;
            var classAncestors = declaration
                .Ancestors()
                .OfType<ClassDeclarationSyntax>();

            if (classAncestors.Any())
            {
                SyntaxToken classIdentifier = classAncestors.First().Identifier;
                string className = classIdentifier.Text;
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
                string structName = structAncestors.First().Identifier.Text;
                string pureAttribute = HasPureAttribute() ? "[Pure] " : "";
                return $"(struct) {pureAttribute}{returnType} {structName}.{methodName}";
            }
            else
            {
                return $"{returnType} *no class/identifier* {methodName}";
            }
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
                IEnumerable<Method> dependencies = row.Field<IEnumerable<Method>>("dependencies");
                if (!dependencies.Any() && !history.Contains(identifier))
                {
                    Add(identifier);
                    history.Add(identifier);
                }
            }
        }
    }
}
