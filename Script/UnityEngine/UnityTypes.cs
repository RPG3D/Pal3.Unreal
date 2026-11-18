// Stub types needed by Engine files. Thin wrappers; wire to UE in later phases.
namespace UnityEngine
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Base class for anything attached to a game object. Mirrors UnityEngine.Component:
    /// exposes gameObject/transform/enabled plus the GetComponent family.
    /// </summary>
    public class Component : Object
    {
        private GameObject? _gameObject;
        public GameObject gameObject => _gameObject ??= new GameObject();
        private GameObject? ComponentHost => this as GameObject ?? _gameObject;
        protected internal void BindGameObject(GameObject owner) => _gameObject = owner;
        private Transform? _transform;
        public Transform transform => ComponentHost?.HostedTransform ?? (_transform ??= new Transform());
        public virtual bool enabled { get; set; } = true;

        // Lazily-created attached stub components (GetComponent<T> analog). In
        // Unity these would be wired up in the editor; here they are created on
        // demand so UI/layout code paths keep working on stub objects.
        private readonly System.Collections.Generic.Dictionary<Type, object> _stubComponents = new();

        public T GetComponent<T>() where T : class => FindComponent(typeof(T)) as T ?? null!;

        public Component? GetComponent(Type type) => FindComponent(type) as Component;

        private object? FindComponent(Type type)
        {
            GameObject? owner = ComponentHost;
            if (owner != null && owner.TryFindHostedComponent(type, out object? hosted)) return hosted;
            if (_stubComponents.TryGetValue(type, out object? existing)) return existing;
            if (type.IsAbstract || type.IsInterface) return null;
            try
            {
                object created = Activator.CreateInstance(type)!;
                _stubComponents[type] = created;
                return created;
            }
            catch (MissingMethodException)
            {
                return null;
            }
        }

        public T GetComponentInChildren<T>() where T : class
        {
            if (ComponentHost?.FindHostedComponentsInChildren(typeof(T)) is { } hosted)
                return hosted.OfType<T>().FirstOrDefault()!;
            return GetComponent<T>();
        }

        public T[] GetComponentsInChildren<T>() where T : class
        {
            if (ComponentHost?.FindHostedComponentsInChildren(typeof(T)) is { } hosted)
                return hosted.OfType<T>().ToArray();
            return GetComponent<T>() is { } component ? new[] { component } : Array.Empty<T>();
        }
        public T GetOrAddComponent<T>() where T : class => GetComponent<T>();
    }

    public class Transform : Component
    {
        public virtual Vector3 position { get; set; }
        public virtual Vector3 localPosition { get; set; }
        public virtual Quaternion rotation { get; set; }
        public virtual Vector3 localScale { get; set; } = Vector3.one;
        public virtual Vector3 forward => Vector3.forward;
        public virtual void SetParent(Transform? parent, bool worldPositionStays = true) { }
        public virtual void LookAt(Vector3 target) { }
        public Vector3 TransformPoint(Vector3 p) => p;
        public Vector3 InverseTransformPoint(Vector3 p) => p;

        /// <summary>
        /// Enumerates child transforms (supports `foreach (Transform child in transform)`).
        /// Real child traversal is deferred; returns no children for now.
        /// </summary>
        public System.Collections.Generic.IEnumerator<Transform> GetEnumerator()
            => ((System.Collections.Generic.IEnumerable<Transform>)System.Array.Empty<Transform>()).GetEnumerator();
    }

    public class GameObject : Component
    {
        public GameObject() { }
        public GameObject(string n) { name = n; }

        /// <summary>
        /// Real UE actor backing this stub object, when one exists. The port sets it
        /// for the main camera so the game's ITransform writes land on a real actor;
        /// entities created from such a GameObject wrap it (see GameEntity).
        /// </summary>
        public UnrealSharp.Engine.AActor? UeActor;
        public bool activeInHierarchy => true;
        public bool activeSelf => true;

        /// <summary>
        /// Script types attached to this stub prefab. Consumed by the game layer's
        /// Object.InstantiateHook to spawn real managed entities when the prefab
        /// is cloned (Unity prefab-instantiate analog for the boot chain).
        /// </summary>
        public readonly System.Collections.Generic.List<System.Type> PrefabScriptTypes = new();

        private Func<Type, object?>? _addHostedComponent;
        private Func<Type, object?>? _findHostedComponent;
        private Func<Type, IEnumerable<object>>? _findHostedChildren;
        private Action? _destroyHost;
        internal Transform? HostedTransform { get; private set; }

        // UnrealSharp portability. Bind to the same component registry that
        // drives the entity's MonoBehaviour callbacks on the game thread.
        public void BindComponentHost(Func<Type, object?> add, Func<Type, object?> find,
            Func<Type, IEnumerable<object>> findInChildren, Transform transform, Action destroy)
        {
            _addHostedComponent = add;
            _findHostedComponent = find;
            _findHostedChildren = findInChildren;
            _destroyHost = destroy;
            HostedTransform = transform;
            BindComponent(transform);
        }

        public void ReleaseComponentHost()
        {
            // A GameObject can outlive the entity in managed code.
            _addHostedComponent = static _ => null;
            _findHostedComponent = static _ => null;
            _findHostedChildren = static _ => Array.Empty<object>();
            _destroyHost = null;
        }

        public void BindComponent(Component component) => component.BindGameObject(this);

        public new void Destroy() => _destroyHost?.Invoke();

        internal bool TryFindHostedComponent(Type type, out object? component)
        {
            component = _findHostedComponent?.Invoke(type);
            return _findHostedComponent != null;
        }

        internal IEnumerable<object>? FindHostedComponentsInChildren(Type type) => _findHostedChildren?.Invoke(type);

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T))!;

        public Component? AddComponent(Type type)
        {
            if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException("Expected a concrete Unity component type.", nameof(type));
            var component = (_addHostedComponent != null ? _addHostedComponent(type) : Activator.CreateInstance(type)) as Component;
            if (component != null) BindComponent(component);
            return component;
        }

        public void SetActive(bool value) { }
    }

    public class Material : Object
    {
        public Color color;
        public Shader? shader;
        public void SetFloat(string n, float v) { }
        public void SetInt(string n, int v) { }
        public void SetColor(string n, Color v) { }
        public void SetTexture(string n, Texture t) { }
        public void SetVector(string n, Vector4 v) { }
        public float GetFloat(string n) => 0f;
        public int GetInt(string n) => 0;
        public Color GetColor(string n) => Color.white;
        public Texture? GetTexture(string n) => null;
    }

    public class Mesh
    {
        public Vector3[] vertices = Array.Empty<Vector3>();
        public int[] triangles = Array.Empty<int>();
        public Vector2[] uv = Array.Empty<Vector2>();
        public Vector3[] normals = Array.Empty<Vector3>();
        public Vector4[] tangents = Array.Empty<Vector4>();
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }
        public void RecalculateTangents() { }
        // Mv3 vertex animation mutates these buffers every frame; when the mesh is
        // backed by a UE ProceduralMeshSection (wired up by StaticMeshRenderer) the
        // callback re-uploads the section, otherwise animation writes are invisible.
        public System.Action? OnMeshDataChanged;

        public void SetVertices(Vector3[] v) { vertices = v; OnMeshDataChanged?.Invoke(); }
        public void SetTriangles(int[] t, int submesh = 0) { triangles = t; OnMeshDataChanged?.Invoke(); }
        public void SetNormals(Vector3[] n) { normals = n; OnMeshDataChanged?.Invoke(); }
        public void SetTangents(Vector4[] t) { tangents = t; OnMeshDataChanged?.Invoke(); }
        public void SetUVs(int channel, Vector2[] uv2) { uv = uv2; OnMeshDataChanged?.Invoke(); }
        public void Destroy() { OnMeshDataChanged = null; }
    }

    public class Texture : Object { public int width; public int height; public Texture2D? nativeTexture; }

    public class Texture2D : Texture
    {
        public Texture2D() { }
        public Texture2D(int width, int height) { this.width = width; this.height = height; }
    }

    public class Sprite { public Texture? texture; public void Destroy() { } }

    public class MeshFilter { public Mesh? mesh; }

    public class MeshRenderer : Component { public Material? material; public Material[] materials => new[] { material! }; }

    public class SpriteRenderer : Component
    {
        public Sprite? sprite;
        public Material? sharedMaterial;
        public Color color = Color.white;
    }

    public class Skybox { public Material? material; }

    public class Collider : Component
    {
        public Bounds bounds;
        public bool isTrigger;
        public Vector3 center;
    }

    public class BoxCollider : Collider
    {
        public Vector3 size;
    }

    public class CapsuleCollider : Collider
    {
        public float radius;
        public float height;
        public int direction;
    }

    public class MeshCollider : Collider
    {
        public Mesh? sharedMesh;
        public bool convex;
    }

    public class AudioClip
    {
        public string name = "";
        public float length;

        // Compressed bytes handed to the UE audio helper (decoded there via
        // dr_libs / RIFF parser). audioType follows Unity's AudioType enum:
        // 7 = WAV, 6 = MPEG.
        public byte[]? rawData;
        public int audioType;

        public void Destroy()
        {
            rawData = null;
        }

        /// <summary>Builds a clip from the bytes read out of the CPK archives.</summary>
        public static AudioClip FromCompressedData(string clipName, byte[]? data, int type)
        {
            return new AudioClip
            {
                name = clipName,
                rawData = data,
                audioType = type,
                length = data != null ? EstimateLength(data, type) : 0f,
            };
        }

        private static float EstimateLength(byte[] data, int type)
        {
            if (type == 7) // WAV
            {
                int sampleRate = 22050, channels = 2, bits = 16, dataSize = 0;
                for (int pos = 12; pos + 8 <= data.Length;)
                {
                    string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    int size = data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24);
                    int body = pos + 8;
                    if (id == "fmt " && body + 16 <= data.Length)
                    {
                        channels = data[body + 2] | (data[body + 3] << 8);
                        sampleRate = data[body + 4] | (data[body + 5] << 8) |
                                     (data[body + 6] << 16) | (data[body + 7] << 24);
                        bits = data[body + 14] | (data[body + 15] << 8);
                    }
                    else if (id == "data")
                    {
                        dataSize = System.Math.Min(size, System.Math.Max(0, data.Length - body));
                        break;
                    }
                    int advance = 8 + size + ((size & 1) == 1 ? 1 : 0);
                    if (advance <= 8) break;
                    pos += advance;
                }

                int bytesPerSecond = System.Math.Max(1, sampleRate * channels * System.Math.Max(1, bits / 8));
                return dataSize > 0 ? (float)dataSize / bytesPerSecond : 0f;
            }

            // MPEG: rough estimate at 128 kbps (only used for loop pacing).
            return data.Length > 0 ? data.Length / (128f * 1024f / 8f) : 0f;
        }
    }

    public class Camera : Component
    {
        public static Camera? main;
        public float fieldOfView;
        public float nearClipPlane, farClipPlane;
        public int pixelWidth => 1920;
        public int pixelHeight => 1080;
        public Vector3 WorldToScreenPoint(Vector3 p) => p;
    }

    public struct Bounds
    {
        public Vector3 center, extents, size, min, max;
        public Bounds(Vector3 c, Vector3 s)
        {
            center = c; size = s; extents = s * 0.5f; min = c - extents; max = c + extents;
        }
        public void Encapsulate(Bounds b)
        {
            Vector3 nmin = new(Math.Min(min.x, b.min.x), Math.Min(min.y, b.min.y), Math.Min(min.z, b.min.z));
            Vector3 nmax = new(Math.Max(max.x, b.max.x), Math.Max(max.y, b.max.y), Math.Max(max.z, b.max.z));
            min = nmin; max = nmax; center = (nmin + nmax) * 0.5f; size = nmax - nmin; extents = size * 0.5f;
        }
        public void Encapsulate(Vector3 p)
        {
            if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y; if (p.z < min.z) min.z = p.z;
            if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y; if (p.z > max.z) max.z = p.z;
            center = (min + max) * 0.5f; size = max - min; extents = size * 0.5f;
        }
        public Vector3 ClosestPoint(Vector3 point) => new(
            Math.Clamp(point.x, min.x, max.x),
            Math.Clamp(point.y, min.y, max.y),
            Math.Clamp(point.z, min.z, max.z));
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
    }

    /// <summary>UnityEngine.Screen.sleepTimeout value; keeps display awake.</summary>
    public static class SleepTimeout
    {
        public const int NeverSleep = -1;
    }

    public enum FullScreenMode { ExclusiveFullScreen = 0, FullScreenWindow = 1, MaximizedWindow = 2, Windowed = 3 }

    public enum LightType { Spot = 0, Directional = 1, Point = 2, Area = 3 }

    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public class Light : Component
    {
        public LightType type = LightType.Point;
        public float range = 10f;
        public float intensity = 1f;
        public float spotAngle = 30f;
        public Color color = Color.white;
        public LightShadows shadows = LightShadows.None;
        public int cullingMask;
        public float shadowNearPlane = 0.2f;
        public float shadowStrength = 1f;
        public float shadowBias = 0.05f;
    }

    /// <summary>Refresh rate of a display (refreshRateRatio.value is a fractional value).</summary>
    public struct RefreshRate
    {
        public uint numerator;
        public uint denominator;
        public double value => denominator != 0 ? (double)numerator / denominator : 0.0;
    }

    public struct Resolution
    {
        public int width;
        public int height;
        public int refreshRate;
        public RefreshRate refreshRateRatio;
    }

    public static class Screen
    {
        public static int width = 1920;
        public static int height = 1080;
        public static float dpi = 96f;
        public static bool fullScreen = true;
        public static FullScreenMode fullScreenMode = FullScreenMode.FullScreenWindow;
        public static int sleepTimeout = SleepTimeout.NeverSleep;
        public static Resolution currentResolution => new Resolution
        {
            width = 1920,
            height = 1080,
            refreshRate = 60,
            refreshRateRatio = new RefreshRate { numerator = 60, denominator = 1 },
        };
        public static Resolution[] resolutions => new[] { currentResolution };
    }

    public enum GraphicsDeviceType { Direct3D11 = 0, Direct3D12 = 1, Vulkan = 2, Metal = 3, OpenGLES2 = 4, OpenGLES3 = 5 }

    public static class SystemInfo
    {
        public static string deviceModel = "Desktop";
        public static string deviceName = "Desktop";
        public static string operatingSystem = "Windows";
        public static int systemMemorySize = 16384;
        public static int graphicsMemorySize = 8192;
        public static string graphicsDeviceName = "GPU";
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.Direct3D12;
        public static int processorCount = 8;
        public static string processorType = "Unknown CPU";
        public static bool supportsInstancing => true;
    }

    public class ResourceRequest
    {
        public Object? asset => null;
        public bool isDone => false;
        public float progress => 0f;
    }

    public static class Resources
    {
        public static Object? Load(string path) => null;
        public static Object? Load(string path, System.Type systemTypeInstance) => null;
        public static T Load<T>(string path) where T : Object => null!;
        public static T Load<T>(string path, System.Type systemTypeInstance) where T : Object => null!;
        public static ResourceRequest LoadAsync(string path) => new();
        public static ResourceRequest LoadAsync(string path, System.Type type) => new();
        public static ResourceRequest LoadAsync<T>(string path) where T : Object => new();
        public static void UnloadUnusedAssets() { }
        public static T[] FindObjectsOfType<T>() where T : Object => Array.Empty<T>();
    }

    public class DisplayInfo
    {
        public int systemWidth = 1920;
        public int systemHeight = 1080;
        public int renderingWidth = 1920;
        public int renderingHeight = 1080;
    }

    public static class Display
    {
        public static int width = 1920;
        public static int height = 1080;
        public static DisplayInfo main = new();
    }

    public enum CursorLockMode { None = 0, Locked = 1, Confined = 2 }
    public enum CursorMode { Auto = 0, ForceSoftware = 1 }

    public static class Cursor
    {
        public static bool visible = true;
        public static CursorLockMode lockState = CursorLockMode.None;
        public static void SetCursor(Texture2D? texture, Vector2 hotspot, CursorMode cursorMode) { }
        public static void SetCursor(Texture2D? texture, Vector2 hotspot, CursorMode cursorMode, int frameSpeed = 0) { }
    }

    public enum ShadowQuality { Disabled = 0, Low = 1, Medium = 2, High = 3 }

    public static class QualitySettings
    {
        public static int vSyncCount = 0;
        public static int antiAliasing = 0;
        public static int pixelLightCount = 2;
        public static bool shadows = true;
        public static ShadowQuality shadowQuality = ShadowQuality.High;
        public static int shadowResolution = 2048;
        public static int masterTextureLimit = 0;
        public static int anisotropicFiltering = 1;
        public static bool softVegetation = true;
    }

    public static class RenderSettings
    {
        public static Color ambientLight = Color.white;
        public static float ambientIntensity = 1f;
        public static Material? skybox = null;
        public static Light? sun = null;
        public static bool fog = false;
        public static Color fogColor = Color.gray;
        public static float fogDensity = 0.01f;
        public static float fogStartDistance = 0f;
        public static float fogEndDistance = 300f;
    }

    /// <summary>
    /// Scene lighting approximation for the UE port.
    ///
    /// GameScene publishes the original ambient and directional colors here.
    /// UnityMaterial applies their calibrated linear gain to the unlit UE materials.
    /// The baked shadow textures and local lights are applied separately.
    /// </summary>
    public static class SceneLighting
    {
        public static Color MainLightColor = Color.white;
        public static float MainLightIntensity = 1f;
        public static Color AmbientColor = Color.white;
        public static float AmbientIntensity = 1f;

        /// <summary>Composite multiplier applied to every PAL3 texture.</summary>
        public static Color SurfaceScale = Color.white;
        public static Vector3? LocalLightPosition;

        public static void Set(Color mainLightColor, float mainLightIntensity,
            Color ambientColor, float ambientIntensity, bool isNight, float sceneBrightness)
        {
            MainLightColor = mainLightColor;
            MainLightIntensity = mainLightIntensity;
            AmbientColor = ambientColor;
            AmbientIntensity = ambientIntensity;

            // Lift daytime ambient fill more than night, so baked daylight stays
            // readable while a night scene still has room for the local light.
            float ambient = ambientIntensity * (isNight ? 0.065f : 0.16f);
            float direct = mainLightIntensity * (isNight ? 0.035f : 0.075f);
            Color balance = isNight ? new Color(1.20f, 1.10f, 0.62f) : new Color(1.05f, 1.00f, 0.90f);
            SurfaceScale = new Color(
                Clamp01((ambientColor.r * ambient + mainLightColor.r * direct) * balance.r) * sceneBrightness,
                Clamp01((ambientColor.g * ambient + mainLightColor.g * direct) * balance.g) * sceneBrightness,
                Clamp01((ambientColor.b * ambient + mainLightColor.b * direct) * balance.b) * sceneBrightness,
                1f);
        }

        public static void Reset(float sceneBrightness = 1f)
        {
            SurfaceScale = new Color(sceneBrightness, sceneBrightness, sceneBrightness, 1f);
            LocalLightPosition = null;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public enum AudioRolloffMode { Logarithmic = 0, Linear = 1, Custom = 2 }

    public struct LayerMask
    {
        public int value;
        public static implicit operator int(LayerMask m) => m.value;
        public static implicit operator LayerMask(int v) => new() { value = v };
        public static int NameToLayer(string layerName) => 0;
        public static string LayerToName(int layer) => "";
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 position { get => new(x, y); set { x = value.x; y = value.y; } }
        public Vector2 size { get => new(width, height); set { width = value.x; height = value.y; } }
        public Vector2 center { get => new(x + width * 0.5f, y + height * 0.5f); set { x = value.x - width * 0.5f; y = value.y - height * 0.5f; } }
        public Vector2 min => new(xMin, yMin);
        public Vector2 max => new(xMax, yMax);
        public bool Contains(Vector2 p) => p.x >= xMin && p.x <= xMax && p.y >= yMin && p.y <= yMax;
    }

    public class CustomYieldInstruction
    {
        public virtual bool keepWaiting => false;
    }

    public class WaitForSeconds : CustomYieldInstruction
    {
        public float seconds;
        public WaitForSeconds(float s)
        {
            seconds = s;
            _target = Time.time + s;
        }
        public override bool keepWaiting => Time.time < _target;
        private readonly float _target;
    }

    namespace Networking
    {
        public class UnityWebRequest : IDisposable
        {
            public enum Result { InProgress = 0, Success = 1, ConnectionError = 2, ProtocolError = 3, DataProcessingError = 4 }

            public string url = "";
            public Result result = Result.InProgress;
            public string error => result == Result.Success ? "" : "UnityWebRequest failed";
            public DownloadHandler downloadHandler = new();
            public bool isDone => true;
            public bool isHttpError => false;
            public bool isNetworkError => false;

            public void SetRequestHeader(string name, string value) { }
            public UnityWebRequest SendWebRequest() { result = Result.Success; return this; }
            public void Dispose() { }

            public static UnityWebRequest Get(string uri) => new() { url = uri };
            public static UnityWebRequest Post(string uri, string postData) => new() { url = uri };
            public static string EscapeURL(string s) => System.Uri.EscapeDataString(s);
            public static string UnEscapeURL(string s) => System.Uri.UnescapeDataString(s);
        }

        public class DownloadHandler
        {
            public string text => "";
            public byte[] data => Array.Empty<byte>();
        }
    }
}
