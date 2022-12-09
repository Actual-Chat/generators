namespace ActualLab.Generators;

public class Interceptor
{
    public TResult Intercept<TResult>(Invocation invocation)
    {
        OnBefore();
        TResult result;
        Exception? error = null;
        try {
            var func = (Func<ArgumentList,TResult>)invocation.Delegate;
            result = func(invocation.Arguments);
            return result;
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            OnAfter(error);
        }
    }

    public void Intercept(Invocation invocation)
    {
        OnBefore();
        Exception? error = null;
        try {
            var action = (Action<ArgumentList>)invocation.Delegate;
            action(invocation.Arguments);
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            OnAfter(error);
        }
    }

    public TResult Intercept<TResult>(Func<ArgumentList, TResult> intercepted, ArgumentList args)
    {
        OnBefore();
        TResult result;
        Exception? error = null;
        try {
            result = intercepted(args);
            return result;
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            OnAfter(error);
        }
    }

    public TResult Intercept<TInstance, TResult>(Func<ArgumentList, TResult> intercepted, MethodInfo methodInfo, TInstance subject, ArgumentList args)
    {
        OnBefore();
        TResult result;
        Exception? error = null;
        try {
            result = intercepted(args);
            return result;
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            OnAfter(error);
        }
    }

    public void Intercept<TInstance>(Action<ArgumentList> intercepted, MethodInfo methodInfo, TInstance subject, ArgumentList args)
    {
        OnBefore();
        Exception? error = null;
        try {
            intercepted(args);
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            OnAfter(error);
        }
    }

    protected void OnBefore()
    {
    }

    protected void OnAfter(Exception? error)
    {
    }
}

public interface IProxy
{
    void Bind(Interceptor interceptor);
}
