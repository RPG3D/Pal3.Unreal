// Stubs for Unity/Engine types not yet ported to UnrealSharp compat layer.
// These let Pal3.Game compile; UI/Input/Video/Renderer functionality is deferred (Batch 4).
// Replace with real UE (UMG / EnhancedInput / Engine.Renderer UE impl) over time.
// NOTE: stub members are minimal common surface; expand as CS1061 errors reveal real usage.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using Engine.Core.Abstraction;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)]
    public class Tooltip : Attribute { public Tooltip(string t) { } }
    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

    public static class TouchScreenKeyboard
    {
        public static bool hideInput;
    }

    public enum AudioType { UNKNOWN = 0, ACC = 1, AIFF = 2, AUDIOQUEUE = 3, AUDIO_WAV = 4, AUDIO_OGGVORBIS = 5, MPEG = 6, WAV = 7, OGGVORBIS = 8 }

    public class AudioSource : Component
    {
        public AudioClip? clip;
        public bool isPlaying;
        public bool loop;
        public bool mute;
        public float volume = 1f;
        public float spatialBlend;
        public float maxDistance = 500f;
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void UnPause() { }
        public void PlayOneShot(AudioClip clip, float volumeScale = 1f) { }
    }

    public class Rigidbody : Component
    {
        public bool useGravity = true;
        public bool isKinematic;
        public float mass = 1f;
        public Vector3 velocity;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public RigidbodyConstraints constraints = RigidbodyConstraints.None;
        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force) { }
        public void AddTorque(Vector3 torque) { }
    }

    [Flags]
    public enum RigidbodyConstraints
    {
        None = 0,
        FreezePositionX = 2,
        FreezePositionY = 4,
        FreezePositionZ = 8,
        FreezeRotationX = 16,
        FreezeRotationY = 32,
        FreezeRotationZ = 64,
        FreezePosition = FreezePositionX | FreezePositionY | FreezePositionZ,
        FreezeRotation = FreezeRotationX | FreezeRotationY | FreezeRotationZ,
        FreezeAll = FreezePosition | FreezeRotation,
    }

    public enum ForceMode { Force = 0, Acceleration = 5, Impulse = 1, VelocityChange = 2 }

    public class ParticleSystem : Component
    {
        public bool isPlaying => false;
        public bool isPaused => false;
        public bool isStopped => true;
        public bool enableEmission { get; set; } = true;
        public float startLifetime = 1f;
        public float startSpeed = 1f;
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void Clear() { }
    }

    public class FpsCounter
    {
        public float GetFps() => 60f;
        public int Fps => 60;
    }

    public class Coroutine
    {
        /// <summary>Opaque scheduler handle (see CoroutineScheduler).</summary>
        internal object Handle;
    }

    public struct ColorBlock
    {
        public Color normalColor;
        public Color highlightedColor;
        public Color pressedColor;
        public Color selectedColor;
        public Color disabledColor;
        public float colorMultiplier;
        public float fadeDuration = 0.1f;
        public ColorBlock(Color normal, Color highlighted, Color pressed, Color selected, Color disabled, float multiplier, float fade)
        {
            normalColor = normal; highlightedColor = highlighted; pressedColor = pressed;
            selectedColor = selected; disabledColor = disabled; colorMultiplier = multiplier; fadeDuration = fade;
        }
    }

    public class Renderer : Component
    {
        public bool enabled = true;
        public Material? material;
        public Material[] materials => Array.Empty<Material>();
        public Bounds bounds;
        public int sortingOrder;
        public int sortingLayerID;
        public bool isVisible => true;
    }

    public class Canvas : Component
    {
        public bool enabled = true;
        public int sortingOrder;
    }

    public class CanvasGroup : Component
    {
        public float alpha = 1f;
        public bool interactable = true;
        public bool blocksRaycasts = true;
        public bool ignoreParentGroups;
    }

    public class RenderTexture
    {
        public int width;
        public int height;
        public RenderTexture() { }
        public RenderTexture(int width, int height) { this.width = width; this.height = height; }
        public void Release() { }
        public void Create() { }
    }

}

namespace UnityEngine.InputSystem
{
    public class InputControl
    {
        public string name = "";
        public string displayName = "";
        public bool synthetic;
        public bool noisy;
        public InputDevice? device;
    }

    public class InputDevice : InputControl
    {
        public static InputDevice? current => null;
        public string layout => "";
        public IEnumerable<InputControl> allControls => Array.Empty<InputControl>();
        public bool IsPressed() => false;
        public void RequestReset() { }
    }

