using System.Threading.Tasks;
using Gu.Roslyn.Asserts;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Analyzers.DiagnosticSuppressors;
using NUnit.Framework;

namespace NUnit.Analyzers.Tests.DiagnosticSuppressors
{
    public class DereferencePossiblyNullReferenceSuppressorTests
    {
        private const string ABDefinition = @"
            private static A? GetA(bool create) => create ? new A() : default(A);

            private static B? GetB(bool create) => create ? new B() : default(B);

            private static A? GetAB(string? text) => new A { B = new B { Text = text } };
                
            private class A
            {
                public B? B { get; set; }
            }

            private class B
            {
                public string? Text { get; set; }

                public void Clear() => this.Text = null;

                [System.Diagnostics.Contracts.Pure]
                public string SafeGetText() => this.Text ?? string.Empty;
            }
        ";

        private static readonly DiagnosticSuppressor suppressor = new DereferencePossiblyNullReferenceSuppressor();

        [TestCase("")]
        [TestCase("Assert.NotNull(string.Empty)")]
        [TestCase("Assert.IsNull(s)")]
        [TestCase("Assert.Null(s)")]
        [TestCase("Assert.That(s, Is.Null)")]
        public void NoValidAssert(string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [TestCase("""")]
                public void Test(string? s)
                {{
                    {assert};
                    Assert.That(↓s.Length, Is.GreaterThan(0));
                }}
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("Assert.NotNull(s)")]
        [TestCase("Assert.IsNotNull(s)")]
        [TestCase("Assert.That(s, Is.Not.Null)")]
        public void WithLocalValidAssert(string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [TestCase("""")]
                public void Test(string? s)
                {{
                    {assert};
                    Assert.That(↓s.Length, Is.GreaterThan(0));
                }}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("Assert.NotNull(s)")]
        [TestCase("Assert.IsNotNull(s)")]
        [TestCase("Assert.That(s, Is.Not.Null)")]
        public void WithFieldValidAssert(string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                private string? s;
                [Test]
                public void Test()
                {{
                    {assert};
                    Assert.That(↓s.Length, Is.GreaterThan(0));
                }}

                public void SetS(string? v) => s = v;
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void ReturnValue()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [TestCase("""")]
                public void Test(string? s)
                {
                    string result = DoSomething(s);
                }

                private static string DoSomething(string? s)
                {
                    Assert.NotNull(s);
                    return ↓s;
                }
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8603"]),
                testCode);
        }

        [Test]
        public void Parameter()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [TestCase("""")]
                public void Test(string? s)
                {
                    Assert.NotNull(s);
                    DoSomething(↓s);
                }

                private static void DoSomething(string s)
                {
                    _ = s;
                }
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8604"]),
                testCode);
        }

        [Test]
        public void NullableCast()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    int? possibleNull = GetNext();
                    Assert.NotNull(possibleNull);
                    int i = ↓(int)possibleNull;
                    Assert.That(i, Is.EqualTo(1));
                }

                private static int? GetNext() => 1;
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8629"]),
                testCode);
        }

