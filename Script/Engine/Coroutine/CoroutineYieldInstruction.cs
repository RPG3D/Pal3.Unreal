using System;
using System.Threading.Tasks;

namespace Engine.Coroutine
{
    public static class CoroutineYieldInstruction
    {
        public static async Task WaitForSeconds(float seconds)
            => await UnityEngine.CoroutineCompatibility.WaitForSeconds(seconds);
        public static async Task WaitUntil(Func<bool> predicate)
            => await UnityEngine.CoroutineCompatibility.WaitUntil(predicate);
    }
}
