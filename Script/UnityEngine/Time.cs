namespace UnityEngine
{
    public static class Time
    {
        public static float deltaTime { get; set; } = 1f / 60f;
        public static float fixedDeltaTime { get; set; } = 0.02f;
        public static float unscaledDeltaTime { get; set; } = 1f / 60f;
        public static float time { get; set; } = 0f;
        public static float timeScale { get; set; } = 1f;
        public static int frameCount { get; set; } = 0;
    }
}