        [Test]
        public void WithReassignedAfterAssert()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [TestCase("""")]
                public void Test(string? s)
                {
                    Assert.NotNull(s);
                    s = null;
                    Assert.That(↓s.Length, Is.GreaterThan(0));
                }
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void WithReassignedFieldAfterAssert()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                private string? s;
                [Test]
                public void Test()
                {
                    Assert.NotNull(this.s);
                    this.s = null;
                    Assert.That(↓this.s.Length, Is.GreaterThan(0));
                }
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void WithPropertyExpression()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test([Values] bool create)
                {{
                    A? a = GetA(true);
                    Assert.That(a, Is.Not.Null);
                    ↓a.B = GetB(create);
                    Assert.That(a.B, Is.Not.Null);
                    ↓a.B.Text = ""?"";
                }}

                {ABDefinition}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void WithComplexExpression()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test([Values] bool create)
                {{
                    A? a = GetA(create);
                    Assert.That(a?.B?.Text, Is.Not.Null);
                    Assert.That(↓a.B.Text.Length, Is.GreaterThan(0));
                }}

                {ABDefinition}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void WithComplexReassignAfterAssert()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test([Values] bool create)
                {{
                    A a = new A {{ B = new B {{ Text = ""."" }} }};
                    Assert.That(a.B.Text, Is.Not.Null);
                    a.B = new B();
                    Assert.That(↓a.B.Text.Length, Is.GreaterThan(0));
                }}

                {ABDefinition}
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void InsideAssertMultiple()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [TestCase("""")]
                public void Test(string? s)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.NotNull(s);
                        Assert.That(↓s.Length, Is.GreaterThan(0));
                    });
                }
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("Assert.True(nullable.HasValue)")]
        [TestCase("Assert.IsTrue(nullable.HasValue)")]
        [TestCase("Assert.That(nullable.HasValue, \"Ensure Value is set\")")]
        [TestCase("Assert.That(nullable.HasValue)")]
        [TestCase("Assert.That(nullable.HasValue, Is.True)")]
        [TestCase("Assert.That(nullable, Is.Not.Null)")]
        public void NullableWithValidAssert(string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [TestCase(42)]
                public void Test(int? nullable)
                {{
                    {assert};
                    Assert.That(↓nullable.Value, Is.EqualTo(42));
                }}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8629"]),
                testCode);
        }

        [TestCase("Assert.False(nullable.HasValue)")]
        [TestCase("Assert.IsFalse(nullable.HasValue)")]
        [TestCase("Assert.That(!nullable.HasValue)")]
        [TestCase("Assert.That(nullable.HasValue, Is.False)")]
        [TestCase("Assert.That(nullable, Is.Null)")]
        public void NullableWithInvalidAssert(string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [TestCase(42)]
                public void Test(int? nullable)
                {{
                    {assert};
                    Assert.That(↓nullable.Value, Is.EqualTo(42));
                }}
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8629"]),
                testCode);
        }

        [Test]
        public void WithIndexer()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test()
                {{
                    string? directory = GetDirectoryName(""C:/Temp"");

                    Assert.That(directory, Is.Not.Null);
                    Assert.That(↓directory[0], Is.EqualTo('T'));
                }}

                // System.IO.Path.GetDirectoryName is not annotated in the libraries we are referencing.
                private static string? GetDirectoryName(string path) => System.IO.Path.GetDirectoryName(path);
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("var", "Throws")]
        [TestCase("Exception?", "Catch")]
        public void ThrowsLocalDeclaration(string type, string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test()
                {{
                    {type} ex = Assert.{assert}<Exception>(() => throw new InvalidOperationException());
                    string m = ↓ex.Message;
                }}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("var", "CatchAsync")]
        [TestCase("Exception?", "ThrowsAsync")]
        public void ThrowsAsyncLocalDeclaration(string type, string assert)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test()
                {{
                    {type} ex = Assert.{assert}<Exception>(() => Task.Delay(0));
                    string m = ↓ex.Message;
                }}
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [TestCase("var")]
        [TestCase("Exception?")]
        public void ThrowsLocalDeclarationInsideAssertMultiple(string type)
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@$"
                [Test]
                public void Test()
                {{
                    Assert.Multiple(() =>
                    {{
                        {type} ex = Assert.Throws<Exception>(() => throw new InvalidOperationException());
                        string m = ↓ex.Message;
                    }});
                }}
            ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void ThrowsLocalAssignment()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    Exception ex;
                    ex = ↓Assert.Throws<Exception>(() => throw new InvalidOperationException());
                }
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8600"]),
                testCode);
        }

        [Test]
        public void ThrowsPropertyAssignment()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                private Exception Ex { get; set; } = new NotImplementedException();

                [Test]
                public void Test()
                {
                    Ex = ↓Assert.Throws<Exception>(() => throw new InvalidOperationException());
                }
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8601"]),
                testCode);
        }

        [Test]
        public void ThrowsPassedAsArgument()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                private void ShowException(Exception ex) => Console.WriteLine(ex.Message);

                [Test]
                public void Test()
                {
                    ShowException(↓Assert.Throws<Exception>(() => throw new InvalidOperationException()));
                }
            ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8604"]),
                testCode);
        }

        [Test]
        public void ThrowAssignedOutsideAssertMultipleUsedInside()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    var e = Assert.Throws<Exception>(() => throw new Exception(""Test""));
                    Assert.Multiple(() =>
                    {
                        Assert.That(↓e.Message, Is.EqualTo(""Test""));
                        Assert.That(e.InnerException, Is.Null);
                        Assert.That(e.StackTrace, Is.Not.Empty);
                    });
                }");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void VariableAssertedOutsideAssertMultipleUsedInside()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    var e = GetPossibleException();
                    Assert.That(e, Is.Not.Null);
                    Assert.Multiple(() =>
                    {
                        Assert.That(↓e.Message, Is.EqualTo(""Test""));
                        Assert.That(e.InnerException, Is.Null);
                        Assert.That(e.StackTrace, Is.Not.Empty);
                    });
                }

                private Exception? GetPossibleException() => new Exception();
                ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void VariableAssignedOutsideAssertMultipleUsedInside()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    var e = GetPossibleException();
                    Assert.Multiple(() =>
                    {
                        Assert.That(↓e.Message, Is.EqualTo(""Test""));
                        Assert.That(e.InnerException, Is.Null);
                        Assert.That(e.StackTrace, Is.Not.Empty);
                    });
                }

                private Exception? GetPossibleException() => new Exception();
                ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void VariableAssignedUsedInsideLambda()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    var e = GetPossibleException();
                    Assert.That(() => ↓e.Message, Is.EqualTo(""Test""));
                }

                private Exception? GetPossibleException() => new Exception();
                ");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void VariableAssertedUsedInsideLambda()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    var e = GetPossibleException();
                    Assert.That(e, Is.Not.Null);
                    Assert.That(() => ↓e.Message, Is.EqualTo(""Test""));
                }

                private Exception? GetPossibleException() => new Exception();
                ");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void NestedStatements()
        {
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [Test]
                public void Test()
                {
                    Assert.Multiple(() =>
                    {
                        Assert.DoesNotThrow(() =>
                        {
                            var e = Assert.Throws<Exception>(() => new Exception(""Test""));
                            if (↓e.InnerException is not null)
                            {
                                Assert.That(e.InnerException.Message, Is.EqualTo(""Test""));
                            }
                            else
                            {
                                Assert.That(e.Message, Is.EqualTo(""Test""));
                            }
                        });
                    });
                }");

            RoslynAssert.NotSuppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }

        [Test]
        public void TestIssue436()
        {
            // Test is changed from actual issue by replacing the .Select with an [1].
            // The original code would not give null reference issues on the .Select for the .NET Framework
            // because System.Linq is not annotated and therefore the compiler doesn't know null is not allowed.
            var testCode = TestUtility.WrapMethodInClassNamespaceAndAddUsings(@"
                [TestCase(null)]
                public void HasCountShouldNotAffectNullabilitySuppression(List<int>? maybeNull)
                {
                    Assert.That(maybeNull, Is.Not.Null);
                    Assert.Multiple(() =>
                    {
                        Assert.That(maybeNull, Has.Count.EqualTo(2));
                        Assert.That(↓maybeNull[1], Is.EqualTo(1));
                    });
                }
            ", @"using System.Collections.Generic;");

            RoslynAssert.Suppressed(suppressor,
                ExpectedDiagnostic.Create(DereferencePossiblyNullReferenceSuppressor.SuppressionDescriptors["CS8602"]),
                testCode);
        }
    }
}
