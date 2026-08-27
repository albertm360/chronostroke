using System.Diagnostics;
using ChronoStroke.Interop;

namespace ChronoStroke;

/// <summary>
/// Holds the claim to being the only running copy of ChronoStroke, and releases it on dispose.
/// </summary>
/// <remarks>
/// Two copies is not merely untidy here. The second one loses the RegisterHotKey race and reports
/// the combination as taken by "another application" — true, and completely baffling, because the
/// other application is ChronoStroke. Worse, the second copy can still be started with the mouse,
/// and then both are injecting into whatever has focus with only one hotkey between them able to
/// stop either.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// The Local prefix scopes the claim to one logon session, which is the right boundary: two
    /// users switched between on the same machine have separate input queues and separate hotkey
    /// registrations, so one copy each is correct rather than one copy between them. The GUID is
    /// there so the name cannot collide with an unrelated program's.
    /// </summary>
    private const string DefaultName = @"Local\ChronoStroke-6f1c7a24-8f5e-4a3b-9d20-5c7e1b84af10";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <inheritdoc cref="TryAcquire(string, out SingleInstance?)"/>
    public static bool TryAcquire(out SingleInstance? instance) =>
        TryAcquire(DefaultName, out instance);

    /// <summary>
    /// Claims the name if no other process holds it.
    /// </summary>
    /// <remarks>
    /// The mutex is never waited on and never owned — only its existence is the signal, which is
    /// what keeps this free of abandoned-mutex handling. The kernel object lives exactly as long
    /// as some process holds a handle to it, so a copy that is killed rather than closed releases
    /// the claim just the same.
    /// <para>
    /// The name is a parameter so the tests can use one of their own; nothing in the app passes it.
    /// </para>
    /// </remarks>
    internal static bool TryAcquire(string name, out SingleInstance? instance)
    {
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            instance = null;
            return false;
        }

        instance = new SingleInstance(mutex);
        return true;
    }

    /// <summary>
    /// Brings the copy that is already running to the front, so starting ChronoStroke twice looks
    /// like being taken back to it rather than like nothing happening.
    /// </summary>
    public static void ActivateExisting()
    {
        using var current = Process.GetCurrentProcess();

        foreach (var other in Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                // MainWindowHandle is zero for a copy still on its way up. Nothing useful can be
                // done about that from here — the claim is already lost either way — so it is
                // skipped rather than waited for.
                if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                NativeMethods.ShowWindow(other.MainWindowHandle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    public void Dispose() => _mutex.Dispose();
}
