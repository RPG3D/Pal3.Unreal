using System;
using System.Threading;
using System.Threading.Tasks;
using UnrealSharp;

namespace UnityEngine
{
    public static class CoroutineCompatibility
    {
        public static async Task WaitForSeconds(float seconds, CancellationToken ct = default)
            => await Task.Delay((int)(seconds * 1000), ct).ConfigureWithUnrealContext();
        public static async Task NextFrame(CancellationToken ct = default)
        { await Task.Yield(); ct.ThrowIfCancellationRequested(); }
        public static async Task WaitUntil(Func<bool> predicate, CancellationToken ct = default)
        { while (!predicate()) { ct.ThrowIfCancellationRequested(); await Task.Yield(); } }
    }
}
