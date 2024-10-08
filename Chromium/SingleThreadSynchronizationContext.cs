using System.Collections.Concurrent;

namespace Tractus.HtmlToNdi.Chromium;
// https://github.com/cefsharp/CefSharp.MinimalExample/blob/master/CefSharp.MinimalExample.OffScreen/SingleThreadSynchronizationContext.cs
public sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> queue =
        [];

    public override void Post(SendOrPostCallback d, object state)
    {
        this.queue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
    }

    public void RunOnCurrentThread()
    {
        while (this.queue.TryTake(out var workItem, Timeout.Infinite))
        {
            workItem.Key(workItem.Value);
        }
    }

    public void Complete()
    {
        this.queue.CompleteAdding();
    }
}
