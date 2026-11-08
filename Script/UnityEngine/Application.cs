namespace UnityEngine
{
    public static class Application
    {
        public static string persistentDataPath =>
            UnrealSharp.SharpDemo.UProjectStorageLibrary.ResolveWritableSavedDirectory();
        public static string dataPath => System.Environment.CurrentDirectory;
        public static string streamingAssetsPath => System.IO.Path.Combine(System.AppContext.BaseDirectory, "StreamingAssets");
        public static string version => "1.0.0";
        public static bool isPlaying => true;
        public static int targetFrameRate { get; set; } = 60;
        public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
    }
    public enum RuntimePlatform { WindowsPlayer = 0, Android = 1, LinuxPlayer = 2, IPhonePlayer = 3 }
}
