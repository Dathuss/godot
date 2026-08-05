using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Godot.SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstStringConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Common.StringTypeConstructorWithConstantStringRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreationSyntax, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreationSyntax, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private void AnalyzeObjectCreationSyntax(SyntaxNodeAnalysisContext context)
    {
        SemanticModel semanticModel = context.SemanticModel;
        SyntaxNode node = context.Node;

        if (semanticModel.GetOperation(node, context.CancellationToken) is IObjectCreationOperation
            {
                Type: ITypeSymbol type,
                Arguments: ImmutableArray<IArgumentOperation> { Length: 1 } args
            } && args[0].Value.ConstantValue.Value is string
            && type.FullQualifiedNameOmitGlobal() is GodotClasses.StringName or GodotClasses.NodePath)
        {
            string typeName = type.Name;
            context.ReportDiagnostic(Diagnostic.Create(
                Common.StringTypeConstructorWithConstantStringRule,
                node.GetLocation(),
                ImmutableDictionary.CreateRange(new KeyValuePair<string, string?>[] { new("typeName", typeName) }),
                typeName));
        }
    }
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConstStringConstructorCodeFixProvider))]
public sealed class ConstStringConstructorCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(Common.StringTypeConstructorWithConstantStringRule.Id);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;
        SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
        if (semanticModel == null)
            return;

        Diagnostic diagnostic = context.Diagnostics.First();

        TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

        SyntaxNode syntaxNode = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

        if (syntaxNode is not BaseObjectCreationExpressionSyntax objectCreationExpression)
            return;

        // Guaranteed to be either "StringName" or "NodePath"
        string typeName = diagnostic.Properties["typeName"]!;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Remove constructor",
                createChangedDocument: ct => RemoveExplicitConstructorAsync(context.Document, objectCreationExpression, ct),
                equivalenceKey: "GDStringTypeRemoveCtor"),
            context.Diagnostics
        );
    }

    private static async Task<Document> RemoveExplicitConstructorAsync(Document document,
        BaseObjectCreationExpressionSyntax objectCreationExpression, CancellationToken ct)
    {
        ExpressionSyntax argumentExpression = objectCreationExpression.ArgumentList!.Arguments[0].Expression;
        SyntaxNode oldRoot = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
        SyntaxNode newRoot = oldRoot.ReplaceNode(objectCreationExpression, argumentExpression);

        return document.WithSyntaxRoot(newRoot);
    }
}
