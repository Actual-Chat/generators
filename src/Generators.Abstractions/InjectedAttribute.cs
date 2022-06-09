namespace ActualLab.Generators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class InjectedAttribute : Attribute
{
    public bool IsOptional { get; set; }
}
