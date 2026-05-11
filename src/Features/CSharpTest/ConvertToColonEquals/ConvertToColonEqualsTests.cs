// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.ConvertToColonEquals;
using Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Testing;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.ConvertToColonEquals;

using VerifyCS = CSharpCodeRefactoringVerifier<CSharpConvertToColonEqualsCodeRefactoringProvider>;

[UseExportProvider]
public sealed class ConvertToColonEqualsTests
{
    [Fact]
    public Task ConvertSimpleLocal()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    var [||]x = 42;
                }
            }
            """,
            FixedCode = """
            class C
            {
                void M()
                {
                    x := 42;
                }
            }
            """,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task ConvertStringInitializer()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    var [||]name = "hello";
                }
            }
            """,
            FixedCode = """
            class C
            {
                void M()
                {
                    name := "hello";
                }
            }
            """,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task ConvertForInit()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    for (var [||]i = 0; i < 10; i++) { }
                }
            }
            """,
            FixedCode = """
            class C
            {
                void M()
                {
                    for (i := 0; i < 10; i++) { }
                }
            }
            """,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task ConvertVerbatimIdentifier()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    var [||]@event = 1;
                }
            }
            """,
            FixedCode = """
            class C
            {
                void M()
                {
                    @event := 1;
                }
            }
            """,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    // --- Negative cases (refactor must NOT offer) ---

    [Fact]
    public Task NotOfferedOnExplicitType()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    int [||]x = 42;
                }
            }
            """,
            OffersEmptyRefactoring = false,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task NotOfferedOnMultipleDeclarators()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    var [||]x = 1, y = 2;
                }
            }
            """,
            CompilerDiagnostics = CompilerDiagnostics.None,
            OffersEmptyRefactoring = false,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task NotOfferedWithoutInitializer()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    var [||]x;
                }
            }
            """,
            CompilerDiagnostics = CompilerDiagnostics.None,
            OffersEmptyRefactoring = false,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task NotOfferedOnUsingDeclaration()
        => new VerifyCS.Test
        {
            TestCode = """
            using System.IO;
            class C
            {
                void M()
                {
                    using var [||]s = new MemoryStream();
                }
            }
            """,
            OffersEmptyRefactoring = false,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();

    [Fact]
    public Task NotOfferedWhenAlreadyShortDeclaration()
        => new VerifyCS.Test
        {
            TestCode = """
            class C
            {
                void M()
                {
                    [||]x := 42;
                }
            }
            """,
            OffersEmptyRefactoring = false,
            CodeActionValidationMode = CodeActionValidationMode.None,
        }.RunAsync();
}