    public class Keyboard : InputDevice
    {
        public static new Keyboard? current => null;
        public KeyControl? ctrlKey;
        public KeyControl? leftCtrlKey;
        public KeyControl? rightCtrlKey;
        public KeyControl? shiftKey;
        public KeyControl? leftShiftKey;
        public KeyControl? rightShiftKey;
        public KeyControl? altKey;
        public KeyControl? escapeKey;
        public KeyControl? spaceKey;
        public KeyControl? enterKey;
        public KeyControl? backspaceKey;
        public KeyControl? tabKey;
        public KeyControl? anyKey;
        public KeyControl? f1Key;
        public KeyControl? f2Key;
        public KeyControl? f3Key;
        public KeyControl? f4Key;
        public KeyControl? f5Key;
        public KeyControl? f6Key;
        public KeyControl? f7Key;
        public KeyControl? f8Key;
        public KeyControl? f9Key;
        public KeyControl? f10Key;
        public KeyControl? f11Key;
        public KeyControl? f12Key;
        public KeyControl? aKey;
        public KeyControl? bKey;
        public KeyControl? cKey;
        public KeyControl? dKey;
        public KeyControl? eKey;
        public KeyControl? wKey;
        public KeyControl? sKey;
        public KeyControl? upArrowKey;
        public KeyControl? downArrowKey;
        public KeyControl? leftArrowKey;
        public KeyControl? rightArrowKey;
        public KeyControl this[Key key] => null!;
    }

    public enum Key
    {
        None = 0, Space = 1, Enter = 2, Tab = 3, Backquote = 4, Quote = 5,
        Semicolon = 6, Comma = 7, Period = 8, Slash = 9, Backslash = 10,
        LeftBracket = 11, RightBracket = 12, Minus = 13, Equals = 14,
        A = 15, B = 16, C = 17, D = 18, E = 19, F = 20, G = 21, H = 22,
        I = 23, J = 24, K = 25, L = 26, M = 27, N = 28, O = 29, P = 30,
        Q = 31, R = 32, S = 33, T = 34, U = 35, V = 36, W = 37, X = 38,
        Y = 39, Z = 40, Digit1 = 41, Digit2 = 42, Digit3 = 43, Digit4 = 44,
        Digit5 = 45, Digit6 = 46, Digit7 = 47, Digit8 = 48, Digit9 = 49, Digit0 = 50,
        LeftShift = 51, RightShift = 52, LeftAlt = 53, RightAlt = 54,
        LeftCtrl = 55, RightCtrl = 56, LeftMeta = 57, RightMeta = 58,
        LeftArrow = 59, RightArrow = 60, UpArrow = 61, DownArrow = 62,
        F1 = 63, F2 = 64, F3 = 65, F4 = 66, F5 = 67, F6 = 68,
        F7 = 69, F8 = 70, F9 = 71, F10 = 72, F11 = 73, F12 = 74,
    }

    public class Gamepad : InputDevice
    {
        public static new Gamepad? current => null;
        public Vector2 leftStick => default;
        public Vector2 rightStick => default;
        public ButtonControl? buttonSouth;
        public ButtonControl? buttonNorth;
        public ButtonControl? buttonWest;
        public ButtonControl? buttonEast;
        public ButtonControl? leftShoulder;
        public ButtonControl? rightShoulder;
    }

    public class Mouse : InputDevice
    {
        public static new Mouse? current => null;
        public Vector2 position => default;
        public Vector2 delta => default;
        public Vector2 scroll => default;
        public ButtonControl? leftButton;
        public ButtonControl? rightButton;
        public ButtonControl? middleButton;
    }

    public class Pointer : InputDevice
    {
        public static new Pointer? current => null;
        public Vector2 position => default;
        public Vector2 delta => default;
    }

    public class Touchscreen : InputDevice
    {
        public static new Touchscreen? current => null;
        public TouchControl? primaryTouch;
    }

    public class Joystick : InputDevice
    {
        public static new Joystick? current => null;
    }

    public class InputSettings
    {
        public float defaultButtonPressPoint = 0.5f;
        public float buttonPressPoint = 0.5f;
        public float defaultDeadzoneMin = 0.125f;
    }

    public static class InputSystem
    {
        public static event Action<InputEventPtr, InputDevice?>? onEvent;
        public static event Action<object?, InputActionChange>? onActionChange;
        public static IEnumerable<InputDevice> devices => Array.Empty<InputDevice>();
        public static InputSettings settings { get; } = new();
        public static void DisableDevice(InputDevice device) { }
        public static void EnableDevice(InputDevice device) { }
    }

    public class InputAction
    {
        public bool enabled { get; set; }
        public bool inProgress { get; set; }
        public InputControl? activeControl => null;
        public string name => "";
        public string id => "";
        public string path => "";
        public InputActionType type => InputActionType.Value;

        public event Action<CallbackContext>? performed;
        public event Action<CallbackContext>? started;
        public event Action<CallbackContext>? canceled;

        public void Enable() { enabled = true; }
        public void Disable() { enabled = false; }
        public void AddBinding(string path) { }
        public void AddCompositeBinding(string composite) { }
        public void ApplyBindingOverride(string path) { }

        public T ReadValue<T>() where T : struct => default;
        public bool IsPressed() => false;
        public bool WasPressedThisFrame() => false;
        public bool WasReleasedThisFrame() => false;

