namespace Lassistant.Extractors;

/// <summary>
/// WinRT OCR APIs require an STA thread. Thread-pool (MTA) calls can hang indefinitely.
/// </summary>
internal static class StaThreadRunner
{
    public static T Run<T>(Func<T> func)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return func();

        T? result = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();

        if (error is not null)
            throw error;

        return result!;
    }
}
