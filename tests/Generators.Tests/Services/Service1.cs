namespace ActualLab.Generators.Tests;

[AutoInject]
public partial class Service1 : Service0
{
    [Injected] protected int IntValue { get; }
    [Injected(IsOptional = true)] protected bool BoolValue { get; } = true;
    [Injected(IsOptional = true)] private readonly string _strValue = "";

    protected void Initialize(int i1, int i2, string obj)
    {
        WriteLine($"{nameof(Service1)}: i1 = {i1}, IntValue = {IntValue}, _strValue = {_strValue}");
    }
}