        public struct CallbackContext
        {
            public object? value;
            public bool performed;
            public bool started;
            public bool canceled;
            public InputActionPhase phase;
            public int controlIndex;
            public int actionIndex;

            public T ReadValue<T>() where T : struct => default;
            public bool ReadValueAsButton() => false;
            public float ReadValueAsFloat() => 0f;
            public bool IsPressed() => false;
            public bool WasPressedThisFrame() => false;
            public bool WasReleasedThisFrame() => false;
            public InputControl? control => null;
        }
    }

    public enum InputActionType { Value = 0, Button = 1, PassThrough = 2 }

    public enum InputActionPhase { Disabled = 0, Waiting = 1, Started = 2, Performed = 3, Canceled = 4 }

    public class InputActionMap
    {
        public bool enabled { get; set; }
        public string name => "";
        public void Enable() { enabled = true; }
        public void Disable() { enabled = false; }
        public InputAction FindAction(string name) => new InputAction();
        public InputAction FindAction(string name, bool throwIfNotFound) => new InputAction();
    }

    public class InputActionAsset : IInputActionCollection2
    {
        public InputActionMap[] actionMaps => Array.Empty<InputActionMap>();
        public InputBinding? bindingMask { get; set; }
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices { get; set; }
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputControlScheme> controlSchemes => default;
        public IEnumerable<InputBinding> bindings => Array.Empty<InputBinding>();

        public static InputActionAsset FromJson(string json) => new InputActionAsset();

        public InputActionMap FindActionMap(string name) => new InputActionMap();
        public InputActionMap FindActionMap(string name, bool throwIfNotFound) => new InputActionMap();
        public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false) => new InputAction();
        public int FindBinding(InputBinding bindingMask, out InputAction action) { action = new InputAction(); return 0; }
        public bool Contains(InputAction action) => false;
        public void Enable() { }
        public void Disable() { }
        public IEnumerator<InputAction> GetEnumerator() => ((IEnumerable<InputAction>)Array.Empty<InputAction>()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct InputBinding
    {
        public string path;
        public string name;
        public string action;
        public string groups;
        public bool isComposite;
        public bool isPartOfComposite;
        public string id;
        public string interactions;
        public string processors;
        public override string ToString() => path;
    }

    public class InputControlScheme
    {
        public string name = "";
        public string id = "";
    }

    public enum InputActionChange
    {
        ActionEnabled = 0,
        ActionDisabled = 1,
        ActionMapEnabled = 2,
        ActionMapDisabled = 3,
        ActionStarted = 4,
        ActionPerformed = 5,
        ActionCanceled = 6,
        BoundControlsAdded = 7,
        BoundControlsRemoved = 8,
    }

    public interface IInputActionCollection2 : IEnumerable<InputAction> { }
}

namespace UnityEngine.InputSystem.Controls
{
    public class KeyControl : InputControl
    {
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
        public Key key => Key.None;
    }

    public class ButtonControl : InputControl
    {
        public float pressPoint => 0.5f;
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
        public float ReadValue() => 0f;
        public bool ReadValueFromEvent(InputEventPtr eventPtr, out float value) { value = 0f; return false; }
    }

    public class TouchControl : InputControl
    {
        public bool isInProgress => false;
        public Vector2 position => default;
        public Vector2 delta => default;
    }

    public class Vector2Control : InputControl
    {
        public Vector2 ReadValue() => default;
    }

    public class AxisControl : InputControl
    {
        public float ReadValue() => 0f;
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    public struct InputEventPtr
    {
        public bool IsA<T>() where T : struct => false;
        public override string ToString() => "InputEventPtr";
    }

    public struct StateEvent { }
    public struct DeltaStateEvent { }
}

namespace UnityEngine.InputSystem.DualShock
{
    public class DualShockGamepad : Gamepad
    {
        public static new DualShockGamepad? current => null;
    }
}

namespace UnityEngine.InputSystem.OnScreen
{
    public class OnScreenButton : InputControl
    {
        public bool enabled = true;
        public void OnPointerDown() { }
        public void OnPointerUp() { }
    }

    public class OnScreenStick : Component
    {
        public float movementRange = 50f;
        public bool useIsometricView;
        public Vector2 value => default;
    }
}

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<T> : IEnumerable<T>
    {
        public int Length => 0;
        public T this[int index] => default!;
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public static implicit operator T[](ReadOnlyArray<T> array) => Array.Empty<T>();
    }
}

namespace UnityEngine.Collections
{
    public struct ReadOnlyArray<T> { public int Length => 0; }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem
    {
        public static EventSystem? current => null;
        public GameObject? firstSelectedGameObject;
    }
}

namespace UnityEngine.UI
{
    public class Image : Component
    {
        public bool enabled = true;
        public float fillAmount = 1f;
        public Color color = Color.white;
        public Sprite? sprite;
        public bool preserveAspect;
        public bool raycastTarget = true;
        public Material? material;
        public RectTransform rectTransform => null!;
    }

