namespace ActualLab.Generators.Tests;

public class Service0
{
    public object Obj { get; }

    // public Service0(SkipInitialize @skipInitialize) 
        // =>  V0 = skipInitialize.ToString()!;
    public Service0(object obj)
        => Obj = obj;
}
