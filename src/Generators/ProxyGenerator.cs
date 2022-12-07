using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ActualLab.Generators;

[Generator]
public class ProxyGenerator : IIncrementalGenerator
{
    private static readonly string AbstractionsNamespaceName = "ActualLab.Generators";
    private static readonly string GenerateProxyAttributeFullName = $"{AbstractionsNamespaceName}.GenerateProxyAttribute";

    private const string ProxyInterfaceTypeName = "IProxy";
    private const string ProxyInterfaceBindMethodName = "Bind";
    private const string ProxyClassSuffix = "Proxy";
    private const string InterceptorTypeName = "Interceptor";
    private const string InterceptorPropertyName = "Interceptor";

    private ITypeSymbol? _generateProxyAttributeType;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var items = context.SyntaxProvider
            .CreateSyntaxProvider(CouldBeAugmented, MustAugment)
            .Where(i => i.ClassDef != null)
            .Collect();
        context.RegisterSourceOutput(items, Generate!);
    }

    private bool CouldBeAugmented(SyntaxNode node, CancellationToken cancellationToken)
        => node is ClassDeclarationSyntax {
            Parent: FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax
        }; // Top-level type

    private (SemanticModel SemanticModel, ClassDeclarationSyntax? ClassDef)
        MustAugment(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;
        _generateProxyAttributeType ??= compilation.GetTypeByMetadataName(GenerateProxyAttributeFullName)!;

        var classDef = (ClassDeclarationSyntax) context.Node;
        var generateCtorAttrDef = semanticModel.GetAttribute(_generateProxyAttributeType!, classDef.AttributeLists);
        return generateCtorAttrDef == null ? default : (semanticModel, classDef);
    }

    private void Generate(
        SourceProductionContext context,
        ImmutableArray<(SemanticModel SemanticModel, ClassDeclarationSyntax ClassDef)> items)
    {
        if (items.Length == 0)
            return;
#if DEBUG
        try {
            context.ReportDiagnostic(DebugWarning($"Found {items.Length} type(s) to generate proxy."));
            GenerateImpl(context, items);
        }
        catch (Exception e) {
            // This code allows to see the stack trace
            context.ReportDiagnostic(DebugWarning(e));
        }
#else
        GenerateImpl(context, items);
#endif
    }

    private void GenerateImpl(
        SourceProductionContext context,
        ImmutableArray<(SemanticModel SemanticModel, ClassDeclarationSyntax ClassDef)> items)
    {
        foreach (var item in items) {
            var typeInfo = BuildTypeInfo(item.SemanticModel, item.ClassDef);
            var code = GenerateCode(context, item.SemanticModel, typeInfo);
            var classType = (ITypeSymbol)item.SemanticModel.GetDeclaredSymbol(item.ClassDef)!;
            context.AddSource($"{classType.ContainingNamespace}.{classType.Name}Proxy.g.cs", code);
            context.ReportDiagnostic(AutoInjectTypeProcessedInfo(item.ClassDef));
        }
    }

    private ProxyTypeInfo BuildTypeInfo(SemanticModel itemSemanticModel, ClassDeclarationSyntax itemClassDef)
    {
        var typeInfo = new ProxyTypeInfo {
            ClassDef = itemClassDef
        };
        return typeInfo;
    }

    private string GenerateCode(SourceProductionContext context, SemanticModel semanticModel, ProxyTypeInfo typeInfo)
    {
        var originalClassDef = typeInfo.ClassDef;
        var ns = originalClassDef.GetNamespaceName();
        var actualLabNs = QualifiedName(IdentifierName("ActualLab"), IdentifierName("Generators"));

        var baseClassNameSyntax = IdentifierName(originalClassDef.Identifier.Text);
        var baseClassFullNameSyntax = ns != null ? (NameSyntax)QualifiedName(ns, baseClassNameSyntax) : baseClassNameSyntax;
        var classDef = ClassDeclaration(originalClassDef.Identifier.Text + ProxyClassSuffix)
            .WithBaseList(BaseList(CommaSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(baseClassFullNameSyntax),
                SimpleBaseType(QualifiedName(actualLabNs, IdentifierName(ProxyInterfaceTypeName))))
            ))
            .WithTypeParameterList(originalClassDef.TypeParameterList)
            .AddModifiers(Token(SyntaxKind.PartialKeyword));

        var classMembers = new List<MemberDeclarationSyntax>();

        AddMethodOverrides(classMembers, context, originalClassDef, actualLabNs);

        context.ReportDiagnostic(DebugWarning($"{classMembers.Count} class members added for method overrides."));

        AddInterceptor(classMembers, actualLabNs);

        AddConstructors(classMembers, context, originalClassDef, classDef.Identifier.Text);

        if (classMembers.Count > 0)
            classDef = classDef.WithMembers(List(classMembers));

        // Building Compilation unit

        var syntaxRoot = semanticModel.SyntaxTree.GetRoot();
        var unit = CompilationUnit()
            .AddUsings(syntaxRoot.ChildNodes().OfType<UsingDirectiveSyntax>().ToArray())
            .AddMembers(
                FileScopedNamespaceDeclaration(originalClassDef.GetNamespaceName()!)
                    .AddMembers(classDef));

        var code = unit.NormalizeWhitespace().ToFullString();
        return "// Generated code" + Environment.NewLine +
            "#nullable enable" + Environment.NewLine +
            code;
    }

    private static void AddConstructors(ICollection<MemberDeclarationSyntax> classMembers, SourceProductionContext context,
        ClassDeclarationSyntax originalClassDef, string className)
    {
        foreach (var originalCtor in originalClassDef.Members.OfType<ConstructorDeclarationSyntax>()) {
            var parameters = new List<SyntaxNodeOrToken>();
            foreach (var parameter in originalCtor.ParameterList.Parameters) {
                if (parameters.Count > 0)
                    parameters.Add(Token(SyntaxKind.CommaToken));
                parameters.Add(Argument(IdentifierName(parameter.Identifier.Text)));
            }

            var ctorDef = ConstructorDeclaration(Identifier(className))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(originalCtor.ParameterList)
                .WithInitializer(
                    ConstructorInitializer(SyntaxKind.BaseConstructorInitializer,
                        ArgumentList(SeparatedList<ArgumentSyntax>(parameters))))
                .WithBody(Block());
            classMembers.Add(ctorDef);
        }
    }

    private static void AddMethodOverrides(
        ICollection<MemberDeclarationSyntax> classMembers,
        SourceProductionContext context,
        ClassDeclarationSyntax originalClassDef,
        QualifiedNameSyntax actualLabNs)
    {
        const string argumentListTypeName = "ArgumentList";
        const string argumentListNewMethodName = "New";
        const string interceptMethodName = "Intercept";

        var cachedInterceptedIndex = 0;

        foreach (var originalMethod in originalClassDef.Members.OfType<MethodDeclarationSyntax>()) {
            var originalMethodModifiers = originalMethod.Modifiers;
            var isPublic = originalMethodModifiers.Any(c => c.IsKind(SyntaxKind.PublicKeyword));
            var isProtected = originalMethodModifiers.Any(c => c.IsKind(SyntaxKind.ProtectedKeyword));
            var isPrivate = !isPublic && !isProtected;
            if (isPrivate)
                continue;
            var isVirtual = originalMethodModifiers.Any(c => c.IsKind(SyntaxKind.VirtualKeyword));
            if (!isVirtual) {
                context.ReportDiagnostic(DebugWarning($"method '{originalMethod.ToString()}' is not virtual and not private."));
                continue;
            }

            var fieldName = "_cachedIntercepted" + cachedInterceptedIndex++;

            var fieldType = NullableType(
                GenericName(Identifier("Func"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            CommaSeparatedList(
                                  QualifiedName(actualLabNs, IdentifierName(argumentListTypeName)),
                                originalMethod.ReturnType
                            ))));
            var cachedInterceptedField = FieldDeclaration(VariableDeclaration(fieldType)
                    .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(fieldName)))))
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

            var accessModifier = isPublic ? Token(SyntaxKind.PublicKeyword)
                : isProtected ? Token(SyntaxKind.ProtectedKeyword)
                : throw new InvalidOperationException("Wrong access modifer");

            var typedArgsVarGenericArguments = new List<TypeSyntax>();
            foreach (var parameter in originalMethod.ParameterList.Parameters) {
                typedArgsVarGenericArguments.Add(parameter.Type!);
            }

            var typeArgsVariableType = QualifiedName(actualLabNs,
                GenericName(Identifier(argumentListTypeName))
                        .WithTypeArgumentList(
                            TypeArgumentList(CommaSeparatedList(typedArgsVarGenericArguments))));

            var typedArgsVariable = VariableDeclarator(Identifier("typedArgs"))
                .WithInitializer(EqualsValueClause(
                    CastExpression(typeArgsVariableType, IdentifierName("args")
                    )));
            var baseCallArguments = new List<SyntaxNodeOrToken>();
            for (int itemId = 0; itemId < originalMethod.ParameterList.Parameters.Count; itemId++) {
                if (baseCallArguments.Count > 0)
                    baseCallArguments.Add(Token(SyntaxKind.CommaToken));
                baseCallArguments.Add(Argument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(typedArgsVariable.Identifier.Text),
                        IdentifierName("Item" + itemId))));
            }

            var interceptedBlock = Block(
                LocalDeclarationStatement(
                    VariableDeclaration(VarIdentifier())
                        .WithVariables(SingletonSeparatedList(typedArgsVariable))),
                ReturnStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                BaseExpression(),
                                IdentifierName(originalMethod.Identifier.Text)))
                        .WithArgumentList(ArgumentList(SeparatedList<ArgumentSyntax>(baseCallArguments)))));

            var argumentListFactoryMethodParams = new List<ArgumentSyntax>();
            foreach (var parameter in originalMethod.ParameterList.Parameters)
                argumentListFactoryMethodParams.Add(Argument(IdentifierName(parameter.Identifier)));

            var interceptedVariableIdentifier = Identifier("intercepted");
            var methodBody = Block(
                LocalDeclarationStatement(
                    VariableDeclaration(VarIdentifier())
                        .WithVariables(
                            SingletonSeparatedList(
                                VariableDeclarator(interceptedVariableIdentifier)
                                    .WithInitializer(
                                        EqualsValueClause(
                                            AssignmentExpression(
                                                SyntaxKind.CoalesceAssignmentExpression,
                                                IdentifierName(fieldName),
                                                SimpleLambdaExpression(
                                                        Parameter(Identifier("args")))
                                                    .WithBlock(interceptedBlock))))))),
                ReturnStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(InterceptorTypeName),
                                IdentifierName(interceptMethodName)))
                        .WithArgumentList(
                            ArgumentList(
                                CommaSeparatedList(
                                    Argument(IdentifierName(interceptedVariableIdentifier.Text)),
                                    Argument(
                                        InvocationExpression(
                                                MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    QualifiedName(actualLabNs, IdentifierName(argumentListTypeName)),
                                                    IdentifierName(argumentListNewMethodName)))
                                            .WithArgumentList(
                                                ArgumentList(CommaSeparatedList(argumentListFactoryMethodParams))))
                                )))));

            var interceptedMethod = MethodDeclaration(originalMethod.ReturnType, originalMethod.Identifier)
                .WithModifiers(
                    TokenList(accessModifier, Token(SyntaxKind.SealedKeyword), Token(SyntaxKind.OverrideKeyword)))
                .WithParameterList(originalMethod.ParameterList)
                .WithBody(methodBody);

            classMembers.Add(cachedInterceptedField);
            classMembers.Add(interceptedMethod);

            cachedInterceptedIndex++;
        }
    }
    private static void AddInterceptor(
        ICollection<MemberDeclarationSyntax> classMembers,
        QualifiedNameSyntax actualLabNs)
    {
        const string interceptorFieldName = "_interceptor";
        var interceptorType = QualifiedName(actualLabNs, IdentifierName(InterceptorTypeName));
        var interceptorField = FieldDeclaration(
                VariableDeclaration(NullableType(interceptorType))
                    .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(interceptorFieldName)))))
            .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));
        classMembers.Add(interceptorField);

        var interceptorProperty =
            PropertyDeclaration(interceptorType, Identifier(InterceptorPropertyName))
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                .WithAccessorList(
                    AccessorList(
                        SingletonList<AccessorDeclarationSyntax>(
                            AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithBody(
                                    Block(
                                        IfStatement(
                                            BinaryExpression(
                                                SyntaxKind.EqualsExpression,
                                                IdentifierName(interceptorFieldName),
                                                LiteralExpression(
                                                    SyntaxKind.NullLiteralExpression)),
                                            ThrowStatement(
                                                ObjectCreationExpression(
                                                        IdentifierName(nameof(InvalidOperationException)))
                                                    .WithArgumentList(
                                                        ArgumentList(
                                                            SingletonSeparatedList<ArgumentSyntax>(
                                                                Argument(
                                                                    LiteralExpression(
                                                                        SyntaxKind.StringLiteralExpression,
                                                                        Literal(
                                                                            "Bind Proxy with Interceptor first.")))))))),
                                        ReturnStatement(
                                            IdentifierName(interceptorFieldName)))))));
        classMembers.Add(interceptorProperty);

        const string interceptorParameterName = "interceptor";
        var bindInterceptorMethod = MethodDeclaration(
                PredefinedType(Token(SyntaxKind.VoidKeyword)),
                Identifier(ProxyInterfaceBindMethodName))
            .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(QualifiedName(actualLabNs, IdentifierName(ProxyInterfaceTypeName))))
            .WithParameterList(ParameterList(
                    SingletonSeparatedList(
                        Parameter(Identifier(interceptorParameterName))
                            .WithType(interceptorType))))
            .WithBody(
                Block(
                    IfStatement(
                        BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            IdentifierName(interceptorFieldName),
                            LiteralExpression(
                                SyntaxKind.NullLiteralExpression)),
                        ThrowStatement(
                            ObjectCreationExpression(
                                    QualifiedName(IdentifierName("System"), IdentifierName("InvalidOperationException")))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList<ArgumentSyntax>(
                                            Argument(
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal("Interceptor is bound already.")))))))),
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            IdentifierName(interceptorFieldName),
                            BinaryExpression(
                                SyntaxKind.CoalesceExpression,
                                IdentifierName(interceptorParameterName),
                                ThrowExpression(
                                    ObjectCreationExpression(
                                            IdentifierName("ArgumentNullException"))
                                        .WithArgumentList(
                                            ArgumentList(
                                                SingletonSeparatedList<ArgumentSyntax>(
                                                    Argument(
                                                        LiteralExpression(
                                                            SyntaxKind.StringLiteralExpression,
                                                            Literal(interceptorParameterName))))))))))));
        classMembers.Add(bindInterceptorMethod);
    }

    private static SeparatedSyntaxList<TNode> CommaSeparatedList<TNode>(params TNode[] nodes) where TNode : SyntaxNode
        => CommaSeparatedList((IEnumerable<TNode>)nodes);

    private static SeparatedSyntaxList<TNode> CommaSeparatedList<TNode>(IEnumerable<TNode> nodes) where TNode : SyntaxNode
    {
        var list = new List<SyntaxNodeOrToken>();
        foreach (var nodeOrToken in nodes) {
            if (list.Count > 0)
                list.Add(Token(SyntaxKind.CommaToken));
            list.Add(nodeOrToken);
        }
        return SeparatedList<TNode>(NodeOrTokenList(list));
    }

    private static IdentifierNameSyntax VarIdentifier()
        => IdentifierName(
            Identifier(
                TriviaList(),
                SyntaxKind.VarKeyword,
                "var",
                "var",
                TriviaList()));

    public sealed record ProxyTypeInfo
    {
        public ClassDeclarationSyntax ClassDef { get; init; } = null!;
    }
}
