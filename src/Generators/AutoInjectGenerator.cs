using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ActualLab.Generators;

[Generator]
public class AutoInjectGenerator : IIncrementalGenerator
{
    private static readonly string AbstractionsNamespaceName = "ActualLab.Generators";
    private static readonly string AutoInjectAttributeFullName = $"{AbstractionsNamespaceName}.AutoInjectAttribute";
    private static readonly string InjectedAttributeFullName = $"{AbstractionsNamespaceName}.InjectedAttribute";
    private static readonly string IsOptionalPropertyName = "IsOptional";
    private static readonly string SkipInitializeFullName = $"{AbstractionsNamespaceName}.SkipInitialize";
    private static readonly string InitializeMethodName = "Initialize";
    private static readonly string SkipInitializeParameterName = "@skipInitialize";

    private ITypeSymbol? _autoInjectAttributeType;
    private ITypeSymbol? _injectedAttributeType;
    private ITypeSymbol? _skipInitializeType;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var items = context.SyntaxProvider
            .CreateSyntaxProvider(CouldBeAugmented, MustAugment)
            .Where(i => i.ClassDef != null)
            .Collect();
        context.RegisterSourceOutput(items, Generate!);
    }

    private bool CouldBeAugmented(SyntaxNode node, CancellationToken cancellationToken) 
        => node is ClassDeclarationSyntax s 
            && s.Parent is FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax; // Top-level type

    private (SemanticModel SemanticModel, ClassDeclarationSyntax? ClassDef) 
        MustAugment(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;
        _autoInjectAttributeType ??= compilation.GetTypeByMetadataName(AutoInjectAttributeFullName)!;
        _injectedAttributeType ??= compilation.GetTypeByMetadataName(InjectedAttributeFullName)!;
        _skipInitializeType ??= compilation.GetTypeByMetadataName(SkipInitializeFullName)!;

        var classDef = (ClassDeclarationSyntax) context.Node;
        var generateCtorAttrDef = semanticModel.GetAttribute(_autoInjectAttributeType!, classDef.AttributeLists);
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
            context.ReportDiagnostic(DebugWarning($"Found {items.Length} type(s) to process."));
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
        var knownTypes = new HashSet<string>(
            items.Select(i => i.SemanticModel.GetDeclaredSymbol(i.ClassDef)!.ToFullName()),
            StringComparer.Ordinal);
        var processedTypes = new Dictionary<string, AutoInjectTypeInfo>(StringComparer.Ordinal);

        var queue = new Queue<(SemanticModel SemanticModel, ClassDeclarationSyntax ClassDef)>(items);
        while (queue.Count > 0) {
            var item = queue.Dequeue();
            var typeInfo = BuildTypeInfo(item.SemanticModel, item.ClassDef);
            if (typeInfo == null) {
                // Will retry later - we should generate base class code first
                queue.Enqueue(item);
                continue;
            }
            var code = GenerateCode(item.SemanticModel, typeInfo);
            var classType = (ITypeSymbol) item.SemanticModel.GetDeclaredSymbol(item.ClassDef)!;
            context.AddSource($"{classType.ContainingNamespace}.{classType.Name}.g.cs", code);
            context.ReportDiagnostic(AutoInjectTypeProcessedInfo(item.ClassDef));
        }

        AutoInjectTypeInfo? BuildTypeInfo(SemanticModel semanticModel, ClassDeclarationSyntax classDef)
        {
            if (!classDef.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) {
                context.ReportDiagnostic(AutoInjectTypeMustBePartialError(classDef));
                return null;
            }
            if (classDef.Members.OfType<ConstructorDeclarationSyntax>().Any()) {
                context.ReportDiagnostic(AutoInjectTypeHasConstructorError(classDef));
                return null;
            }

            var classType = (ITypeSymbol) semanticModel.GetDeclaredSymbol(classDef)!;
            var baseType = (
                from b in classDef.BaseList?.Types ?? new SeparatedSyntaxList<BaseTypeSyntax>()
                let bType = semanticModel.GetTypeInfo(b.Type).Type
                where bType.TypeKind == TypeKind.Class
                select bType
            ).FirstOrDefault();
            var baseTypeFullName = baseType?.ToFullName() ?? "";
            processedTypes!.TryGetValue(baseTypeFullName, out var baseTypeInfo);
            if (baseTypeInfo == null && knownTypes!.Contains(baseTypeFullName)) {
#if DEBUG
                context.ReportDiagnostic(DebugWarning($"[AutoInject]: postponing '{classType.Name}'."));
#endif
                return null;
            }

            var isAbstract = classDef.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));
            var isSealed = classDef.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword));

            var initMethod = GetInitializeMethod(classType);
            if (initMethod != null) {
                var initMethodIsPublic = initMethod.DeclaredAccessibility == Accessibility.Public;
                var initMethodIsProtected = initMethod.DeclaredAccessibility == Accessibility.Protected
                    || initMethod.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
                var initMethodIsPrivate = !(initMethodIsPublic || initMethodIsProtected);
                if (initMethodIsPublic || (initMethodIsPrivate && !isSealed)) {
                    context.ReportDiagnostic(AutoInjectTypeHasWrongInitializeError(classDef));
                    return null;
                }
            }
            else {
                initMethod = GetInitializeMethod(baseType);
            }

            var deps = new List<DependencyInfo>() {
                new() {
                    Name = SkipInitializeParameterName,
                    VarName = SkipInitializeParameterName.ToVariableName(),
                    TypeDef = _skipInitializeType!.ToTypeName(),
                    Type = _skipInitializeType,
                    Kind = DependencyKind.SkipInit,
                    IsOptional = false,
                }
            };
            deps.AddRange(
                from m in classDef.Members 
                where !m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)) 
                let depAttrDef = semanticModel.GetAttribute(_injectedAttributeType!, m.AttributeLists) 
                where depAttrDef != null 
                let isOptionalArgDef = depAttrDef.GetNamedArgument(IsOptionalPropertyName) 
                let isOptional = isOptionalArgDef?.Expression is LiteralExpressionSyntax e 
                    && e.IsKind(SyntaxKind.TrueLiteralExpression)
                let p = m.GetNameAndType()
                where p.Name is not null
                select new DependencyInfo() {
                    Name = p.Name!, 
                    VarName = p.Name.ToVariableName(), 
                    TypeDef = p.TypeDef,
                    Kind = DependencyKind.PropertyOrField,
                    IsOptional = isOptional,
                }
            );

            if (initMethod != null) {
                deps.AddRange(  
                    from p in initMethod.Parameters
                    let name = p.Name
                    where name is not null
                    select new DependencyInfo() {
                        Name = name,
                        VarName = name.ToVariableName(),
                        TypeDef = p.Type.ToTypeName(),
                        Kind = DependencyKind.Initialize,
                        IsOptional = p.HasExplicitDefaultValue,
                    });
            }

            deps.AddRange(GetBaseDependencies());

            deps = deps
                .OrderBy(d => d.IsOptional)
                .ThenBy(d => d.Kind)
                .DistinctBy(d => d.VarName)
                .ToList();

            var typeInfo = new AutoInjectTypeInfo() {
                ClassDef = classDef,
                ClassType = classType,
                BaseType = baseType,
                BaseTypeInfo = baseTypeInfo,
                IsSealed = isSealed,
                IsAbstract = isAbstract,
                Dependencies = deps.ToImmutableList(),
                InitMethod = initMethod,
            };
            processedTypes[classType.ToFullName()] = typeInfo;
            return typeInfo;

            ImmutableList<DependencyInfo> GetBaseDependencies()
            {
                if (baseType == null)
                    return ImmutableList<DependencyInfo>.Empty;

                if (baseTypeInfo != null)
                    return baseTypeInfo.Dependencies
                        .Select(d => d with { Kind = DependencyKind.BaseClass })
                        .ToImmutableList();

                var baseCtors = baseType.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Constructor)
                    .OrderByDescending(c => c.Parameters.Length)
                    .ToList();
                var baseCtor = baseCtors.FirstOrDefault(
                        c => SymbolEqualityComparer.Default.Equals(
                            _skipInitializeType, 
                            c.Parameters.FirstOrDefault()?.Type)) 
                    ?? baseCtors.FirstOrDefault();
                if (baseCtor == null)
                    return ImmutableList<DependencyInfo>.Empty;

                return (  
                    from p in baseCtor.Parameters
                    let name = p.Name
                    where name is not null
                    select new DependencyInfo() {
                        Name = name,
                        VarName = name.ToVariableName(),
                        TypeDef = p.Type.ToTypeName(),
                        Type = p.Type,
                        Kind = DependencyKind.BaseClass,
                        IsOptional = p.IsOptional,
                    }).ToImmutableList();
            }
        }

        string GenerateCode(SemanticModel semanticModel, AutoInjectTypeInfo typeInfo)
        {
            var classDef = typeInfo.ClassDef;
            var dependencies = typeInfo.Dependencies;
            var baseVarNames = dependencies
                .Where(d => d.Kind is DependencyKind.BaseClass)
                .Select(d => d.VarName)
#if !NETSTANDARD2_0
                .ToHashSet(StringComparer.Ordinal);
#else
                .ToHashSet();
#endif

            var partialClassDef = ClassDeclaration(classDef.Identifier.Text)
                .WithTypeParameterList(classDef.TypeParameterList)
                .AddModifiers(Token(SyntaxKind.PartialKeyword));

            for (var isSkipInitCtorIndex = 0; isSkipInitCtorIndex < 2; isSkipInitCtorIndex++) {
                var isSkipInitCtor = isSkipInitCtorIndex != 0;
                if (typeInfo.IsSealed && isSkipInitCtor)
                    continue;

                var modifier = isSkipInitCtor
                    ? (typeInfo.IsSealed ? SyntaxKind.PrivateKeyword : SyntaxKind.ProtectedKeyword)
                    : (typeInfo.IsAbstract ? SyntaxKind.ProtectedKeyword : SyntaxKind.PublicKeyword); 
                var ctorDef = ConstructorDeclaration(Identifier(classDef.Identifier.Text))
                    .AddModifiers(Token(modifier));

                var baseCall = ConstructorInitializer(SyntaxKind.BaseConstructorInitializer);
                var initializeCall = typeInfo.InitMethod != null && !isSkipInitCtor 
                    ? InvocationExpression(IdentifierName(InitializeMethodName))
                    : null;

                foreach (var d in dependencies) {
                    var paramRef = IdentifierName(d.VarName);
                    var paramDef = Parameter(Identifier(d.VarName))
                        .WithType(d.TypeDef);
                    if (d.IsOptional)
                        paramDef = paramDef
                            .WithDefault(EqualsValueClause(DefaultExpression(d.TypeDef)));

                    var isSkipInitParam = d.Kind == DependencyKind.SkipInit;
                    if (baseVarNames.Contains(d.VarName)) // base(..., [expr])
                        baseCall = baseCall.AddArgumentListArguments(Argument(
                            NameColon(paramRef), 
                            default, 
                            isSkipInitParam ? DefaultExpression(d.TypeDef) : paramRef));

                    if (!isSkipInitParam || isSkipInitCtor) // .ctor(..., [param])
                        ctorDef = ctorDef.AddParameterListParameters(paramDef);

                    if (d.Kind is DependencyKind.SkipInit or DependencyKind.BaseClass)
                        continue; // Nothing else to do with @skipInit

                    if (d.Kind == DependencyKind.Initialize) {
                        initializeCall = initializeCall?.AddArgumentListArguments(
                            Argument(NameColon(paramRef), default, paramRef));
                        continue;
                    }

                    if (d.Kind != DependencyKind.PropertyOrField)
                        continue;

                    var lValue = MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression, 
                        ThisExpression(), 
                        IdentifierName(d.Name!));

                    var assignmentStatementDef = ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            lValue,
                            paramRef));
                    var statementDef = d.IsOptional
                        ? (StatementSyntax) IfStatement(
                            BinaryExpression(SyntaxKind.NotEqualsExpression,
                                paramRef,
                                DefaultExpression(d.TypeDef)),
                            assignmentStatementDef)
                        : assignmentStatementDef;

                    ctorDef = ctorDef.AddBodyStatements(statementDef);
                }

                if (baseCall.ArgumentList.Arguments.Count > 0)
                    ctorDef = ctorDef.WithInitializer(baseCall);
                if (initializeCall != null)
                    ctorDef = ctorDef.AddBodyStatements(ExpressionStatement(initializeCall));

                partialClassDef = partialClassDef.AddMembers(ctorDef);
            }

            // Building Compilation unit

            var syntaxRoot = semanticModel.SyntaxTree.GetRoot();
            var unit = CompilationUnit()
                .AddUsings(syntaxRoot.ChildNodes().OfType<UsingDirectiveSyntax>().ToArray())
                .AddMembers(
                    FileScopedNamespaceDeclaration(classDef.GetNamespaceName()!)
                        .AddMembers(partialClassDef));

            var code = unit.NormalizeWhitespace().ToFullString();
            return "// Generated code\r\n" + code;
        }
    }

    private static IMethodSymbol? GetInitializeMethod(ITypeSymbol? type) 
        => type?.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary && !m.IsStatic && Equals(m.Name, InitializeMethodName))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

    public sealed record AutoInjectTypeInfo
    {
        public ClassDeclarationSyntax ClassDef { get; init; } = null!;
        public ITypeSymbol ClassType { get; init; } = null!;
        public ITypeSymbol? BaseType { get; init; }
        public AutoInjectTypeInfo? BaseTypeInfo { get; init; }
        public bool IsSealed { get; init; }
        public bool IsAbstract { get; init; }
        public ImmutableList<DependencyInfo> Dependencies { get; init; } = ImmutableList<DependencyInfo>.Empty;
        public IMethodSymbol? InitMethod { get; init; }
    } 

    public enum DependencyKind { SkipInit, Initialize, PropertyOrField, BaseClass }

    public sealed record DependencyInfo
    {
        public string Name { get; init; } = null!;
        public string VarName { get; init; } = null!;
        public TypeSyntax TypeDef { get; init; } = null!;
        public ITypeSymbol? Type { get; init; }
        public DependencyKind Kind { get; init; }
        public bool IsOptional { get; init; }
    }
}
