namespace Tractus.HtmlToNdi.Chromium;

//https://github.com/cefsharp/CefSharp.MinimalExample/blob/master/CefSharp.MinimalExample.OffScreen/AsyncContext.cs
public static class AsyncContext
{
    public static void Run(Func<Task> func)
    {
        var prevCtx = SynchronizationContext.Current;

        try
        {
            var syncCtx = new SingleThreadSynchronizationContext();

            SynchronizationContext.SetSynchronizationContext(syncCtx);

            var t = func();

            t.ContinueWith(delegate
            {
                syncCtx.Complete();
            }, TaskScheduler.Default);

            syncCtx.RunOnCurrentThread();

            t.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }
}
