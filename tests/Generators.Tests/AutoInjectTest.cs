namespace ActualLab.Generators.Tests;

public class AutoInjectTest
{
    private readonly ITestOutputHelper _out;
    
    public AutoInjectTest(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void BasicTest()
    {
        var services = new ServiceCollection()
            .AddSingleton<Service1>()
            .BuildServiceProvider();
        var service1 = services.GetRequiredService<Service1>();
        _out.WriteLine(service1.ToString());
    }
}
