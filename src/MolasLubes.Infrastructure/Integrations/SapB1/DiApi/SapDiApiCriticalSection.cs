namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Serializes SAP DI API COM access within this process.
/// SAP B1 DI API is prone to unmanaged crashes when multiple threads in the
/// same host create/use COM business objects concurrently, even with separate
/// Company instances and STA threads.
/// </summary>
internal static class SapDiApiCriticalSection
{
    private static readonly object Gate = new();

    public static void Run(Action action)
    {
        lock (Gate)
        {
            action();
        }
    }

    public static T Run<T>(Func<T> action)
    {
        lock (Gate)
        {
            return action();
        }
    }
}
