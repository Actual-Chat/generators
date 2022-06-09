namespace ActualLab.Generators.Tests;

[AutoInject]
public sealed partial class Service2 : ExtService0
{
    [Injected] private int XValue { get; }

    private void Initialize(int newStuff, int i2, int i1)
    {
        Console.WriteLine($"{nameof(Service2)}: {newStuff}, XValue = {XValue}");
    }
}
