# Powerstone — `:=` (short variable declaration)

Powerstone is the name of this C# fork. The first language change it ships is
the `:=` operator (Go-style "walrus" / short variable declaration).

## What it does

`x := expr;` introduces a new local `x` of inferred type, equivalent to
`var x = expr;`. The desugaring happens at parse time: the resulting syntax
tree is a normal `LocalDeclarationStatementSyntax` whose declared type is
implicit-`var`. Nothing downstream of the parser needs to know that `:=` was
used — semantic analysis, lowering, codegen, and the binder behave exactly
as if the user had written `var x = expr;`.

The token layout preserves every source character so `tree.ToFullString()`
reproduces the original program verbatim.

## What works

| Form                                | Status |
|-------------------------------------|--------|
| Statement: `x := expr;`             | ✅     |
| `for`-init: `for (i := 0; ...)`     | ✅     |
| `@`-verbatim names: `@event := ...` | ✅     |

## Missing / not yet supported

These are intentional v0 gaps, not bugs. Each could be added; tracked here so
the limits are explicit.

### Expression position
```csharp
var z = (x := 1);          // parse error
while ((line := Read()) != null) { ... }   // parse error
```
`:=` is parsed only at statement-start and inside `for`-init. There is no
"declaration expression" form. Python's walrus and Go's `if`/`for` short
declarations both fall in this category. Adding it would require a new
expression node and tighter scoping rules around the introduced binding.

### Multi-declarator
```csharp
x, y := 1, 2;              // parse error
```
Only a single identifier is accepted before `:=`. Go allows multi-name short
declarations; the C# `var x = 1, y = 2;` form is also unsupported here.
Would need either tuple-deconstruction lowering (`(x, y) := (1, 2);`, which
is a different feature) or a new comma-separated declarator list.

### Re-declaration vs. reuse
```csharp
x := 1;
x := 2;                    // CS0128: A local named 'x' is already defined
```
`:=` always declares a *new* local. Go permits re-using a name on the LHS as
long as at least one name on the LHS is new. The diagnostic here comes from
the binder (`CS0128`), not the parser, so the error message points at the
duplicate-local case rather than at the operator. Could be improved with a
parser-level diagnostic.

### Other declaration positions
- `using x := expr;` / `await using x := expr;` — not supported. Use
  `using var x = expr;`.
- `foreach (x := collection)` — not supported (semantically nonsensical
  anyway; `foreach` introduces, doesn't initialize).
- `out x := ...`, pattern positions (`is var x := ...`, etc.) — N/A by
  construction; `:=` is statement-level only.

### Tooling parity
The desugaring puts a synthetic `EqualsToken` whose `.Text` is `":="` into
the tree. All in-tree IDE/Workspace consumers filter `EqualsToken` by
`.Kind()`, which works correctly. External analyzers / refactorings that
read `Token.Text` and assume it equals `"="` would observe `":="` here.
Worth a smoke pass before relying on third-party tooling.

## Implementation notes

- New `SyntaxKind.ColonEqualsToken = 8288`. Produced by the lexer, consumed
  by the parser, never appears in a parsed tree.
- `LanguageParser.ParseColonEqualsVariableDeclaration` builds the synthetic
  `var`/name/`=` triple. Shared between statement and `for`-init paths.
- `TypeSyntax.IsVar` recognises the zero-width `var` token via
  `ContextualKind == VarKeyword && Width == 0`.
- See `StatementParsingTests.ColonEquals_*` for the round-trip + negative
  cases.

## Self-host

The compiler successfully compiles itself with ~14,400 `var` → `:=`
conversions across `src/Compilers/CSharp/Portable/` (commit history has the
exact scope). The `:=` form is therefore exercised against real production
code, not just tests.
