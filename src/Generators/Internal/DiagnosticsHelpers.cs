namespace ActualLab.Generators.Internal;

public static class DiagnosticsHelpers
{
    private static readonly DiagnosticDescriptor DebugDescriptor = new(
        id: "ALGDEBUG",
        title: "Debug warning",
        messageFormat: "Debug warning: {0}",
        category: nameof(AutoInjectGenerator),
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoInjectTypeProcessedDescriptor = new(
        id: "ALG0001",
        title: "[AutoInject]: class processed.",
        messageFormat: "[AutoInject]: class '{0}' is processed.",
        category: nameof(AutoInjectGenerator),
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoInjectTypeMustBePartialDescriptor = new(
        id: "ALG0002",
        title: "[AutoInject]: The class must be partial.",
        messageFormat: "[AutoInject]: Class '{0}' must be declared as partial.",
        category: nameof(AutoInjectGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoInjectTypeHasConstructorDescriptor = new(
        id: "ALG0003",
        title: "[AutoInject]: The class shouldn't have non-generated constructors.",
        messageFormat: "[AutoInject]: Class '{0}' shouldn't have any non-generated constructors.",
        category: nameof(AutoInjectGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoInjectTypeHasWrongInitializeDescriptor = new(
        id: "ALG0004",
        title: "[AutoInject]: Initialize(...) method should be either private (on sealed type) or protected.",
        messageFormat: "[AutoInject]: Class '{0}' Initialize(...) method should be either private (on sealed type) or protected.",
        category: nameof(AutoInjectGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Diagnostics

    public static Diagnostic DebugWarning(string text, Location? location = null) 
        => Diagnostic.Create(DebugDescriptor, location ?? Location.None, text);

    public static Diagnostic DebugWarning(Exception e)
    {
#if !NETSTANDARD2_0
        var text = (e.ToString() ?? "")
            .Replace("\r\n", " | ", StringComparison.Ordinal)
            .Replace("\n", " | ", StringComparison.Ordinal);
#else 
        var text = (e.ToString() ?? "")
            .Replace("\r\n", " | ")
            .Replace("\n", " | ");
#endif
        return DebugWarning(text);
    }

    public static Diagnostic AutoInjectTypeProcessedInfo(ClassDeclarationSyntax classDef) 
        => Diagnostic.Create(AutoInjectTypeProcessedDescriptor, classDef.GetLocation(), classDef.Identifier.Text);

    public static Diagnostic AutoInjectTypeMustBePartialError(ClassDeclarationSyntax classDef)
        => Diagnostic.Create(AutoInjectTypeMustBePartialDescriptor, classDef.GetLocation(), classDef.Identifier.Text);

    public static Diagnostic AutoInjectTypeHasConstructorError(ClassDeclarationSyntax classDef)
        => Diagnostic.Create(AutoInjectTypeHasConstructorDescriptor, classDef.GetLocation(), classDef.Identifier.Text);

    public static Diagnostic AutoInjectTypeHasWrongInitializeError(ClassDeclarationSyntax classDef)
        => Diagnostic.Create(AutoInjectTypeHasWrongInitializeDescriptor, classDef.GetLocation(), classDef.Identifier.Text);
}
