namespace ActualLab.Generators.Tests;

[GenerateProxy]
public interface IChats
{
    Task<int> Foo(int a, string b);
}

[GenerateProxy]
public class Chats : IChats
{
    public virtual Task<int> Foo(int a, string b)
    {
        return Task.FromResult(a);
    }

    public virtual Task<System.Type> Foo2(System.Int32 a)
    {
        return Task.FromResult(a.GetType());
    }

    public string Boo(string x)
    {
        return x;
    }

    public Chats(int x)
    {
    }
}

public class ChatsProxyExample : Chats, IProxy
{
    private Interceptor? _interceptor;
    private Func<ArgumentList, Task<int>>? _cachedIntercepted0;
    private System.Reflection.MethodInfo? _cachedMethodInfo0;

    private Interceptor Interceptor {
        get {
            if (_interceptor == null)
                throw new InvalidOperationException("Bind Proxy with Interceptor first.");
            return _interceptor;
        }
    }

    public sealed override Task<int> Foo(int a, string b)
    {
        var intercepted = _cachedIntercepted0 ??= args => {
            var typedArgs = (ArgumentList<int, string>)args;
            return base.Foo(typedArgs.Item0, typedArgs.Item1);
        };
        return Interceptor.Intercept(intercepted, ArgumentList.New(a, b));
    }

    public Task<int> Foo2(int a, string b)
    {
        var methodInfo = _cachedMethodInfo0 ??= typeof(Chats).GetMethod("Foo",
            GenerateProxyHelper.GetMethodBindingFlags,
            new [] { typeof(int), typeof(string) });
        var intercepted = _cachedIntercepted0 ??= args => {
            var typedArgs = (ArgumentList<int, string>)args;
            return base.Foo(typedArgs.Item0, typedArgs.Item1);
        };
        var invocation = new Invocation(this, methodInfo!, intercepted, ArgumentList.New(a, b));
        return Interceptor.Intercept<Task<int>>(invocation);
    }

    void IProxy.Bind(Interceptor interceptor)
    {
        if (_interceptor != null)
            throw new InvalidOperationException("Interceptor is bound already.");
        _interceptor = interceptor ?? throw new ArgumentNullException("interceptor");
    }

    public ChatsProxyExample(int x)
        :base(x)
    {
    }
}

public class IChatsProxyExample : IChats, IProxy
{
    private readonly IChats _subject;
    private Interceptor? _interceptor;
    private Func<ArgumentList, Task<int>>? _cachedIntercepted0;
    private System.Reflection.MethodInfo? _cachedMethodInfo0;

    private Interceptor Interceptor {
        get {
            if (_interceptor == null)
                throw new InvalidOperationException("Bind Proxy with Interceptor first.");
            return _interceptor;
        }
    }

    public Task<int> Foo(int a, string b)
    {
        var methodInfo = _cachedMethodInfo0 ??= typeof(Chats).GetMethod("Foo",
            GenerateProxyHelper.GetMethodBindingFlags,
            new [] {typeof(int), typeof(string)});
        var intercepted = _cachedIntercepted0 ??= args => {
            var typedArgs = (ArgumentList<int, string>)args;
            return _subject.Foo(typedArgs.Item0, typedArgs.Item1);
        };
        return Interceptor.Intercept(intercepted, methodInfo!, _subject, ArgumentList.New(a, b));
    }

    void IProxy.Bind(Interceptor interceptor)
    {
        if (_interceptor != null)
            throw new InvalidOperationException("Interceptor is bound already.");
        _interceptor = interceptor ?? throw new ArgumentNullException("interceptor");
    }

    public IChatsProxyExample(IChats subject)
    {
        this._subject = subject;
    }
}