    public class Button : Component
    {
        public bool interactable = true;
        public ButtonClickedEvent onClick { get; } = new();
        public Navigation navigation;
        public ColorBlock colors;
        public Selectable.Transition transition = Selectable.Transition.ColorTint;
        public void Select() { }
        public void OnClick() => onClick?.Invoke();
        public void OnSubmit() => OnClick();
    }

    public class Selectable
    {
        public enum Transition { None = 0, ColorTint = 1, SpriteSwap = 2, Animation = 3 }
    }

    public class ButtonClickedEvent
    {
        private UnityAction? _onClick;
        public void AddListener(UnityAction action) => _onClick += action;
        public void RemoveListener(UnityAction action) => _onClick -= action;
        public void RemoveAllListeners() => _onClick = null;
        public void Invoke() => _onClick?.Invoke();
    }

    public struct Navigation
    {
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
        public Mode mode;
        public bool wrapAround;
        public Button? selectOnUp;
        public Button? selectOnDown;
        public Button? selectOnLeft;
        public Button? selectOnRight;
    }

    public class RectTransform : Transform
    {
        public enum Axis { Horizontal = 0, Vertical = 1 }
        public Rect rect;
        public Vector2 anchoredPosition;
        public Vector2 anchorMin = Vector2.zero;
        public Vector2 anchorMax = Vector2.zero;
        public Vector2 sizeDelta;
        public Vector2 pivot = new(0.5f, 0.5f);
        public Quaternion localRotation;
        public void SetSizeWithCurrentAnchors(Axis axis, float size) { }
        public void SetSizeDelta(Vector2 size) => sizeDelta = size;
        public void ForceUpdateRectTransforms() { }
        public Vector2 GetSize() => rect.size;
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera? cam, out Vector2 localPoint)
        {
            localPoint = default;
            return false;
        }
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint) => default;
    }

    public class GridLayoutGroup : Component
    {
        public enum Constraint { Flexible = 0, FixedColumnCount = 1, FixedRowCount = 2 }
        public Constraint constraint = Constraint.Flexible;
        public int constraintCount = 1;
        public Vector2 cellSize = new(100f, 100f);
        public Vector2 spacing;
        public TextAnchor childAlignment;
        public bool childForceExpandWidth = true;
        public bool childForceExpandHeight = true;
    }

    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8 }

    public class ScrollRect : Component
    {
        public bool horizontal = true;
        public bool vertical = true;
        public float horizontalNormalizedPosition = 1f;
        public float verticalNormalizedPosition = 1f;
        public bool movementType;
        public void StopMovement() { }
    }

    public class Text : Component
    {
        public string text = "";
        public int fontSize = 14;
        public Color color = Color.white;
        public bool raycastTarget = true;
        public TextAnchor alignment;
        public bool supportRichText = true;
        public void SetText(string value) => text = value;
    }
}

namespace TMPro
{
    public class TextMeshProUGUI
    {
        public string text = "";
        public float fontSize = 14f;
        public bool enabled = true;
        public Color color = Color.white;
        public bool raycastTarget = true;
        public bool autoSizeTextContainer;
        public void SetText(string value) => text = value;
        public void SetText(string format, float arg) => text = string.Format(format, arg);
        public void SetText(string format, int arg) => text = string.Format(format, arg);
        public void SetText(string format, float arg0, float arg1, float arg2, float arg3, float arg4, float arg5)
            => text = string.Format(format, arg0, arg1, arg2, arg3, arg4, arg5);
        public void ForceMeshUpdate() { }
    }
}

namespace UnityEngine.Rendering
{
    public class FloatParameter { public float value; }
    public class IntParameter { public int value; }
    public class ColorParameter { public UnityEngine.Color value; }

    public enum BlendMode { Zero = 0, One = 1, DstColor = 2, SrcColor = 3, OneMinusDstColor = 4, SrcAlpha = 5, OneMinusSrcColor = 6, DstAlpha = 7, OneMinusDstAlpha = 8, SrcAlphaSaturate = 9, OneMinusSrcAlpha = 10 }

    public enum CompareFunction { Disabled = 0, Never = 1, Less = 2, Equal = 3, LessEqual = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8 }
}

namespace UnityEngine.Video
{
    public class VideoPlayer
    {
        public bool isPlaying;
        public bool isPrepared;
        public bool isLooping;
        public bool waitForFirstFrame;
        public string url = "";
        public long frame;
        public RenderTexture? targetTexture;
        public uint width;
        public uint height;

        public event Action<VideoPlayer>? loopPointReached;
        public event Action<VideoPlayer>? started;
        public event Action<VideoPlayer>? prepared;
        public event Action<VideoPlayer>? errorReceived;

        public void Play() { isPlaying = true; }
        public void Stop() { isPlaying = false; }
        public void Pause() { }
        public void Prepare() { isPrepared = true; }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T>(T arg);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);

