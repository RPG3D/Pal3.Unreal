namespace Engine.Coroutine
{
    using System;
    using UnityEngine;

    internal static class CoroutineWaitBackend
    {
        // The scheduler starts a new scaled wait each time this duration is yielded.
        internal static object WaitForSeconds(float seconds) => seconds;

        internal static object WaitUntil(Func<bool> predicate) => new PredicateWait(predicate);

        private sealed class PredicateWait(Func<bool> predicate) : CustomYieldInstruction
        {
            public override bool keepWaiting => !predicate();
        }
    }
}
