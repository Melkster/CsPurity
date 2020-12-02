using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsPurity;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

using static System.Console;

namespace CsPurityTests
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public void TestAnalyzeBasicPurity()
        {
            var file = (@"
                class C1
                {
                    int bar = 42;
                    int foo()
                    {
                        return bar;
                    }
                }
            ");
            LookupTable resultTable = Analyzer.Analyze(file);
            var fooDeclaration = resultTable.GetMethodByName("foo");

            Assert.AreEqual(resultTable.GetPurity(fooDeclaration), Purity.Pure);
        }

        /// <summary>
        /// Empty input or input with no methods should have no purity.
        /// </summary>
        [TestMethod]
        public void TestNoMethodsInInput()
        {
            var file1 = ("");
            var file2 = ("foo");
            var file3 = (@"
                namespace TestSpace
                {
                    class TestClass { }
                }
            ");

            LookupTable result1 = Analyzer.Analyze(file1);
            LookupTable result2 = Analyzer.Analyze(file2);
            LookupTable result3 = Analyzer.Analyze(file3);

            Assert.IsFalse(result1.table.AsEnumerable().Any());
            Assert.IsFalse(result2.table.AsEnumerable().Any());
            Assert.IsFalse(result3.table.AsEnumerable().Any());
        }

        /// <summary>
        /// Analyzer should handle local implicitly typed variables.
        ///
        /// Because `var` counts as an IdentifierNameSyntax this initially
        /// caused problems with the code.
        /// </summary>
        [TestMethod]
        public void HandleImmplicitlyTypedVariables()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        var bar = 42;
                        return bar;
                    }
                }
            ");
            Analyzer.Analyze(file);
        }

        [TestMethod]
        public void TestReadsStaticField()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        bar();
                        C2.fooz();
                        return 42 + C2.StaticValue;
                    }

                    static int bar()
                    {
                        return 1;
                    }

                    void faz()
                    {
                        C2.fooz();
                    }

                    int foz()
                    {
                        return C3.StaticValue;
                    }
                }

                class C2
                {
                    public static int StaticValue = 1;
                    public static int fooz()
                    {
                        return 3;
                    }
                }

                static class C3
                {
                    public static int StaticValue = 3;
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", analyzer.lookupTable.trees.Single().GetRoot());
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", analyzer.lookupTable.trees.Single().GetRoot());
            var fazDeclaration = HelpMethods.GetMethodDeclaration("faz", analyzer.lookupTable.trees.Single().GetRoot());

            Assert.IsTrue(analyzer.ReadsStaticFieldOrProperty(fooDeclaration));
            Assert.IsFalse(analyzer.ReadsStaticFieldOrProperty(barDeclaration));
            Assert.IsFalse(analyzer.ReadsStaticFieldOrProperty(fazDeclaration));
        }

        [TestMethod]
        public void TestReadsStaticProperty()
        {
            var file = (@"
                static class C1
                {
                    string foo() {
                        return C2.Name;
                    }
                }

                class C2
                {
                    public static string Name { get; set; } = ""foo"";
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", analyzer.lookupTable.trees.Single().GetRoot());

            Assert.IsTrue(analyzer.ReadsStaticFieldOrProperty(fooDeclaration));
        }

        [TestMethod]
        // Calling a static method is not considered impure
        public void TestCallsStaticMethod()
        {
            var file = (@"
                class C1
                {
                    public string Foo() {
                        return C2.bar();
                    }
                }

                class C2
                {
                    public static string bar() { return ""bar""; }
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var foo = HelpMethods.GetMethodDeclaration("Foo", analyzer.lookupTable.trees.Single().GetRoot());

            Assert.IsFalse(analyzer.ReadsStaticFieldOrProperty(foo));
        }

        [TestMethod]
        // Calling a static method is not considerd impure
        public void TestEnumInAttribute()
        {
            var file = (@"
                using System;
                class TestClass {
                    [Foo(Color.Blue)]
                    public string Bar() {
                        return ""bar"";
                    }

                    public enum Color
                    {
                        Red, Green, Blue
                    }

                    public class FooAttribute : Attribute
                    {
                        private Color color;

                        public FooAttribute(Color color)
                        {
                            this.color = color;
                        }
                    }
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var bar = HelpMethods.GetMethodDeclaration("Bar", analyzer.lookupTable.trees.Single().GetRoot());

            Assert.IsFalse(analyzer.ReadsStaticFieldOrProperty(bar));
        }

        // Implicitly static property means a non-static property pointing to a
        // static field
        //[TestMethod] // Not implemented in Analyzer for now
        public void TestReadsImplicitlyStaticProperty()
        {
            var file = (@"
                class C1
                {
                    string foo() {
                        C2 c2 = new C2();
                        return c2.Name;
                    }
                }

                class C2
                {
                    static string _name = ""foo"";
                    public string Name
                    {
                        get => _name;
                        set => _name = value;
                    }
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", analyzer.lookupTable.trees.Single().GetRoot());

            Assert.IsTrue(analyzer.ReadsStaticFieldOrProperty(fooDeclaration));
        }

        [TestMethod]
        public void TestThrowException()
        {
            var file = (@"
                class C1
                {
                    void foo() {
                        throw new Exception(
                            $""Foo exception""
                        );
                    }

                    int bar() {
                        return 42;
                    }
                }
            ");
            Analyzer analyzer = new Analyzer(file);
            var fooDeclaration = analyzer.lookupTable.GetMethodByName("foo");
            var barDeclaration = analyzer.lookupTable.GetMethodByName("bar");
            Assert.IsTrue(analyzer.ThrowsException(fooDeclaration));
            Assert.IsFalse(analyzer.ThrowsException(barDeclaration));
        }

        [TestMethod]
        public void TestAnalyze()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        C2 c2 = new C2();
                        return c2.foz();
                    }

                    int bar()
                    {
                        C2.baz();
                        return 42;
                    }
                }

                class C2
                {
                    public static int value = 42;

                    public static void baz()
                    {
                        value = 3;
                        value++;
                    }

                    public int foz() {
                        return 1;
                    }
                }
            ");
            LookupTable resultTable = Analyzer.Analyze(file);

            var fooDeclaration = resultTable.GetMethodByName("foo");
            var barDeclaration = resultTable.GetMethodByName("bar");
            var bazDeclaration = resultTable.GetMethodByName("baz");
            var fozDeclaration = resultTable.GetMethodByName("foz");

            Assert.AreEqual(resultTable.GetPurity(fooDeclaration), Purity.Pure);
            Assert.AreEqual(resultTable.GetPurity(barDeclaration), Purity.Impure);
            Assert.AreEqual(resultTable.GetPurity(bazDeclaration), Purity.Impure);
            Assert.AreEqual(resultTable.GetPurity(fozDeclaration), Purity.Pure);
        }

        [TestMethod]
        public void TestAnalyze2()
        {
            var file = (@"
                public class LinkedList
                {
                    private Node head;
                    private Node tail;

                    // Returns length of list
                    public static int Length(LinkedList list)
                    {
                        Node current = list.head;
                        int length = 0;

                        while (current != null)
                        {
                            length++;
                            current = current.next;
                        }
                        return length;
                    }

                    // Appends data to the list
                    public void Add(Object data)
                    {
                        if (LinkedList.Length(this) == 0)
                        {
                            head = new Node(data);
                            tail = head;
                        }
                        else
                        {
                            Node addedNode = new Node(data);
                            tail.next = addedNode;
                            tail = addedNode;
                        }
                    }

                    // Removes item at index from list.
                    // Assumes that list is non-empty and
                    // that index is non-negative and less
                    // than list's length
                    static public void Remove(int index, LinkedList list)
                    {
                        if (index == 0)
                        {
                            list.head = list.head.next;
                        }
                        else
                        {
                            Node pre = list.head;

                            for (int i = 0; i < index - 1; i++)
                            {
                                pre = pre.next;
                            }
                            pre.next = pre.next.next;
                        }
                    }

                    public static void PrintListLength(LinkedList list)
                    {
                        Console.WriteLine(Length(list));
                    }

                    public void PrintLength()
                    {
                        PrintListLength(this);
                    }

                    private class Node
                    {
                        public Node next;
                        public Object data;

                        public Node() { }

                        public Node(Object data)
                        {
                            this.data = data;
                        }
                    }
                }
            ");
            LookupTable resultTable = Analyzer.Analyze(file);

            var lengthDeclaration = resultTable.GetMethodByName("Length");
            var addDeclaration = resultTable.GetMethodByName("Add");
            var removeDeclaration = resultTable.GetMethodByName("Remove");
            var printListLengthDeclaration = resultTable.GetMethodByName("PrintListLength");
            var printLengthDeclaration = resultTable.GetMethodByName("PrintLength");

            //TODO: Implement checks for for commented purities
            Assert.AreEqual(resultTable.GetPurity(lengthDeclaration), Purity.Pure);

            Assert.AreEqual(resultTable.GetPurity(addDeclaration), Purity.Pure);
            //Assert.AreEqual(resultTable.GetPurity(addDeclaration), Purity.LocallyImpure);

            Assert.AreEqual(resultTable.GetPurity(removeDeclaration), Purity.Pure);
            //Assert.AreEqual(resultTable.GetPurity(removeDeclaration), Purity.ParametricallyImpure);

            Assert.AreEqual(resultTable.GetPurity(printListLengthDeclaration), Purity.Impure);
            Assert.AreEqual(resultTable.GetPurity(printLengthDeclaration), Purity.Impure);
        }

        [TestMethod]
        public void TestAnalyzeUnknownPurity()
        {
            var file = (@"
                class C1
                {
                    public List<int> foo()
                    {
                        return C2.bar();
                    }

                    public int foz()
                    {
                        return 1;
                    }
                }

                class C2
                {
                    public static List<int> bar() {
                        return C2.baz();
                    }

                    public static List<int> baz() {
                        List<int> l = new List<int>();
                        l.Add(1);
                        var c = l.Contains(1);
                        return l;
                    }
                }
            ");
            LookupTable resultTable = Analyzer.Analyze(file);

            var fooDeclaration = resultTable.GetMethodByName("foo");
            var fozDeclaration = resultTable.GetMethodByName("foz");
            var barDeclaration = resultTable.GetMethodByName("bar");
            var bazDeclaration = resultTable.GetMethodByName("baz");

            Assert.IsTrue(resultTable.GetPurity(fooDeclaration) == Purity.Unknown);
            Assert.IsTrue(resultTable.GetPurity(fozDeclaration) == Purity.Pure);
            Assert.IsTrue(resultTable.GetPurity(barDeclaration) == Purity.Unknown);
            Assert.IsTrue(resultTable.GetPurity(bazDeclaration) == Purity.Unknown);
        }

        [TestMethod]
        public void TestPurityIsKnownPrior()
        {
            Assert.IsTrue(Analyzer.PurityIsKnownPrior(new Method("Console.WriteLine")));
            Assert.IsFalse(Analyzer.PurityIsKnownPrior(new Method("foo")));
            Assert.IsFalse(Analyzer.PurityIsKnownPrior(new Method("")));

            Assert.IsTrue(Analyzer.PurityIsKnownPrior("Console.WriteLine"));
            Assert.IsFalse(Analyzer.PurityIsKnownPrior("foo"));
            Assert.IsFalse(Analyzer.PurityIsKnownPrior(""));

            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return 3;
                    }
                }
            ");
            var lt = Analyzer.Analyze(file);
            Method foo = lt.GetMethodByName("foo");

            Assert.IsFalse(Analyzer.PurityIsKnownPrior(foo));
        }

        [TestMethod]
        public void TestAnalyzeMultipleFiles()
        {
            var file1 = (@"
                using System;
                using ConsoleApp1;

                namespace ConsoleApp2
                {
                    class Program
                    {
                        public static string Bar()
                        {
                            return ""bar"";
                        }

                        static void Main()
                        {
                            Bar();
                            Console.WriteLine(Class1.Foo(""foo""));
                        }
                    }
                }
            ");

            var file2 = (@"
                using System;
                using System.Collections.Generic;
                using System.Text;

                namespace ConsoleApp1
                {
                    class Class1
                    {
                        public static string Foo(string val)
                        {
                            return val;
                        }
                    }
                }
            ");

            LookupTable lt = Analyzer
                .Analyze(new List<string> { file1, file2 })
                .StripMethodsNotDeclaredInAnalyzedFiles();

            var foo = lt.GetMethodByName("Foo");
            var bar = lt.GetMethodByName("Bar");
            var main = lt.GetMethodByName("Main");

            Assert.IsTrue(lt.table.Rows.Count == 3);
            Assert.IsTrue(lt.HasMethod(foo));
            Assert.IsTrue(lt.HasMethod(bar));
            Assert.IsTrue(lt.HasMethod(main));
        }

        [TestMethod]
        public void TestLocalFunction()
        {
            var file = (@"
                class Program
                {
                    static string Foo()
                    {
                        Foz();
                        return Bar();

                        string Bar()
                        {
                            return Baz();

                            string Baz()
                            {
                                string baz = ""baz"";
                                return baz;
                            }
                        }
                    }

                    static int Foz()
                    {
                        return 0;
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);

            var foo = lt.GetMethodByName("Foo");
            var foz = lt.GetMethodByName("Foz");

            // Foo() should not depend on Bar() or Baz() since they are a local
            // functions inside Foo(). The local functions are simply ignored
            // when calculating Foo()'s dependencies.
            //
            // Foo() should only depend on Foz()
            Assert.AreEqual(lt.CalculateDependencies(foo).Single(), foz);
            Assert.IsTrue(lt.HasMethod(foo));
            Assert.IsTrue(lt.HasMethod(foz));
            Assert.AreEqual(lt.table.Rows.Count, 2);
        }

        [TestMethod]
        public void TestDelegateFunction()
        {
            var file = (@"
                class Program
                {
                    // delegate declaration
                    public delegate void PrintString(string s);

                    public static void WriteToScreen(string str) {
                        Console.WriteLine(""The String is: {0}"", str);
                    }

                    public static void sendString(PrintString ps) {
                        ps(""Hello World"");
                    }

                    static void Foo() {
                        PrintString ps1 = new PrintString(WriteToScreen);
                        sendString(ps1);
                    }

                    static void Bar() {
                        PrintString ps1 = new PrintString(WriteToScreen);
                        ps1.BeginInvoke(""foo"", null, null);
                        ps1.EndInvoke(null);
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            Assert.AreEqual(lt.table.Rows.Count, 5);
        }

        [TestMethod]
        public void TestOverloading()
        {
            var file = (@"
                class Program
                {
                    int Foo(int i) {
                        return i * i;
                    }

                    int Foo(int i1, int i2) {
                        Console.WriteLine(i1);
                        return i1 * i2;
                    }

                    int Bar() {
                        return Foo(2) + Foo(3, 4);
                    }

                    int Bar1() {
                        return Foo(2);
                    }

                    int Bar2() {
                        return Foo(3, 4);
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var tree = lt.trees.Single();
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var foo1 = new Method(
                root
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == "Foo")
                    .First()
                );
            var foo2 = new Method(
                root
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == "Foo")
                    .Last()
                );

            var bar = lt.GetMethodByName("Bar");
            var bar1 = lt.GetMethodByName("Bar1");
            var bar2 = lt.GetMethodByName("Bar2");

            Assert.AreEqual(lt.GetPurity(bar), Purity.Impure);
            Assert.IsTrue(HelpMethods.HaveEqualElements(
                lt.CalculateDependencies(bar),
                new List<Method> { foo1, foo2 }
            ));

            Assert.AreEqual(lt.GetPurity(bar1), Purity.Pure);
            Assert.IsTrue(HelpMethods.HaveEqualElements(
                lt.CalculateDependencies(bar1),
                new List<Method> { foo1 }
            ));

            Assert.AreEqual(lt.GetPurity(bar2), Purity.Impure);
            Assert.IsTrue(HelpMethods.HaveEqualElements(
                lt.CalculateDependencies(bar2),
                new List<Method> { foo2 }
            ));
        }

        // For now, constructors are ignored by Analyzer.Analyze() and so the
        // constructor for class B is never analyzed
        [TestMethod]
        public void TestConstructorCall()
        {
            var file = (@"
                class A
                {
                    void Foo(int i) {
                        new B(i);
                    }
                }

                class B
                {
                    int val;
                    public B(int val) {
                        this.val = val;
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var foo = lt.GetMethodByName("Foo");

            Assert.AreEqual(lt.table.Rows.Count, 1);
            Assert.IsTrue(lt.HasMethod(foo));
        }

        [TestMethod]
        public void TestAnalyzeInterface()
        {
            var file = (@"
                public interface IParseTree
                {
                    new IParseTree Foo(int i);
                }

                public class A : IParseTree
                {
                    public IParseTree Foo(int i) {
                        return null;
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var foo = new Method(lt
                .trees
                .Single()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "Foo")
                .Last()
                );

            Assert.AreEqual(lt.table.Rows.Count, 1);
            Assert.IsTrue(lt.HasMethod(foo));
        }

        [TestMethod]
        public void TestRecursion()
        {
            var file = (@"
                class A
                {
                    int Foo(int i) {
                        if (i == 0) return 0;
                        return 1 + Foo(i - 1);
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var foo = lt.GetMethodByName("Foo");

            Assert.AreEqual(lt.table.Rows.Count, 1);
            Assert.IsTrue(lt.HasMethod(foo));
            Assert.AreEqual(lt.GetPurity(foo), Purity.Pure);
            Assert.IsTrue(!lt.CalculateDependencies(foo).Any());
        }

        [TestMethod]
        public void TestSecondHandRecursion()
        {
            var file = (@"
                class A
                {
                    int Foo(int i, int j) {
                        return Foo(i) + Foo(j);
                    }

                    int Foo(int i) {
                        if (i == 0) return 0;
                        return 1 + Foo(i - 1);
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var foo1 = new Method(lt
                .trees
                .Single()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "Foo")
                .First()
            );
            var foo2 = new Method(lt
                .trees
                .Single()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "Foo")
                .Last()
            );

            Assert.AreEqual(lt.table.Rows.Count, 2);
            Assert.AreEqual(lt.CalculateDependencies(foo1).Single(), foo2);
            Assert.IsTrue(!lt.CalculateDependencies(foo2).Any());
        }

        [TestMethod]
        public void TestMutualRecursion()
        {
            var file = (@"
                class A
                {
                    int Foo(int i) {
                        if (i == 0) return 0;
                        return Bar(i - 1)
                    }

                    int Bar(int i) {
                        if (i == 0) return 0;
                        return 1 + Foo(i - 1);
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);
            var foo = lt.GetMethodByName("Foo");
            var bar = lt.GetMethodByName("Bar");

            Assert.AreEqual(lt.table.Rows.Count, 2);
            Assert.AreEqual(lt.CalculateDependencies(foo).Single(), bar);
            Assert.AreEqual(lt.CalculateDependencies(bar).Single(), foo);
        }

        [TestMethod]
        public void TestHasPureAttribute()
        {
            var file = (@"
                using System.Diagnostics.Contracts;

                class Class2
                {
                    [Pure]
                    public string Foo()
                    {
                        return ""foo"";
                    }

                    [Foo]
                    public string Bar()
                    {
                        return ""bar"";
                    }

                    public string Baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var foo = HelpMethods.GetMethodDeclaration("Foo", root);
            var bar = HelpMethods.GetMethodDeclaration("Bar", root);
            var baz = HelpMethods.GetMethodDeclaration("Baz", root);

            Assert.IsTrue(foo.HasPureAttribute());
            Assert.IsFalse(bar.HasPureAttribute());
            Assert.IsFalse(baz.HasPureAttribute());
        }

        [TestMethod]
        public void TestHasMethodBody()
        {
            var file = (@"
                class Foz
                {
                    public int Bar()
                    {
                        return 0;
                    }
                }

                public interface IParseTree
                {
                    IParseTree Foo(int i);
                }

                abstract class Shape
                {
                    public abstract int GetArea();
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var bar = HelpMethods.GetMethodDeclaration("Bar", root);
            var foo = HelpMethods.GetMethodDeclaration("Foo", root);
            var getArea = HelpMethods.GetMethodDeclaration("GetArea", root);

            Assert.IsTrue(bar.HasBody());
            Assert.IsFalse(foo.HasBody());
            Assert.IsFalse(getArea.HasBody());
        }

        [TestMethod]
        public void TestEnumsAreImpure()
        {
            if (!Analyzer.enumsAreImpure) return;

            var file = (@"
            namespace Test {
                public class TestClass {
                    public TypeCode GetTypeCode() {
                        return TypeCode.String;
                    }

                    public TypeCode Foo(TypeCode tc) {
                        return tc;
                    }

                    public enum TypeCode
                    {
                        String = 18
                    }
                }
            }
            ");

            LookupTable lt = Analyzer.Analyze(file);
            var m = lt.GetMethodByName("GetTypeCode");
            var foo = lt.GetMethodByName("Foo");

            // Since enums are static, reading their value is considered impure
            Assert.AreNotEqual(Purity.Pure, lt.GetPurity(m));
            // But returning an enum or using it as a parameter is not impure
            Assert.AreEqual(Purity.Pure, lt.GetPurity(foo));
        }

        [TestMethod]
        public void TestEnumsAreNotImpure()
        {
            if (Analyzer.enumsAreImpure) return;

            var file = (@"
            namespace Test {
                public class TestClass {
                    public int bar = 0;

                    public bool Baz(TypeCode tc, int foo) {
                        bar = foo;
                        return tc == TypeCode.String;
                    }

                    public TypeCode GetTypeCode() {
                        return TypeCode.String;
                    }

                    public TypeCode Foo(TypeCode tc) {
                        return tc;
                    }

                    public enum TypeCode
                    {
                        String = 18
                    }
                }
            }
            ");

            LookupTable lt = Analyzer.Analyze(file);
            var m = lt.GetMethodByName("GetTypeCode");
            var baz = lt.GetMethodByName("Baz");
            var foo = lt.GetMethodByName("Foo");

            Assert.AreEqual(Purity.Pure, lt.GetPurity(m));
            Assert.AreEqual(Purity.Pure, lt.GetPurity(baz));
            Assert.AreEqual(Purity.Pure, lt.GetPurity(foo));
        }

        [TestMethod]
        public void TestContainsUnknownIdentifier()
        {
            if (Analyzer.enumsAreImpure) return;

            var file = (@"
                class A {
                    int val = 10;

                    int Foo()
                    {
                        return val;
                    }

                    int Bar()
                    {
                        Console.WriteLine(""bar"");
                    }

                    [Foo]
                    int Baz()
                    {
                        UnknownClass.UnknownMethod();
                    }

                    char[] GetBestFitUnicodeToBytesData()
                    {
                        return EmptyArray<Char>.Value;
                    }
                }
            ");

            Analyzer analyzer = new Analyzer(file);
            var foo = analyzer.lookupTable.GetMethodByName("Foo");
            var bar = analyzer.lookupTable.GetMethodByName("Bar");
            var baz = analyzer.lookupTable.GetMethodByName("Baz");
            var m = analyzer.lookupTable.GetMethodByName("GetBestFitUnicodeToBytesData");

            Assert.IsFalse(analyzer.ContainsUnknownIdentifier(foo));
            Assert.IsFalse(analyzer.ContainsUnknownIdentifier(bar));
            Assert.IsTrue(analyzer.ContainsUnknownIdentifier(baz));
            Assert.IsTrue(analyzer.ContainsUnknownIdentifier(m));
        }

        [TestMethod]
        public void TestPureCalleImpureCaller()
        {
            var file = (@"
                class A {

                    int Foo()
                    {
                        UnknownMethod();
                        return Bar();
                    }

                    int Bar()
                    {
                        return 42;
                    }
                }
            ");

            LookupTable lookupTable = Analyzer.Analyze(file);
            var foo = lookupTable.GetMethodByName("Foo");
            var bar = lookupTable.GetMethodByName("Bar");

            Assert.AreEqual(Purity.Unknown, lookupTable.GetPurity(foo));
            Assert.AreEqual(Purity.Pure, lookupTable.GetPurity(bar));
        }
    }

    [TestClass]
    public class LookupTableTest
    {

        [TestMethod]
        public void TestCalculateDependencies()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var lt = new LookupTable(tree);

            var foo = HelpMethods.GetMethodDeclaration("foo", root);
            var bar = HelpMethods.GetMethodDeclaration("bar", root);
            var baz = HelpMethods.GetMethodDeclaration("baz", root);
            var fooDependencies = lt.CalculateDependencies(foo);
            var barDependencies = lt.CalculateDependencies(bar);
            var bazDependencies = lt.CalculateDependencies(baz);
            var expectedFooDependencies = new List<Method> { bar, baz };
            var expectedBarDependencies = new List<Method> { baz };
            var expectedBazDependencies = new List<Method>();

            var foo2 = HelpMethods.GetMethodDeclaration("foo", root);
            var eq = foo2.Equals(foo);
            var eq2 = foo.Equals(foo2);
            var l1 = new List<Method> { foo };
            var l2 = new List<Method> { foo2 };

            Assert.IsTrue(eq);
            Assert.IsTrue(eq2);

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(fooDependencies, expectedFooDependencies)
            );

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(barDependencies, expectedBarDependencies)
            );

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(bazDependencies, expectedBazDependencies)
            );
        }

        [TestMethod]
        public void TestGettingMultipleDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        baz();
                        return bar();
                    }

                    string bar()
                    {
                        return ""bar"";
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(tree);
            var fooDependencies = lt.CalculateDependencies(new Method(fooDeclaration));
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .Select(m => new Method(m));

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingNestedDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar();
                    }

                    string bar()
                    {
                        return baz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var lt = new LookupTable(tree);

            var foo = lt.GetMethodByName("foo");
            var bar = lt.GetMethodByName("bar");
            var baz = lt.GetMethodByName("baz");

            var fooDependencies = lt.CalculateDependencies(foo);
            var expectedFooDependencies = new List<Method> { bar };

            var barDependencies = lt.CalculateDependencies(bar);
            var expectedBarDependencies = new List<Method> { baz };

            var bazDependencies = lt.CalculateDependencies(baz);
            var expectedBazDependencies = new List<Method> { };

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedFooDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    barDependencies,
                    expectedBarDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    bazDependencies,
                    expectedBazDependencies
                )
            );
        }

        [TestMethod]
        public void TestGettingMultipleNestedDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar() + baz();
                    }

                    string bar()
                    {
                        return far() + faz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }

                    void far()
                    {
                        return ""far"";
                    }

                    void faz()
                    {
                        return ""faz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var lt = new LookupTable(tree);

            var foo = lt.GetMethodByName("foo");
            var bar = lt.GetMethodByName("bar");
            var baz = lt.GetMethodByName("baz");
            var far = lt.GetMethodByName("far");
            var faz = lt.GetMethodByName("faz");

            var fooDependencies = lt.CalculateDependencies(foo);
            var barDependencies = lt.CalculateDependencies(bar);
            var bazDependencies = lt.CalculateDependencies(baz);
            var farDependencies = lt.CalculateDependencies(far);
            var fazDependencies = lt.CalculateDependencies(faz);

            var expectedFooDependencies = new List<Method> { bar, baz };
            var expectedBarDependencies = new List<Method> { far, faz };
            var expectedBazDependencies = new List<Method> { };
            var expectedFarDependencies = new List<Method> { };
            var expectedFazDependencies = new List<Method> { };

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedFooDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    barDependencies,
                    expectedBarDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    bazDependencies,
                    expectedBazDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    farDependencies,
                    expectedFarDependencies
                )
            );
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fazDependencies,
                    expectedFazDependencies
                )
            );
        }

        [TestMethod]
        public void TestGettingMethodDependency()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        C2 c2 = new C2();
                        return c2.bar();
                    }
                }

                class C2
                {
                    public string bar()
                    {
                        return ""bar"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(tree);
            var fooDependencies = lt.CalculateDependencies(new Method(fooDeclaration));
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .Select(m => new Method(m));

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingDependenciesWithSameNames()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return C2.bar() + bar();
                    }

                    string bar()
                    {
                        return ""bar"";
                    }
                }

                class C2
                {
                    public static string bar()
                    {
                        return ""bar"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(tree);
            var fooDependencies = lt.CalculateDependencies(new Method(fooDeclaration));
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .Select(m => new Method(m));

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingMultipleIdenticalDependencies()
        {
            var file = (@"
                class C1
                {
                    string foo()
                    {
                        return bar() + baz();
                    }

                    string bar()
                    {
                        return ""bar"" + baz() + baz();
                    }

                    void baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var model = Analyzer.GetSemanticModel(tree);

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(tree);
            var fooDependencies = lt.CalculateDependencies(new Method(fooDeclaration));
            var expectedResults = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() != "foo")
                .Select(m => new Method(m));

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedResults
                )
            );
        }

        [TestMethod]
        public void TestGettingBuiltInMethod()
        {
            var file = (@"
                class C1
                {
                    void foo()
                    {
                        Console.WriteLine(""foo"");
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var fooDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var lt = new LookupTable(tree);
            var fooDependencies = lt.CalculateDependencies(new Method(fooDeclaration));
            var cwlInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            SemanticModel model = Analyzer.GetSemanticModel(new List<SyntaxTree> { tree }, tree);
            var expectedResultList = new List<Method> { new Method(cwlInvocation, model) };

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    fooDependencies,
                    expectedResultList
                )
            );
        }

        [TestMethod]
        public void TestBuildLookupTable1()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar();
                    }

                    int bar()
                    {
                        return 42;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            LookupTable lookupTable1 = new LookupTable(tree);

            LookupTable lookupTable2 = new LookupTable();
            lookupTable2.AddMethod(HelpMethods.GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(HelpMethods.GetMethodDeclaration("bar", root));
            lookupTable2.AddDependency(
                HelpMethods.GetMethodDeclaration("foo", root),
                HelpMethods.GetMethodDeclaration("bar", root)
            );

            Assert.IsTrue(HelpMethods.TablesAreEqual(lookupTable2.table, lookupTable1.table));
        }

        [TestMethod]
        public void TestBuildLookupTable2()
        {
            var file = (@"
                class C2
                {
                    int foo()
                    {
                        C2 c2 = new C2();
                        return c2.bar();
                    }
                }

                class C2
                {
                    int bar()
                    {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            LookupTable lookupTable1 = new LookupTable(tree);

            LookupTable lookupTable2 = new LookupTable();
            lookupTable2.AddMethod(HelpMethods.GetMethodDeclaration("foo", root));
            lookupTable2.AddMethod(HelpMethods.GetMethodDeclaration("bar", root));
            lookupTable2.AddDependency(
                HelpMethods.GetMethodDeclaration("foo", root),
                HelpMethods.GetMethodDeclaration("bar", root)
            );

            Assert.IsTrue(HelpMethods.TablesAreEqual(lookupTable2.table, lookupTable1.table));
        }

        [TestMethod]
        public void TestHasMethod()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return 42;
                    }
                }
            ");
            LookupTable lookupTable = new LookupTable();
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var methodDeclaration = HelpMethods.GetMethodDeclaration("foo", root);

            lookupTable.AddMethod(methodDeclaration);

            Assert.IsTrue(lookupTable.HasMethod(methodDeclaration));
        }

        [TestMethod]
        public void TestRemoveMethod()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return 42;
                    }

                    int bar()
                    {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            LookupTable lookupTable = new LookupTable(tree);

            var foo = HelpMethods.GetMethodDeclaration("foo", root);
            var bar = HelpMethods.GetMethodDeclaration("bar", root);

            Assert.IsTrue(lookupTable.HasMethod(foo));
            Assert.IsTrue(lookupTable.HasMethod(bar));

            lookupTable.RemoveMethod(foo);

            Assert.IsFalse(lookupTable.HasMethod(foo));
            Assert.IsTrue(lookupTable.HasMethod(bar));

            lookupTable.RemoveMethod(bar);

            Assert.IsFalse(lookupTable.HasMethod(foo));
            Assert.IsFalse(lookupTable.HasMethod(bar));
        }

        [TestMethod]
        public void TestStripMethodsNotDeclaredInAnalyzedFiles()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        Console.WriteLine();
                        return 42;
                    }

                    int bar()
                    {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            LookupTable lookupTable = new LookupTable(tree);

            var foo = HelpMethods.GetMethodDeclaration("foo", root);
            var bar = HelpMethods.GetMethodDeclaration("bar", root);
            var cwl = new Method("Console.WriteLine");

            Assert.IsTrue(lookupTable.HasMethod(foo));
            Assert.IsTrue(lookupTable.HasMethod(bar));
            Assert.IsTrue(lookupTable.HasMethod(cwl));

            lookupTable = lookupTable.StripMethodsNotDeclaredInAnalyzedFiles();

            Assert.IsTrue(lookupTable.HasMethod(foo));
            Assert.IsTrue(lookupTable.HasMethod(bar));
            Assert.IsFalse(lookupTable.HasMethod(cwl));
        }

        [TestMethod]
        public void TestStripInterfaceMethods()
        {
            var file = (@"
                class C1 : I1
                {
                    public int Foo()
                    {
                        return 42;
                    }
                }

                public interface I1
                {
                    int Foo();
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);

            var foo = new Method(lt
                .trees
                .Single()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "Foo")
                .First()
            );

            lt = lt.StripInterfaceMethods();
            Assert.AreEqual(lt.table.Rows.Count, 1);
            Assert.IsTrue(lt.HasMethod(foo));
        }

        [TestMethod]
        public void TestAddDependency()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar();
                    }

                    int bar()
                    {
                        return 42;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", root);
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", root);

            LookupTable lookupTable = new LookupTable();
            lookupTable.AddMethod(fooDeclaration);
            lookupTable.AddDependency(fooDeclaration, barDeclaration);

            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, barDeclaration));
        }

        [TestMethod]
        public void TestRemoveDependency()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", root);
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", root);
            var bazDeclaration = HelpMethods.GetMethodDeclaration("baz", root);

            LookupTable lookupTable = new LookupTable();
            lookupTable.AddMethod(fooDeclaration);
            lookupTable.AddDependency(fooDeclaration, barDeclaration);
            lookupTable.AddDependency(fooDeclaration, bazDeclaration);
            lookupTable.AddDependency(barDeclaration, bazDeclaration);
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(fooDeclaration, barDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(fooDeclaration, bazDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            lookupTable.RemoveDependency(barDeclaration, bazDeclaration);
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsFalse(lookupTable.HasDependency(barDeclaration, bazDeclaration));

            LookupTable lookupTable2 = new LookupTable(tree);

            Assert.IsTrue(lookupTable2.HasDependency(fooDeclaration, barDeclaration));
            Assert.IsTrue(lookupTable2.HasDependency(fooDeclaration, bazDeclaration));
            Assert.IsTrue(lookupTable2.HasDependency(barDeclaration, bazDeclaration));
        }

        [TestMethod]
        public void TestCalculateWorkingSet()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }

                    int foz() {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var bazDeclaration = HelpMethods.GetMethodDeclaration("baz", root);
            var fozDeclaration = HelpMethods.GetMethodDeclaration("foz", root);

            LookupTable lookupTable = new LookupTable(tree);

            var expectedResult = new List<Method>() { bazDeclaration, fozDeclaration };

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    expectedResult,
                    lookupTable.workingSet
                )
            );

            lookupTable.workingSet.Calculate();

            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    new List<MethodDeclarationSyntax>(),
                    lookupTable.workingSet
                )
            );
        }

        [TestMethod]
        public void TestGetSetPurity()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }

                    int foz() {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", root);
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", root);
            var bazDeclaration = HelpMethods.GetMethodDeclaration("baz", root);
            var fozDeclaration = HelpMethods.GetMethodDeclaration("foz", root);

            LookupTable lookupTable = new LookupTable(tree);

            Assert.AreEqual(lookupTable.GetPurity(fooDeclaration), Purity.Pure);
            Assert.AreEqual(lookupTable.GetPurity(barDeclaration), Purity.Pure);
            Assert.AreEqual(lookupTable.GetPurity(bazDeclaration), Purity.Pure);
            Assert.AreEqual(lookupTable.GetPurity(fozDeclaration), Purity.Pure);

            lookupTable.SetPurity(fooDeclaration, Purity.Impure);
            lookupTable.SetPurity(barDeclaration, Purity.Pure);
            lookupTable.SetPurity(bazDeclaration, Purity.ParametricallyImpure);

            Assert.AreEqual(lookupTable.GetPurity(fooDeclaration), Purity.Impure);
            Assert.AreEqual(lookupTable.GetPurity(barDeclaration), Purity.Pure);
            Assert.AreEqual(lookupTable.GetPurity(bazDeclaration), Purity.ParametricallyImpure);
            Assert.AreEqual(lookupTable.GetPurity(fozDeclaration), Purity.Pure);

            lookupTable.SetPurity(fooDeclaration, Purity.Impure);
            lookupTable.SetPurity(barDeclaration, Purity.Pure);
            lookupTable.SetPurity(bazDeclaration, Purity.ParametricallyImpure);

            Assert.AreEqual(lookupTable.GetPurity(fooDeclaration), Purity.Impure);
            Assert.AreEqual(lookupTable.GetPurity(barDeclaration), Purity.Pure);
            Assert.AreEqual(lookupTable.GetPurity(bazDeclaration), Purity.ParametricallyImpure);
            Assert.AreEqual(lookupTable.GetPurity(fozDeclaration), Purity.Pure);
        }

        [TestMethod]
        public void TestGetAllImpureMethods()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }

                    int foz() {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", root);
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", root);
            var bazDeclaration = HelpMethods.GetMethodDeclaration("baz", root);
            var fozDeclaration = HelpMethods.GetMethodDeclaration("foz", root);

            LookupTable lookupTable = new LookupTable(tree);

            lookupTable.SetPurity(fooDeclaration, Purity.Impure);
            lookupTable.SetPurity(barDeclaration, Purity.Impure);
            var workingSet = new List<Method>
            {
                fooDeclaration,
                barDeclaration,
                bazDeclaration,
                fozDeclaration
            };
            var expected = new List<Method>
            {
                fooDeclaration,
                barDeclaration
            };
            Assert.IsTrue(
                HelpMethods.HaveEqualElements(
                    expected, lookupTable.GetAllImpureMethods(workingSet)
                )
            );
        }

        [TestMethod]
        public void TestGetCallers()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }

                    int foz() {
                        return 1;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var fooDeclaration = HelpMethods.GetMethodDeclaration("foo", root);
            var barDeclaration = HelpMethods.GetMethodDeclaration("bar", root);
            var bazDeclaration = HelpMethods.GetMethodDeclaration("baz", root);
            var fozDeclaration = HelpMethods.GetMethodDeclaration("foz", root);

            LookupTable lookupTable = new LookupTable(tree);

            var result = lookupTable.GetCallers(bazDeclaration);
            var expected = new List<Method> { fooDeclaration, barDeclaration };
            Assert.IsTrue(HelpMethods.HaveEqualElements(result, expected));
            Assert.IsTrue(lookupTable.GetCallers(fozDeclaration).Count == 0);

            result = lookupTable.GetCallers(barDeclaration);
            expected = new List<Method> { fooDeclaration };
            Assert.IsTrue(HelpMethods.HaveEqualElements(result, expected));
        }

        [TestMethod]
        public void TestCountMethods()
        {
            var file1 = (@"
                class C1
                {
                    int foo()
                    {
                        return bar() + C2.baz();
                    }

                    int bar()
                    {
                        return 42 + C2.baz();
                    }
                }

                class C2
                {
                    public static int baz()
                    {
                        return 42;
                    }

                    int foz() {
                        return 1;
                    }
                }
            ");

            var file2 = "";

            LookupTable lt1 = Analyzer.Analyze(file1);
            LookupTable lt2 = Analyzer.Analyze(file2);

            Assert.AreEqual(lt1.CountMethods(), 4);
            Assert.AreEqual(lt2.CountMethods(), 0);
        }

        [TestMethod]
        public void TestCountMethodsWithPurity()
        {
            var file = (@"
                class C1
                {
                    int foo()
                    {
                        C2 c2 = new C2();
                        return c2.foz();
                    }

                    int bar()
                    {
                        C2.baz();
                        return 42;
                    }
                }

                class C2
                {
                    public static int value = 42;

                    public static void baz()
                    {
                        value = 3;
                        value++;
                    }

                    public int foz() {
                        return 1;
                    }
                }

                class C2
                {
                    public static void faz()
                    {
                        UnknownFunction(""faz"");
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);

            Assert.AreEqual(lt.CountMethodsWithPurity(Purity.Pure), 2);
            Assert.AreEqual(lt.CountMethodsWithPurity(Purity.Impure), 2);
            Assert.AreEqual(lt.CountMethodsWithPurity(Purity.Unknown), 2);
        }

        [TestMethod]
        public void TestCountMethodsWithPureAttribute()
        {
            var file = (@"
                using System.Diagnostics.Contracts;

                class Class2
                {
                    [Pure]
                    public string Foo()
                    {
                        return ""foo"";
                    }

                    [Foo]
                    public string Bar()
                    {
                        return ""bar"";
                    }

                    public string Baz()
                    {
                        return ""baz"";
                    }
                }
            ");
            LookupTable lt = Analyzer.Analyze(file);

            Assert.AreEqual(1, lt.CountMethodsWithPureAttribute());
        }
    }

    public static class HelpMethods
    {
        public static Method GetMethodDeclaration(
            string name,
            SyntaxNode root
        )
        {
            var methodDeclaration = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == name)
                .Single();
            return new Method(methodDeclaration);
        }

        public static Method GetMethodByName(
            this LookupTable lookupTable,
            string name
        )
        {
            foreach (var tree in lookupTable.trees)
            {
                var methodDeclarations = tree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == name);
                if (methodDeclarations.Any())
                {
                    return new Method(methodDeclarations.Single());
                }
            }
            return null;
        }

        // Rows need to be in the same order in both tables
        public static bool TablesAreEqual(DataTable table1, DataTable table2)
        {
            if (table1.Rows.Count != table1.Rows.Count) return false;

            for (int i = 0; i < table1.Rows.Count; i++)
            {
                if (!RowsAreEqual(table1.Rows[i], table2.Rows[i])) return false;
            }
            return true;

            // Dependency fields can be in different order
            static bool RowsAreEqual(DataRow row1, DataRow row2)
            {
                return
                    row1.Field<Method>("identifier").Equals(row2.Field<Method>("identifier")) &&
                    row1.Field<Purity>("purity").Equals(row2.Field<Purity>("purity")) &&
                    HaveEqualElements(
                        row1.Field<List<Method>>("dependencies"),
                        row2.Field<List<Method>>("dependencies")
                    );
            }
        }

        public static bool HaveEqualElements(IEnumerable<Object> list1, IEnumerable<Object> list2)
        {
            if (list1.Count() != list2.Count()) return false;
            foreach (var item in list1)
            {
                if (!list2.Contains(item)) return false;
            }
            return true;
        }
    }

    [TestClass]
    public class MethodTest
    {
        [TestMethod]
        public void TestMethod()
        {
            var file = (@"
                class C1
                {
                    void foo()
                    {
                        Console.WriteLine();
                        C2.bar();
                    }
                }

                class C2
                {
                    public static int bar()
                    {
                        return 2;
                    }
                }
            ");
            var tree = CSharpSyntaxTree.ParseText(file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var clwInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var barInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Last();

            SemanticModel model = Analyzer.GetSemanticModel(new List<SyntaxTree> { tree }, tree);

            var clwMethod = new Method(clwInvocation, model);
            var barMethod = new Method(barInvocation, model);

            Assert.AreEqual(clwMethod.identifier, "Console.WriteLine");
            Assert.AreEqual(clwMethod.declaration, null);
            Assert.AreEqual(barMethod.identifier, null);
            Assert.AreEqual(
                barMethod,
                HelpMethods.GetMethodDeclaration("bar", root)
            );
        }
    }
}