    public class UnityEvent
    {
        private UnityAction? _action;
        public void AddListener(UnityAction action) => _action += action;
        public void RemoveListener(UnityAction action) => _action -= action;
        public void RemoveAllListeners() => _action = null;
        public void Invoke() => _action?.Invoke();
    }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static void BeginSample(string s) { }
        public static void EndSample() { }
        public static long GetTotalAllocatedMemoryLong() => 0;
        public static long GetTotalReservedMemoryLong() => 0;
    }
}

namespace UnityEngine.Rendering.PostProcessing
{
    public class BoolParameter { public bool value; public bool overrideState; public static implicit operator bool(BoolParameter p) => p.value; }
    public class FloatParameter { public float value; public float overrideState; public static implicit operator float(FloatParameter p) => p.value; }
    public class IntParameter { public int value; }
    public class ColorParameter { public UnityEngine.Color value; }

    public class PostProcessVolume : Component
    {
        public PostProcessProfile? profile = new();
        public bool isGlobal = true;
        public float blendDistance = 0f;
        public float weight = 1f;
        public int priority = 0;
        public LayerMask trigger;
    }

    public class PostProcessLayer : Component
    {
        public bool enabled = true;
        public int volumeLayer;
        public bool stopNaNPropagation = true;
    }

    public class PostProcessEffectSettings
    {
        public bool active = true;
        public bool enabled = true;
        public float blendTo = 0f;
    }

    public class PostProcessEffectRenderer<T> where T : PostProcessEffectSettings
    {
        protected T settings = default!;
        public virtual void Render(PostProcessRenderContext context) { }
        public virtual void Init() { }
        public virtual void Release() { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PostProcessAttribute : Attribute
    {
        public PostProcessAttribute(Type renderer, PostProcessEvent e, string menuItem) { }
        public PostProcessAttribute(Type renderer, PostProcessEvent e, string menuItem, bool allowInSceneView = true) { }
    }

    public class PostProcessRenderContext
    {
        public CommandBuffer command = new();
        public PropertySheetFactory propertySheets = new();
        public RenderTargetIdentifier source;
        public RenderTargetIdentifier destination;
        public PostProcessVolume volume = new();
        public Camera? camera;
    }

    public class PostProcessProfile
    {
        public T GetSetting<T>() where T : PostProcessEffectSettings, new() => new();
        public void AddSettings<T>() where T : PostProcessEffectSettings, new() { }
    }

    public class CommandBuffer
    {
        public void BlitFullscreenTriangle(RenderTargetIdentifier source, RenderTargetIdentifier destination, PropertySheet propertySheet, int pass) { }
        public void Release() { }
    }

    public struct RenderTargetIdentifier
    {
        public int id;
        public RenderTargetIdentifier(int id) { this.id = id; }
        public static implicit operator RenderTargetIdentifier(BuiltinRenderTextureType type) => new RenderTargetIdentifier((int)type);
        public static implicit operator RenderTargetIdentifier(Texture tex) => new RenderTargetIdentifier(0);
    }

    public enum BuiltinRenderTextureType { None = 0, RenderTexture = 1, BoundRenderTexture = 2, CameraTarget = 3, CameraDepth = 4, CurrentActive = 5 }

    public class PropertySheet
    {
        public MaterialPropertyBlock properties = new();
    }

    public class PropertySheetFactory
    {
        public PropertySheet Get(Shader shader) => new();
        public PropertySheet Get(Shader shader, Material material) => new();
    }

    public class MaterialPropertyBlock
    {
        public void SetFloat(int nameID, float value) { }
        public void SetFloat(string name, float value) { }
        public void SetColor(string name, Color value) { }
        public void SetTexture(string name, Texture value) { }
        public void SetVector(string name, Vector4 value) { }
    }

    public enum PostProcessEvent { BeforeTransparent, BeforeStack, AfterStack, AfterStackForTransparent }

    public class AmbientOcclusion : PostProcessEffectSettings { }
    public class Vignette : PostProcessEffectSettings { public FloatParameter intensity; public FloatParameter smoothness; }
    public class Bloom : PostProcessEffectSettings { public FloatParameter intensity; public FloatParameter threshold; public FloatParameter diffusion; }
    public class ColorGrading : PostProcessEffectSettings { }
    public class DepthOfField : PostProcessEffectSettings { public FloatParameter focusDistance; public FloatParameter aperture; }
}

namespace Engine.Renderer
{
    /// <summary>
    /// Static-mesh renderer stub. The full Render(...) surface mirrors the call shape in
    /// CvdModelRenderer so the ported mesh pipeline compiles; real UE rendering is deferred.
    /// </summary>
    public class StaticMeshRenderer
    {
        public bool receiveShadows;
        public bool ReceiveShadows { get => receiveShadows; set => receiveShadows = value; }
        public bool castShadows = true;
        public bool IsVisible { get; set; } = true;
        public UnityEngine.Bounds GetRendererBounds() => default;
        public UnityEngine.Bounds GetMeshBounds() => default;
        public Engine.Core.Abstraction.IMaterial[] GetMaterials() => Array.Empty<Engine.Core.Abstraction.IMaterial>();
        public UnityEngine.Mesh Render(
            UnityEngine.Vector3[] vertices,
            int[] triangles,
            UnityEngine.Vector3[] normals,
            (int channel, UnityEngine.Vector2[] uvs) mainTextureUvs,
            (int channel, UnityEngine.Vector2[] uvs) secondaryTextureUvs,
            Engine.Core.Abstraction.IMaterial[] materials,
            bool isDynamic) => new UnityEngine.Mesh();
        public void Dispose() { }
        public void Destroy() { }
    }

