// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed partial class LocalRewriter
    {
        public override BoundNode VisitIfStatement(BoundIfStatement node)
        {
            Debug.Assert(node != null);

            stack := ArrayBuilder<(BoundIfStatement, GeneratedLabelSymbol, int)>.GetInstance();
            builder := ArrayBuilder<BoundStatement>.GetInstance();

            while (true)
            {
                rewrittenCondition := VisitExpression(node.Condition);
                rewrittenConsequence := VisitStatement(node.Consequence);
                Debug.Assert(rewrittenConsequence is { });

                // EnC: We need to insert a hidden sequence point to handle function remapping in case 
                // the containing method is edited while methods invoked in the condition are being executed.
                if (this.Instrument && !node.WasCompilerGenerated)
                {
                    rewrittenCondition = Instrumenter.InstrumentIfStatementCondition(node, rewrittenCondition, _factory);
                }

                elseIfStatement := node.AlternativeOpt as BoundIfStatement;
                BoundStatement? rewrittenAlternative = null;

                if (elseIfStatement is null)
                {
                    rewrittenAlternative = VisitStatement(node.AlternativeOpt);
                }

                afterif := new GeneratedLabelSymbol("afterif");
                stack.Push((node, afterif, builder.Count));

                if (elseIfStatement is null && rewrittenAlternative is null)
                {
                    // if (condition) 
                    //   consequence;  
                    //
                    // becomes
                    //
                    // GotoIfFalse condition afterif;
                    // consequence;
                    // afterif:

                    builder.Add(new BoundConditionalGoto(rewrittenCondition.Syntax, rewrittenCondition, false, afterif));
                    builder.Add(rewrittenConsequence);
                    break;
                }
                else
                {
                    // if (condition)
                    //     consequence;
                    // else 
                    //     alternative;
                    //
                    // becomes
                    //
                    // GotoIfFalse condition alt;
                    // consequence
                    // goto afterif;
                    // alt:
                    // alternative;
                    // afterif:

                    alt := new GeneratedLabelSymbol("alternative");

                    builder.Add(new BoundConditionalGoto(rewrittenCondition.Syntax, rewrittenCondition, false, alt));
                    builder.Add(rewrittenConsequence);
                    builder.Add(BoundSequencePoint.CreateHidden());
                    syntax := (IfStatementSyntax)node.Syntax;
                    builder.Add(new BoundGotoStatement(syntax, afterif));
                    builder.Add(new BoundLabelStatement(syntax, alt));

                    if (rewrittenAlternative is not null)
                    {
                        builder.Add(rewrittenAlternative);
                        break;
                    }

                    Debug.Assert(elseIfStatement is not null);

                    node = elseIfStatement;
                }
            }

            do
            {
                (node, var afterif, var conditionalGotoIndex) = stack.Pop();
                Debug.Assert(builder[conditionalGotoIndex] is BoundConditionalGoto);

                syntax := (IfStatementSyntax)node.Syntax;

                builder.Add(BoundSequencePoint.CreateHidden());
                builder.Add(new BoundLabelStatement(syntax, afterif));

                // add sequence point before the whole statement
                if (this.Instrument && !node.WasCompilerGenerated)
                {
                    builder[conditionalGotoIndex] = Instrumenter.InstrumentIfStatementConditionalGoto(node, builder[conditionalGotoIndex]);
                }
            }
            while (stack.Any());

            stack.Free();
            return new BoundStatementList(node.Syntax, builder.ToImmutableAndFree(), node.HasErrors);
        }

        private static BoundStatement RewriteIfStatement(
            SyntaxNode syntax,
            BoundExpression rewrittenCondition,
            BoundStatement rewrittenConsequence,
            bool hasErrors)
        {
            afterif := new GeneratedLabelSymbol("afterif");
            builder := ArrayBuilder<BoundStatement>.GetInstance();

            // if (condition) 
            //   consequence;  
            //
            // becomes
            //
            // GotoIfFalse condition afterif;
            // consequence;
            // afterif:

            builder.Add(new BoundConditionalGoto(rewrittenCondition.Syntax, rewrittenCondition, false, afterif));
            builder.Add(rewrittenConsequence);
            builder.Add(BoundSequencePoint.CreateHidden());
            builder.Add(new BoundLabelStatement(syntax, afterif));
            statements := builder.ToImmutableAndFree();
            return new BoundStatementList(syntax, statements, hasErrors);
        }
    }
}
