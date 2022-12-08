using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ActualLab.Generators;

[Generator]
public class ProxyGenerator : IIncrementalGenerator
{
    private const string AbstractionsNamespaceName = "ActualLab.Generators";
    private const string GenerateProxyAttributeFullName = $"{AbstractionsNamespaceName}.GenerateProxyAttribute";
    private const string ProxyInterfaceTypeName = "IProxy";
    private const string ProxyInterfaceBindMethodName = "Bind";
    private const string ProxyClassSuffix = "Proxy";
    private const string InterceptorTypeName = "Interceptor";
    private const string InterceptorPropertyName = "Interceptor";
    private const string InterceptMethodName = "Intercept";
    private const string ArgumentListTypeName = "ArgumentList";
    private const string ArgumentListNewMethodName = "New";
    private const string SubjectFieldName = "_subject";

    private ITypeSymbol? _generateProxyAttributeType;
    private readonly QualifiedNameSyntax _actualLabNs;

    public ProxyGenerator()
        => _actualLabNs = QualifiedName(IdentifierName("ActualLab"), IdentifierName("Generators"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var items = context.SyntaxProvider
            .CreateSyntaxProvider(CouldBeAugmented, MustAugment)
            .Where(i => i.TypeDef != null)
            .Collect();
        context.RegisterSourceOutput(items, Generate!);
    }

    private bool CouldBeAugmented(SyntaxNode node, CancellationToken cancellationToken)
        => node is ClassDeclarationSyntax or InterfaceDeclarationSyntax {
            Parent: FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax
        }; // Top-level type

    private (SemanticModel SemanticModel, TypeDeclarationSyntax? TypeDef)
        MustAugment(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;
        _generateProxyAttributeType ??= compilation.GetTypeByMetadataName(GenerateProxyAttributeFullName)!;

        var typeDef = (TypeDeclarationSyntax) context.Node;
        var generateCtorAttrDef = semanticModel.GetAttribute(_generateProxyAttributeType!, typeDef.AttributeLists);
        return generateCtorAttrDef == null ? default : (semanticModel, typeDef);
    }

    private void Generate(
        SourceProductionContext context,
        ImmutableArray<(SemanticModel SemanticModel, TypeDeclarationSyntax TypeDef)> items)
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
        ImmutableArray<(SemanticModel SemanticModel, TypeDeclarationSyntax TypeDef)> items)
    {
        foreach (var item in items) {
            var code = GenerateCode(context, item.SemanticModel, item.TypeDef);
            var typeType = (ITypeSymbol)item.SemanticModel.GetDeclaredSymbol(item.TypeDef)!;
            context.AddSource($"{typeType.ContainingNamespace}.{typeType.Name}Proxy.g.cs", code);
            context.ReportDiagnostic(GenerateProxyTypeProcessedInfo(item.TypeDef));
        }
    }

    private string GenerateCode(SourceProductionContext context, SemanticModel semanticModel, TypeDeclarationSyntax typeDef)
    {
        context.ReportDiagnostic(DebugWarning($"About to generate proxy for '{typeDef.Identifier.Text}'."));
        var originalClassDef = typeDef as ClassDeclarationSyntax;
        var ns = typeDef.GetNamespaceName();

        var originalTypeNameSyntax = IdentifierName(typeDef.Identifier.Text);
        var originalTypeFullNameSyntax = ns != null ? (NameSyntax)QualifiedName(ns, originalTypeNameSyntax) : originalTypeNameSyntax;
        var classDef = ClassDeclaration(originalTypeNameSyntax.Identifier.Text + ProxyClassSuffix)
            .WithBaseList(BaseList(CommaSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(originalTypeFullNameSyntax),
                SimpleBaseType(QualifiedName(_actualLabNs, IdentifierName(ProxyInterfaceTypeName))))
            ));
        if (originalClassDef != null) {
            classDef = classDef
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithTypeParameterList(originalClassDef.TypeParameterList);
        }
        else {
            classDef = classDef
                .AddModifiers(Token(SyntaxKind.PublicKeyword));
        }

        var classMembers = new List<MemberDeclarationSyntax>();

        AddMethodOverrides(classMembers, context, typeDef);

        context.ReportDiagnostic(DebugWarning($"{classMembers.Count} class members added for method overrides."));

        AddInterceptor(classMembers);

        if (originalClassDef != null) {
            AddClassConstructors(classMembers, originalClassDef, classDef.Identifier.Text);
        }
        else {
            AddSubjectField(classMembers, originalTypeFullNameSyntax);
            AddInterfaceProxyConstructor(classMembers, originalTypeFullNameSyntax, classDef.Identifier.Text);
        }

        if (classMembers.Count > 0)
            classDef = classDef.WithMembers(List(classMembers));

        // Building Compilation unit

        var syntaxRoot = semanticModel.SyntaxTree.GetRoot();
        var unit = CompilationUnit()
            .AddUsings(syntaxRoot.ChildNodes().OfType<UsingDirectiveSyntax>().ToArray())
            .AddMembers(FileScopedNamespaceDeclaration(ns!).AddMembers(classDef));

