using System.Threading.Channels;

namespace ActualLab.Generators.Tests;

[AutoInject]
public partial class ExtService1
{
    [Injected] public string V0 { get; private set; } = "";
    [Injected] Channel<int> TestChannel { get; set; } 

    protected void Initialize()
    {
        if (string.IsNullOrEmpty(V0))
            V0 = "Hey!";
    }
}
