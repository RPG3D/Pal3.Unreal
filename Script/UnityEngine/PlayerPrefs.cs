using System.Collections.Generic;

namespace UnityEngine
{
    public static class PlayerPrefs
    {
        static readonly Dictionary<string, int> _ints = new();
        static readonly Dictionary<string, float> _floats = new();
        static readonly Dictionary<string, string> _strings = new();
        public static int GetInt(string k, int d = 0) => _ints.TryGetValue(k, out var v) ? v : d;
        public static void SetInt(string k, int v) => _ints[k] = v;
        public static float GetFloat(string k, float d = 0f) => _floats.TryGetValue(k, out var v) ? v : d;
        public static void SetFloat(string k, float v) => _floats[k] = v;
        public static string GetString(string k, string d = "") => _strings.TryGetValue(k, out var v) ? v : d;
        public static void SetString(string k, string v) => _strings[k] = v;
        public static bool HasKey(string k) => _ints.ContainsKey(k) || _floats.ContainsKey(k) || _strings.ContainsKey(k);
        public static void DeleteKey(string k) { _ints.Remove(k); _floats.Remove(k); _strings.Remove(k); }
        public static void DeleteAll() { _ints.Clear(); _floats.Clear(); _strings.Clear(); }
        public static void Save() { }
    }
}
