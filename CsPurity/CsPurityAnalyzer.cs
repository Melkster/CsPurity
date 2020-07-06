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

        // All methods in the blacklist are those that
        public static readonly List<string> blacklist = new List<string>
        {
            "Console.Read",
            "Console.ReadLine",
            "Console.ReadKey",
            "DateTime.Now",
            "DateTimeOffset",
            "Random.Next",
            "Guid.NewGuid",
            "System.IO.Path.GetRandomFileName",
            "System.Threading.Thread.Start",
            "Thread.Abort",
            "Console.Read",
            "Console.ReadLine",
            "Console-ReadKey",
            "Console.Write",
            "Console.WriteLin",
            "System.IO.Directory.Create",
            "Directory.Move",
            "Directory.Delete",
            "File.Create",
            "File.Move",
            "File.Delete",
            "File.ReadAllBytes",
            "File.WriteAllBytes",
            "System.Net.Http.HttpClient.GetAsync",
            "HttpClient.PostAsync",
            "HttpClinet.PutAsync",
            "HttpClient.DeleteAsync",
            "IDisposable.Dispose"
        };

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

                    if (IsBlackListed(method))
                    {
                        table.SetPurity(method, Purity.Impure);
                        table.PropagatePurity(method);
                        tableModified = true;
                    }
                    else if (table.GetPurity(method) == Purity.Unknown)
                    {
                        table.SetPurity(method, Purity.Unknown);
                        table.PropagatePurity(method);
                        tableModified = true;
                    }
                    else if (method.ReadsStaticFieldOrProperty())
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


        /// <summary>
        /// Checks if the method is blacklisted.
        ///
        /// A method is blacklisted if its implementation is unknown but its
        /// purity level is known to be `Impure` in beforehand.
        /// </summary>
        public static bool IsBlackListed(Method method)
        {
            return blacklist.Contains(method.identifier);
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
            table.Columns.Add("identifier", typeof(Method));
            table.Columns.Add("dependencies", typeof(List<Method>));
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
        /// </summary>
        public void BuildLookupTable()
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDeclaration in methodDeclarations)
            {
                Method method = new Method(methodDeclaration, model);
                AddMethod(method);
                var dependencies = CalculateDependencies(method);
                foreach (var dependency in dependencies)
                {
                    AddDependency(method, dependency);
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
                results.Add(new Method(invocation, model));
                results = results.Union(CalculateDependencies(new Method(invocation, model))).ToList();
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

        public override string ToString()
        {
            string result = "";
            foreach (var row in table.AsEnumerable())
            {
                foreach (var item in row.ItemArray)
                {
                    if (item is Method)
                    {
                        result += ((Method)item);
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
            string result = "";
            foreach (var row in table.AsEnumerable())
            {
                var identifier = row.Field<Method>("identifier");
                var purity = row.Field<Purity>("purity");
                result += identifier + ":\t" + Enum.GetName(typeof(Purity), purity) + "\n";
            }
            return result;
        }
    }

    public class Method
    {
        public string identifier;
        public MethodDeclarationSyntax declaration;
        readonly SemanticModel model;

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
            this.model = model;
            ISymbol symbol = model.GetSymbolInfo(methodInvocation).Symbol;
            if (symbol == null)
            {
                identifier = methodInvocation.Expression.ToString();
                return;
            };

            var declaringReferences = symbol.DeclaringSyntaxReferences;
            if (declaringReferences.Length < 1)
            {
                identifier = methodInvocation.Expression.ToString();
                return;
            };

            // not sure if this cast from SyntaxNode to MethodDeclarationSyntax always works
            declaration = (MethodDeclarationSyntax)declaringReferences
                .Single()
                .GetSyntax();
        }

        public Method(MethodDeclarationSyntax methodDeclaration, SemanticModel model)
        {
            declaration = methodDeclaration;
            identifier = declaration.Identifier.Text;
            this.model = model;
        }

        public bool ReadsStaticFieldOrProperty()
        {
            IEnumerable<IdentifierNameSyntax> identifiers = declaration
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var identifier in identifiers)
            {
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
            if (HasKnownDeclaration()) return declaration.Identifier.Text;
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
