namespace ActualLab.Generators;

public class Interceptor
{
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

    public void Intercept(Action<ArgumentList> intercepted, ArgumentList args)
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

    // public TResult Intercept<TResult>(Func<TResult> intercepted)
    // {
    //     OnBefore();
    //     TResult result;
    //     Exception error = null;
    //     try {
    //         result = intercepted();
    //         return result;
    //     }
    //     catch (Exception e) {
    //         error = e;
    //         throw;
    //     }
    //     finally {
    //         OnAfter(error);
    //     }
    // }
    //
    // public void Intercept(Action intercepted)
    // {
    //     OnBefore();
    //     Exception? error = null;
    //     try {
    //         intercepted();
    //     }
    //     catch (Exception e) {
    //         error = e;
    //         throw;
    //     }
    //     finally {
    //         OnAfter(error);
    //     }
    // }

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