        var code = unit.NormalizeWhitespace().ToFullString();
        return "// Generated code" + Environment.NewLine +
            "#nullable enable" + Environment.NewLine +
            code;
    }

    private void AddInterfaceProxyConstructor(List<MemberDeclarationSyntax> classMembers, NameSyntax interfaceFullNameSyntax, string className)
    {
        const string subjectCtorParameterName = "subject";
        var ctorDef = ConstructorDeclaration(Identifier(className))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                ParameterList(
                    SingletonSeparatedList(
                        Parameter(Identifier(subjectCtorParameterName))
                            .WithType(interfaceFullNameSyntax))))
            .WithBody(
                Block(
                    SingletonList<StatementSyntax>(
                        ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ThisExpression(),
                                    IdentifierName(SubjectFieldName)),
                                IdentifierName(subjectCtorParameterName))))));
        classMembers.Add(ctorDef);
    }

    private void AddSubjectField(ICollection<MemberDeclarationSyntax> classMembers, NameSyntax interfaceFullNameSyntax)
    {
        var subjectField = FieldDeclaration(
                VariableDeclaration(interfaceFullNameSyntax)
                    .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(SubjectFieldName)))))
            .WithModifiers(TokenList(new[] {
                        Token(SyntaxKind.PrivateKeyword),
                        Token(SyntaxKind.ReadOnlyKeyword)
                    }));
        classMembers.Add(subjectField);
    }

    private static void AddClassConstructors(ICollection<MemberDeclarationSyntax> classMembers,
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

    private void AddMethodOverrides(
        ICollection<MemberDeclarationSyntax> classMembers,
        SourceProductionContext context,
        TypeDeclarationSyntax originalClassDef)
    {
        var cachedInterceptedIndex = 0;
        var isInterfaceProxy = originalClassDef is InterfaceDeclarationSyntax;
        var methodSubjectCall = !isInterfaceProxy
            ? (ExpressionSyntax)BaseExpression() : IdentifierName("_subject");

        foreach (var method in originalClassDef.Members.OfType<MethodDeclarationSyntax>()) {
            var modifiers = method.Modifiers;
            SyntaxToken[] methodModifiers;
            if (!isInterfaceProxy) {
                var isPublic = modifiers.Any(c => c.IsKind(SyntaxKind.PublicKeyword));
                var isProtected = modifiers.Any(c => c.IsKind(SyntaxKind.ProtectedKeyword));
                var isPrivate = !isPublic && !isProtected;
                if (isPrivate)
                    continue;
                var isVirtual = modifiers.Any(c => c.IsKind(SyntaxKind.VirtualKeyword));
                if (!isVirtual) {
                    context.ReportDiagnostic(DebugWarning($"method '{method.ToString()}' is not virtual and not private."));
                    continue;
                }
                var accessModifier = isPublic ? Token(SyntaxKind.PublicKeyword)
                    : isProtected ? Token(SyntaxKind.ProtectedKeyword)
                    : throw new InvalidOperationException("Wrong access modifer");
                methodModifiers = new [] {
                    accessModifier, Token(SyntaxKind.SealedKeyword), Token(SyntaxKind.OverrideKeyword)
                };
            }
            else {
                methodModifiers = new [] { Token(SyntaxKind.PublicKeyword) };
            }

            var cachedInterceptedFieldName = "_cachedIntercepted" + cachedInterceptedIndex++;

            var fieldType = NullableType(
                GenericName(Identifier("Func"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            CommaSeparatedList(
                                  QualifiedName(_actualLabNs, IdentifierName(ArgumentListTypeName)),
                                method.ReturnType
                            ))));
            var cachedInterceptedField = FieldDeclaration(
                VariableDeclaration(fieldType)
                    .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(cachedInterceptedFieldName))))
                ).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));


            var typedArgsVarGenericArguments = method.ParameterList
                .Parameters.Select(p => p.Type!).ToArray();

            var typeArgsVariableType = QualifiedName(_actualLabNs,
                GenericName(Identifier(ArgumentListTypeName))
                        .WithTypeArgumentList(
                            TypeArgumentList(CommaSeparatedList(typedArgsVarGenericArguments))));

            var typedArgsVariable = VariableDeclarator(Identifier("typedArgs"))
                .WithInitializer(EqualsValueClause(
                    CastExpression(typeArgsVariableType, IdentifierName("args")
                    )));
            var baseCallArguments = new List<SyntaxNodeOrToken>();
            for (int itemId = 0; itemId < method.ParameterList.Parameters.Count; itemId++) {
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
                                methodSubjectCall,
                                IdentifierName(method.Identifier.Text)))
                        .WithArgumentList(ArgumentList(SeparatedList<ArgumentSyntax>(baseCallArguments)))));

            var lambdaExpression = SimpleLambdaExpression(Parameter(Identifier("args")))
                .WithBlock(interceptedBlock);

            var argumentListFactoryMethodParams = new List<ArgumentSyntax>();
            foreach (var parameter in method.ParameterList.Parameters)
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
                                                IdentifierName(cachedInterceptedFieldName),
                                                lambdaExpression)))))),
                ReturnStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(InterceptorTypeName),
                                IdentifierName(InterceptMethodName)))
                        .WithArgumentList(
                            ArgumentList(
                                CommaSeparatedList(
                                    Argument(IdentifierName(interceptedVariableIdentifier.Text)),
                                    Argument(
                                        InvocationExpression(
                                                MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    QualifiedName(_actualLabNs, IdentifierName(ArgumentListTypeName)),
                                                    IdentifierName(ArgumentListNewMethodName)))
                                            .WithArgumentList(
                                                ArgumentList(CommaSeparatedList(argumentListFactoryMethodParams))))
                                )))));

            var interceptedMethod = MethodDeclaration(method.ReturnType, method.Identifier)
                .WithModifiers(TokenList(methodModifiers))
                .WithParameterList(method.ParameterList)
                .WithBody(methodBody);

            classMembers.Add(cachedInterceptedField);
            classMembers.Add(interceptedMethod);
        }
    }
    private void AddInterceptor(ICollection<MemberDeclarationSyntax> classMembers)
    {
        const string interceptorFieldName = "_interceptor";
        var interceptorType = QualifiedName(_actualLabNs, IdentifierName(InterceptorTypeName));
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
            .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(QualifiedName(_actualLabNs, IdentifierName(ProxyInterfaceTypeName))))
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