    public class AnimatedBillboardRenderer
    {
        public void Init(Engine.Core.Abstraction.ISprite[] sprites, float frameRate) { }
        public void Init(Engine.Core.Abstraction.ISprite[] sprites, float frameRate, Engine.Core.Abstraction.IMaterial material) { }
        public System.Collections.IEnumerator PlayAnimationAsync(int loopCount) { yield break; }
        public void StartAnimation(int loopCount) { }
        public void StopAnimation() { }
        public void Destroy() { }
    }

    public class SkyBoxRenderer
    {
        public void Render(Engine.Core.Abstraction.IGameEntity cameraEntity, Engine.Core.Abstraction.IMaterial skyboxMaterial) { }
        public void Render(Engine.Core.Abstraction.IGameEntity cameraEntity) { }
        public void Destroy() { }
    }

    public class StaticBillboardRenderer { }
}

namespace Engine.Services
{
    public interface ITextureCache
    {
        bool Add(string name, ITexture2D texture);
        bool Add(string name, ITexture2D texture, bool hasAlphaChannel);
        bool TryGet(string name, out ITexture2D texture);
        bool TryGet(string name, out (ITexture2D texture, bool hasAlphaChannel) result);
        bool TryGetValue(string name, out ITexture2D texture);
        bool TryGetValue(string name, out (ITexture2D texture, bool hasAlphaChannel) result);
        void DisposeAll();
    }
}

namespace Engine.Core.Implementation
{
    // Base class for managed game-entity scripts (replaces MonoBehaviour lifecycle).
    // Real UE impl: drive via AActor tick. Members expanded as usage demands.
    public class GameEntityScript
    {
        public Engine.Core.Abstraction.IGameEntity GameEntity { get; set; }
        public bool enabled = true;

        /// <summary>
        /// The transform of the wrapped game entity. Mirrors UnityEngine.Component.transform
        /// (the port uses the PascalCase member on the script, resolved to ITransform).
        /// </summary>
        public Engine.Core.Abstraction.ITransform Transform => GameEntity.Transform;

        /// <summary>
        /// Lowercase UnityEngine-style transform. Returns a stub UnityEngine.Transform so
        /// `foreach (Transform child in transform)` compiles; child enumeration is deferred.
        /// Use <see cref="Transform"/> for real ITransform access.
        /// </summary>
        public UnityEngine.Transform transform => null!;

        /// <summary>
        /// The wrapped game entity itself (UnityEngine.GameObject analog).
        /// </summary>
        public Engine.Core.Abstraction.IGameEntity gameObject => GameEntity;

        /// <summary>
        /// Coroutine runner (Unity-style IEnumerator coroutines). Fed into the
        /// frame-driven <see cref="Engine.Core.Implementation.CoroutineScheduler"/>,
        /// pumped by the UE driver actor (APal3Driver.Tick).
        /// </summary>
        public UnityEngine.Coroutine StartCoroutine(System.Collections.IEnumerator routine)
            => Engine.Core.Implementation.CoroutineScheduler.Run(routine, this);

        public UnityEngine.Coroutine StartCoroutine(System.Threading.Tasks.Task task)
            => Engine.Core.Implementation.CoroutineScheduler.Run(WaitForTask(task), this);

        private static System.Collections.IEnumerator WaitForTask(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted) yield return null;
        }

        public void StopCoroutine(System.Collections.IEnumerator routine)
            => Engine.Core.Implementation.CoroutineScheduler.Stop(routine);

        public void StopCoroutine(UnityEngine.Coroutine routine)
            => Engine.Core.Implementation.CoroutineScheduler.Stop(routine);

        public void StopAllCoroutines()
            => Engine.Core.Implementation.CoroutineScheduler.StopAll(this);

        /// <summary>
        /// MonoBehaviour-style component lookup on the wrapped game entity.
        /// </summary>
        public T[] GetComponentsInChildren<T>() where T : class => Array.Empty<T>();

        /// <summary>
        /// Destroys the wrapped game entity. Mirrors UnityEngine.Object.Destroy.
        /// </summary>
        public void Destroy() => GameEntity?.Destroy();

        protected virtual void Init() { }
        protected virtual void OnEnableGameEntity() { }
        protected virtual void OnDisableGameEntity() { }
        protected virtual void OnUpdateGameEntity(float deltaTime) { }
        protected virtual void OnLateUpdateGameEntity(float deltaTime) { }
        protected virtual void OnFixedUpdateGameEntity(float deltaTime) { }
        protected virtual void OnCollisionEnterGameEntity(Engine.Core.Abstraction.IGameEntity other) { }
        protected virtual void OnCollisionExitGameEntity(Engine.Core.Abstraction.IGameEntity other) { }
        protected virtual void OnTriggerEnterGameEntity(Engine.Core.Abstraction.IGameEntity other) { }
        protected virtual void OnTriggerExitGameEntity(Engine.Core.Abstraction.IGameEntity other) { }

