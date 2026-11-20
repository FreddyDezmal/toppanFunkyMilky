using Milkshake.Domain.Auditing;

namespace Milkshake.Application.Abstractions;

/// <summary>
/// Marks the code that runs inside it as done by a named system process (M11), for the audit trail:
/// <code>using IDisposable actor = AuditActorScope.Begin(AuditActors.PaymentReconciliation);</code>
/// It flows into awaited calls, is restored when disposed, and always wins over any signed-in user, so background work
/// is never attributed to a browser user.
/// </summary>
public static class AuditActorScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The system actor in effect, or null in ordinary request code.</summary>
    public static string? Current => Ambient.Value;

    public static IDisposable Begin(string systemActor)
    {
        if (!AuditActors.IsSystem(systemActor))
        {
            throw new ArgumentException("Only a named system actor (system:…) can be set.", nameof(systemActor));
        }

        string? previous = Ambient.Value;
        Ambient.Value = systemActor;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
