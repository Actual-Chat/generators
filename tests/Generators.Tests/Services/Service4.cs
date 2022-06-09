namespace ActualLab.Generators.Tests;

[AutoInject]
public sealed partial class Service4 : ExtService1
{
    [Injected] private int XValue { get; }
}