        // Internal bridges letting GameEntityScriptRuntime (same assembly) invoke the
        // protected virtuals above only when a derived type actually overrides them.
        private bool? _overridesOnUpdate, _overridesOnLateUpdate, _overridesOnDisable;

        private bool IsOverridden(string methodName)
        {
            System.Reflection.MethodInfo? method = GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return method != null && method.GetBaseDefinition().DeclaringType != typeof(GameEntityScript);
        }

        internal void InvokeVirtualOnUpdate(float deltaTime)
        {
            _overridesOnUpdate ??= IsOverridden(nameof(OnUpdateGameEntity));
            if (_overridesOnUpdate == true) OnUpdateGameEntity(deltaTime);
        }

        internal void InvokeVirtualOnLateUpdate(float deltaTime)
        {
            _overridesOnLateUpdate ??= IsOverridden(nameof(OnLateUpdateGameEntity));
            if (_overridesOnLateUpdate == true) OnLateUpdateGameEntity(deltaTime);
        }

        internal void InvokeVirtualOnDisable()
        {
            _overridesOnDisable ??= IsOverridden(nameof(OnDisableGameEntity));
            if (_overridesOnDisable == true) OnDisableGameEntity();
        }
    }

    public class TickableGameEntityScript : GameEntityScript
    {
        // Tickable variant - real UE impl drives tick via AActor.
    }
}

namespace Engine.UI
{
    public class RoundedFrostedGlassImage
    {
        public float transparency = 0.5f;
        public float blurAmount = 10f;
        public void SetMaterialTransparency(float value) => transparency = value;
        public void SetMaterialBlurAmount(float value) => blurAmount = value;
    }
}

namespace IngameDebugConsole
{
    public static class DebugLogConsole
    {
        public static void AddCommand(string command, string description, Action method) { }
        public static void AddCommand<T>(string command, string description, Action<T> method) { }
        public static void AddCommandStatic(string command, string description, Type type, string methodName) { }
        public static void RemoveCommand(string command) { }
        public static void ExecuteCommand(string command) { }
        public static void FetchArgumentsFromCommand(string command, List<string> arguments) { }
    }

    public class DebugLogManager
    {
        public static DebugLogManager Instance => null!;
        public event Action? OnLogWindowShown;
        public event Action? OnLogWindowHidden;
        public UnityEngine.GameObject gameObject => null!;
    }
}

namespace SimpleFileBrowser
{
    public static class FileBrowser
    {
        public enum PickMode { Files = 0, Folders = 1, FilesAndFolders = 2 }

        public static bool Success;
        public static string[] Result = Array.Empty<string>();
        public static bool IsOpen;
        public static bool IsInitialized;

        /// <summary>
        /// Coroutine that waits for the file browser dialog to complete; the caller
        /// then reads <see cref="Success"/> / <see cref="Result"/>. Dialog is deferred.
        /// </summary>
        public static IEnumerator WaitForLoadDialog(PickMode pickMode,
            bool allowMultiSelection = false,
            string? initialPath = null,
            string? initialFilename = null,
            string? title = null,
            string? loadButtonText = null)
        {
            yield break;
        }

        public static bool ShowLoadDialog(Action<string[]> onSuccess, Action onCancel, int mode = 0) => false;
    }

    public static class FileBrowserHelpers
    {
        public static void CopyDirectory(string sourceDir, string destinationDir) { }
        public static bool ShouldUseSAF => false;
    }
}

namespace Engine.DataLoader
{
    /// <summary>
    /// Texture loaders, in the Engine.DataLoader namespace the ported code expects
    /// (TextureLoaderFactory uses `using Engine.DataLoader;`). Loading is deferred;
    /// Load just reports an alpha channel, ToTexture returns a white texture.
    /// </summary>
    public sealed class TgaTextureLoader : ITextureLoader
    {
        private readonly ITextureFactory _textureFactory;
        public TgaTextureLoader(ITextureFactory textureFactory) { _textureFactory = textureFactory; }
        public void Load(byte[] data, out bool hasAlphaChannel) { hasAlphaChannel = false; }
        public ITexture2D ToTexture() => _textureFactory.CreateWhiteTexture();
    }

    public sealed class DxtTextureLoader : ITextureLoader
    {
        private readonly ITextureFactory _textureFactory;
        public DxtTextureLoader(ITextureFactory textureFactory) { _textureFactory = textureFactory; }
        public void Load(byte[] data, out bool hasAlphaChannel) { hasAlphaChannel = true; }
        public ITexture2D ToTexture() => _textureFactory.CreateWhiteTexture();
    }

    public sealed class BmpTextureLoader : ITextureLoader
    {
        private readonly ITextureFactory _textureFactory;
        public BmpTextureLoader(ITextureFactory textureFactory) { _textureFactory = textureFactory; }
        public void Load(byte[] data, out bool hasAlphaChannel) { hasAlphaChannel = false; }
        public ITexture2D ToTexture() => _textureFactory.CreateWhiteTexture();
    }
}

namespace Pal3.Game.Data
{
    /// <summary>
    /// Unity-engine-backed ITexture2D. Wraps a UnityEngine.Texture2D stub; pixel/sprite
    /// operations are deferred until real UE textures are wired.
    /// </summary>
    public sealed class UnityTexture2D : ITexture2D
    {
        private readonly UnityEngine.Texture2D _texture;
        public UnityTexture2D(UnityEngine.Texture2D texture) { _texture = texture; }

        public int Width => _texture?.width ?? 0;
        public int Height => _texture?.height ?? 0;
        public object NativeObject => (object?)_texture ?? this;
        public bool IsNativeObjectDisposed => false;

        public void SetWrapMode(WrapMode wrapMode) { }
        public (float r, float g, float b, float a) GetPixel(int x, int y) => (1f, 1f, 1f, 1f);
        public ISprite CreateSprite(float x, float y, float width, float height, float pivotX, float pivotY)
            => new UnitySprite(this);
        public void Destroy() { }
    }

    public sealed class UnitySprite : ISprite
    {
        public ITexture2D Texture { get; }
        public object NativeObject => this;
        public bool IsNativeObjectDisposed => false;
        public UnitySprite(ITexture2D texture) { Texture = texture; }
        public void Destroy() { }
    }

    public sealed class UnityTextureFactory : ITextureFactory
    {
        public ITexture2D CreateTexture(int width, int height, byte[] rgba32DataBuffer) => new UnityTexture2D(new UnityEngine.Texture2D(width, height));
        public ITexture2D CreateWhiteTexture() => new UnityTexture2D(new UnityEngine.Texture2D(1, 1));
    }

    public sealed class UnityMaterial : IMaterial
    {
        private readonly UnityEngine.Material _material;
        public UnityMaterial(UnityEngine.Material material, bool isClone) { _material = material; }

        public string ShaderName => _material?.shader?.name ?? "";
        public object NativeObject => (object?)_material ?? this;
        public bool IsNativeObjectDisposed => false;

        public float GetFloat(int propertyId) => _material?.GetFloat(PropertyIdToName(propertyId)) ?? 0f;
        public int GetInt(int propertyId) => _material?.GetInt(PropertyIdToName(propertyId)) ?? 0;
        public void SetInt(int propertyId, int value) { }
        public void SetFloat(int propertyId, float value) { }
        public void SetColor(int propertyId, Pal3.Core.Primitives.Color value) { }
        public void SetMainTexture(ITexture2D texture) { }
        public void SetTexture(int propertyId, ITexture2D value) { }
        public void SetMainTextureScale(float x, float y) { }
        public void SetVector4(int propertyId, UnityEngine.Vector4 value) { }
        public void Destroy() { }

        private static string PropertyIdToName(int id) => id.ToString();
    }

    public sealed class UnityMaterialFactory : IMaterialFactory
    {
        public IMaterial CreateMaterial(string shaderName) => new UnityMaterial(new UnityEngine.Material { shader = UnityEngine.Shader.Find(shaderName) }, isClone: false);
        public IMaterial CreateMaterialFrom(IMaterial material) => new UnityMaterial(new UnityEngine.Material(), isClone: true);
    }

    /// <summary>
    /// Audio clip loader (Unity-era async load). Loading is deferred; the callback
    /// fires synchronously with an empty clip so the coroutine completes.
    /// </summary>
    public static class AudioClipLoader
    {
        public static IEnumerator LoadAudioClipAsync(string filePath,
            UnityEngine.AudioType audioType,
            bool streamAudio,
            Action<UnityEngine.AudioClip>? onLoaded)
        {
            onLoaded?.Invoke(new UnityEngine.AudioClip());
            yield break;
        }
    }

    public static class UnitySupportedVideoFormats
    {
        public static HashSet<string> GetSupportedVideoFormats(UnityEngine.RuntimePlatform platform)
            => new HashSet<string> { ".mp4", ".mov", ".webm" };
    }
}

namespace Pal3.Game.Settings
{
    /// <summary>
    /// Stub transactional key-value store (Unity-era PlayerPrefs). Values are kept
    /// in memory for the session; nothing is persisted to disk yet.
    /// </summary>
    public sealed class PlayerPrefsStore : Pal3.Core.Utilities.ITransactionalKeyValueStore
    {
        private readonly Dictionary<string, object> _values = new();

        public void Set<T>(string key, T value) => _values[key] = value!;

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out object? stored) && stored is T typed)
            {
                value = typed;
                return true;
            }
            value = default!;
            return false;
        }

        public void DeleteKey(string key) => _values.Remove(key);

        public void Commit() { }
    }
}
