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

    public class AudioSource : Component, IDisposable
    {
        public AudioClip? clip;
        public bool loop;
        public bool mute;
        public float spatialBlend;
        public float maxDistance = 500f;
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        private float _volume = 1f;
        private int _handle;
        private readonly List<int> _oneShots = new();

        public bool isPlaying
        {
            get
            {
                _oneShots.RemoveAll(handle => !UnrealSharp.SharpDemo.UPal3AudioHelper.IsAudioPlaying(handle));
                return (_handle != 0 && UnrealSharp.SharpDemo.UPal3AudioHelper.IsAudioPlaying(_handle)) || _oneShots.Count > 0;
            }
        }

        public float volume
        {
            get => _volume;
            set
            {
                _volume = value;
                if (_handle != 0)
                {
                    UnrealSharp.SharpDemo.UPal3AudioHelper.SetAudioVolume(_handle, value);
                }
            }
        }

        public void Play()
        {
            StopClip();
            if (!enabled || clip?.rawData == null) return;
            _handle = UnrealSharp.SharpDemo.UPal3AudioHelper.PlayAudio(
                clip.rawData, clip.audioType, mute ? 0f : _volume, loop ? -1 : 1);
        }

        public void Stop()
        {
            StopClip();
            foreach (int handle in _oneShots)
            {
                UnrealSharp.SharpDemo.UPal3AudioHelper.StopAudio(handle);
            }
            _oneShots.Clear();
        }

        private void StopClip()
        {
            if (_handle != 0)
            {
                UnrealSharp.SharpDemo.UPal3AudioHelper.StopAudio(_handle);
                _handle = 0;
            }
        }

        public void Pause() { }
        public void UnPause() { }

        public void PlayOneShot(AudioClip oneShotClip, float volumeScale = 1f)
        {
            if (!enabled || oneShotClip?.rawData == null) return;
            _oneShots.RemoveAll(shotHandle => !UnrealSharp.SharpDemo.UPal3AudioHelper.IsAudioPlaying(shotHandle));
            int handle = UnrealSharp.SharpDemo.UPal3AudioHelper.PlayAudio(
                oneShotClip.rawData, oneShotClip.audioType, (mute ? 0f : _volume) * volumeScale, 1);
            if (handle != 0) _oneShots.Add(handle);
        }

        public void Dispose()
        {
            enabled = false;
            Stop();
        }
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

        /// <summary>
        /// Drops subscribers from the previous PIE session. Pal3.Start subscribes
        /// a handler that destroys the logo and calls Pal3.Instance (now the new
        /// session's instance) — stale handlers must never run again.
        /// </summary>
        public static void ResetForNewSession()
        {
            onEvent = null;
            onActionChange = null;
        }
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

        // Keyboard bridge (APal3Driver polls UE keys and injects values here — the
        // real InputSystem backend does not exist yet). ReadValue returns the last
        // injected value when its type matches, so Movement.ReadValue<Vector2>()
        // and friends behave like a real device.
        private object? _injectedValue;
        private bool _pressedThisFrame;

        public void InjectValue<T>(T value) where T : struct => _injectedValue = value;
        public void InjectPressed() => _pressedThisFrame = true;

        public T ReadValue<T>() where T : struct => _injectedValue is T typed ? typed : default;
        public bool IsPressed() => _injectedValue switch
        {
            float f => f > 0.01f,
            Vector2 v2 => v2.magnitude > 0.01f,
            _ => false
        };
        public bool WasPressedThisFrame()
        {
            bool pressed = _pressedThisFrame;
            _pressedThisFrame = false;
            return pressed;
        }

        /// <summary>
        /// Fires the callbacks used by the keyboard bridge and automation.
        /// </summary>
        public void SimulatePerformed()
        {
            started?.Invoke(default);
            performed?.Invoke(default);
        }
        public void SimulateCanceled() => canceled?.Invoke(default);
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
        public RectTransform rectTransform => GetComponent<RectTransform>();
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
        private string _text = "";
        public string text
        {
            get => _text;
            set
            {
                _text = value ?? "";
            }
        }
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
    using global::Pal3.Game.Data;

    /// <summary>
    /// Static-mesh renderer stub. The full Render(...) surface mirrors the call shape in
    /// CvdModelRenderer so the ported mesh pipeline compiles; real UE rendering is deferred.
    /// </summary>
    /// <summary>
    /// Mesh renderer backed by a real UE UProceduralMeshComponent. The ported game
    /// hands over Unity-space vertex data (meters, X-right/Y-up/Z-forward); every
    /// position/normal goes through UnityUeConvert at this boundary. Materials are
    /// ignored for now - UE's default material is used (texture pipeline is a later
    /// phase). This is what makes the PAL3 level visible at all.
    /// </summary>
    public class StaticMeshRenderer : Engine.Core.Implementation.IUeStubComponent, IDisposable
    {
        public bool receiveShadows;
        public bool ReceiveShadows { get => receiveShadows; set => receiveShadows = value; }
        public bool castShadows = true;
        public bool IsVisible { get; set; } = true;

        private UnrealSharp.Engine.AActor? _actor;
        private UnrealSharp.ProceduralMeshComponent.UProceduralMeshComponent? _mesh;
        private UnityEngine.Mesh? _animatedMesh;
        private UnityEngine.Bounds? _localBounds;
        private UnityMaterial? _mv3Material;
        private Action? _onMeshBindingChanged;

        // Default lit material for every section: the ported game's own materials are
        // still stubs, and UE skips sections whose material is null.
        private static UnrealSharp.Engine.UMaterialInterface? _defaultMaterial;

        // Diagnostics: proves the ported level data actually reached UE.
        public static int UploadCount;
        public static int RenderCalls;
        public static int ReuploadCount;
        public static long TotalVertices;
        public static long TotalTriangles;
        // World-space (UE cm) bounds of everything uploaded, for camera placement.
        public static double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue;
        public static double MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
        private static bool _loggedFirstUpload;

        /// <summary>
        /// Resets per-session counters/bounds and the cached default material (PIE
        /// session reset). The material lives in the previous world's package and is
        /// garbage-collected with it.
        /// </summary>
        public static void ResetForNewSession()
        {
            _defaultMaterial = null;
            _loggedFirstUpload = false;
            UploadCount = 0;
            RenderCalls = 0;
            ReuploadCount = 0;
            TotalVertices = 0;
            TotalTriangles = 0;
            MinX = MinY = MinZ = double.MaxValue;
            MaxX = MaxY = MaxZ = double.MinValue;
        }

        public void AttachTo(Engine.Core.Implementation.UEGameEntity entity) => _actor = entity.Actor;

        public UnityEngine.Bounds GetRendererBounds()
        {
            if (_mesh == null || !UnrealSharp.CoreUObject.UObjectExtensions.IsValid(_mesh))
                return new UnityEngine.Bounds(_actor == null ? UnityEngine.Vector3.zero :
                    Engine.Core.Implementation.UnityUeConvert.ToUnityPosition(_actor.ActorLocation), UnityEngine.Vector3.zero);

            UnrealSharp.Engine.SystemLibrary.GetComponentBounds(_mesh, out var origin, out var extent, out _);
            return new UnityEngine.Bounds(Engine.Core.Implementation.UnityUeConvert.ToUnityPosition(origin),
                Engine.Core.Implementation.UnityUeConvert.ToUnityPosition(extent) * 2f);
        }
        public UnityEngine.Bounds GetMeshBounds() =>
            _localBounds ??= CalculateLocalBounds(_animatedMesh?.vertices);

        private static UnityEngine.Bounds CalculateLocalBounds(UnityEngine.Vector3[]? vertices)
        {
            if (vertices is not { Length: > 0 }) return default;

            var bounds = new UnityEngine.Bounds(vertices[0], UnityEngine.Vector3.zero);
            for (int i = 1; i < vertices.Length; ++i) bounds.Encapsulate(vertices[i]);
            return bounds;
        }
        public string FingerprintMesh()
        {
            if (_mesh == null) return string.Empty;
            long hash = UnrealSharp.SharpDemo.UPal3DebugOverlay.FingerprintMeshSection(_mesh, 0, out int vertexCount);
            return vertexCount == 0 ? string.Empty : $"{vertexCount}:{hash:x8}";
        }
        public bool IsVertexAnimated => _animatedMesh != null;
        public Engine.Core.Abstraction.IMaterial[] GetMaterials() => Array.Empty<Engine.Core.Abstraction.IMaterial>();

        public UnityEngine.Mesh Render(
            UnityEngine.Vector3[] vertices,
            int[] triangles,
            UnityEngine.Vector3[] normals,
            (int channel, UnityEngine.Vector2[] uvs) mainTextureUvs,
            (int channel, UnityEngine.Vector2[] uvs) secondaryTextureUvs,
            Engine.Core.Abstraction.IMaterial[] materials,
            bool isDynamic)
        {
            ++RenderCalls;
            if (_actor == null || vertices == null || vertices.Length == 0
                || triangles == null || triangles.Length < 3)
            {
                return new UnityEngine.Mesh();
            }

            // NOTE: Render is called once per model/action switch (Mv3ModelRenderer.Init
            // → Render with fresh buffers); per-frame vertex animation flows through
            // Mesh.SetVertices → OnMeshDataChanged (wired below), not through Render.

            // Sanitise before crossing into UE: real game data has nodes whose normal /
            // UV arrays do not match the vertex count, and mismatched buffer lengths
            // crash the engine side of CreateMeshSection.
            int vertexCount = vertices.Length;
            bool hasNormals = normals != null && normals.Length == vertexCount;
            bool hasUv0 = mainTextureUvs.uvs != null && mainTextureUvs.uvs.Length == vertexCount;
            bool hasUv1 = secondaryTextureUvs.uvs != null && secondaryTextureUvs.uvs.Length == vertexCount;
            bool trianglesValid = true;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (triangles[i] < 0 || triangles[i] >= vertexCount
                    || triangles[i + 1] < 0 || triangles[i + 1] >= vertexCount
                    || triangles[i + 2] < 0 || triangles[i + 2] >= vertexCount)
                {
                    trianglesValid = false;
                    break;
                }
            }
            if (!trianglesValid)
            {
                UnityEngine.Debug.LogWarning($"[StaticMeshRenderer] dropped mesh with out-of-range indices ({vertexCount} verts)");
                return new UnityEngine.Mesh();
            }

            var ueVertices = new UnrealSharp.CoreUObject.FVector[vertexCount];
            UnityMaterial? modelMaterial = materials is { Length: > 0 } ? materials[0] as UnityMaterial : null;
            Action? meshBindingChanged = null;
            void CopyVertices(UnityEngine.Vector3[] source)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    ueVertices[i] = Engine.Core.Implementation.UnityUeConvert.ToUePosition(source[i]);
                }
            }

            try
            {
                _mesh ??= _actor.AddComponentByClass<UnrealSharp.ProceduralMeshComponent.UProceduralMeshComponent>(
                    false, new UnrealSharp.CoreUObject.FTransform());

                if (_mesh.OverrideMaterials == null || _mesh.OverrideMaterials.Count == 0)
                {
                    // Prefer the game-provided materials (M_Pal3Tex MID with the
                    // bound texture); fall back to the engine default when absent.
                    var gameMats = new System.Collections.Generic.List<UnrealSharp.Engine.UMaterialInterface>();
                    foreach (Engine.Core.Abstraction.IMaterial gm in materials)
                    {
                        if (gm.NativeObject is UnrealSharp.Engine.UMaterialInterface umi)
                        {
                            gameMats.Add(umi);
                        }
                    }
                    if (gameMats.Count > 0)
                    {
                        _mesh.OverrideMaterials = gameMats;
                    }
                    else
                    {
                        UnrealSharp.Engine.UMaterialInterface mat = GetDefaultMaterial();
                        if (mat != null)
                        {
                            _mesh.OverrideMaterials = new System.Collections.Generic.List<UnrealSharp.Engine.UMaterialInterface> { mat };
                        }
                    }
                }

                CopyVertices(vertices);

                var ueNormals = new System.Collections.Generic.List<UnrealSharp.CoreUObject.FVector>(
                    hasNormals ? vertexCount : 0);
                if (hasNormals)
                {
                    foreach (UnityEngine.Vector3 n in normals)
                    {
                        ueNormals.Add(Engine.Core.Implementation.UnityUeConvert.ToUeDirection(n));
                    }
                }

                var uv0 = new System.Collections.Generic.List<UnrealSharp.CoreUObject.FVector2D>(
                    hasUv0 ? vertexCount : 0);
                bool flipV = modelMaterial?.FlipMainTextureV == true;
                void CopyMainTextureUvs()
                {
                    if (!hasUv0) return;
                    uv0.Clear();
                    foreach (UnityEngine.Vector2 uv in mainTextureUvs.uvs)
                    {
                        uv0.Add(new UnrealSharp.CoreUObject.FVector2D(uv.x, TextureCoordinates.ConvertV(uv.y, flipV)));
                    }
                }
                CopyMainTextureUvs();

                var uv1 = new System.Collections.Generic.List<UnrealSharp.CoreUObject.FVector2D>(
                    hasUv1 ? vertexCount : 0);
                if (hasUv1)
                {
                    foreach (UnityEngine.Vector2 uv in secondaryTextureUvs.uvs)
                    {
                        uv1.Add(new UnrealSharp.CoreUObject.FVector2D(uv.x, uv.y));
                    }
                }

                // Unity winds triangles clockwise seen from the front; UE culls those.
                // Swap the last two indices so faces survive backface culling.
                var ueTriangles = new System.Collections.Generic.List<int>(triangles.Length);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    ueTriangles.Add(triangles[i]);
                    ueTriangles.Add(triangles[i + 2]);
                    ueTriangles.Add(triangles[i + 1]);
                }

                // Static level geometry keeps collision; animated sections only upload positions after creation.
                UnrealSharp.SharpDemo.UPal3DebugOverlay.CreateProceduralSection(
                    _mesh, 0, ueVertices, ueTriangles, ueNormals, uv0, uv1, !isDynamic);
                ++UploadCount;
                if (modelMaterial is { UsesMv3TextureCoordinates: true } mv3Material)
                {
                    var meshComponent = _mesh;
                    var currentMaterial = mv3Material.UeMaterial;
                    meshBindingChanged = () =>
                    {
                        if (_mesh != meshComponent || !UnrealSharp.CoreUObject.UObjectExtensions.IsValid(meshComponent))
                            return;
                        if (hasUv0 && flipV != mv3Material.FlipMainTextureV)
                        {
                            flipV = mv3Material.FlipMainTextureV;
                            CopyMainTextureUvs();
                            UnrealSharp.SharpDemo.UPal3DebugOverlay.CreateProceduralSection(
                                meshComponent, 0, ueVertices, ueTriangles, ueNormals, uv0, uv1, !isDynamic);
                            ++RenderCalls;
                            ++UploadCount;
                        }
                        if (currentMaterial != mv3Material.UeMaterial && mv3Material.UeMaterial is { } updatedMaterial)
                        {
                            meshComponent.SetMaterial(0, updatedMaterial);
                            currentMaterial = updatedMaterial;
                        }
                    };
                }
                UnrealSharp.CoreUObject.FVector actorPos = _actor.ActorLocation;
                foreach (UnrealSharp.CoreUObject.FVector p in ueVertices)
                {
                    double wx = p.X + actorPos.X, wy = p.Y + actorPos.Y, wz = p.Z + actorPos.Z;
                    if (wx < MinX) MinX = wx; if (wx > MaxX) MaxX = wx;
                    if (wy < MinY) MinY = wy; if (wy > MaxY) MaxY = wy;
                    if (wz < MinZ) MinZ = wz; if (wz > MaxZ) MaxZ = wz;
                }
                TotalVertices += ueVertices.Length;
                TotalTriangles += ueTriangles.Count / 3;
                if (!_loggedFirstUpload && ueVertices.Length > 0)
                {
                    _loggedFirstUpload = true;
                    double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                    foreach (UnrealSharp.CoreUObject.FVector p in ueVertices)
                    {
                        if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                        if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                    }
                    UnityEngine.Debug.Log($"[StaticMeshRenderer] first mesh: verts={ueVertices.Length} tris={ueTriangles.Count / 3} "
                        + $"bounds=({minX:F0},{minY:F0},{minZ:F0})..({maxX:F0},{maxY:F0},{maxZ:F0})"
                        + $" actor={_actor.Name} at=({actorPos.X:F0},{actorPos.Y:F0},{actorPos.Z:F0})");
                    float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                    if (mainTextureUvs.uvs != null)
                    {
                        foreach (UnityEngine.Vector2 uv in mainTextureUvs.uvs)
                        {
                            if (uv.x < uMin) uMin = uv.x; if (uv.x > uMax) uMax = uv.x;
                            if (uv.y < vMin) vMin = uv.y; if (uv.y > vMax) vMax = uv.y;
                        }
                        UnityEngine.Debug.Log($"[StaticMeshRenderer] uv0 range=({uMin:F2},{vMin:F2})..({uMax:F2},{vMax:F2}) count={mainTextureUvs.uvs.Length}");
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[StaticMeshRenderer] UE mesh upload failed: " + e.Message);
                return new UnityEngine.Mesh();
            }

            UnityEngine.Mesh result = new();
            DetachMaterial();
            if (meshBindingChanged != null)
            {
                _mv3Material = modelMaterial!;
                _onMeshBindingChanged = meshBindingChanged;
                _mv3Material.MeshBindingChanged += _onMeshBindingChanged;
            }
            _animatedMesh = isDynamic ? result : null;
            _localBounds = isDynamic ? null : CalculateLocalBounds(vertices);
            if (isDynamic)
            {
                result.vertices = vertices;
                result.OnMeshDataChanged = () =>
                {
                    if (_mesh == null || result.vertices.Length != vertexCount) return;
                    _localBounds = null;
                    CopyVertices(result.vertices);
                    UnrealSharp.SharpDemo.UPal3DebugOverlay.UpdateProceduralVertices(_mesh, 0, ueVertices);
                    ++UploadCount;
                    ++ReuploadCount;
                };
            }
            return result;
        }

        private static UnrealSharp.Engine.UMaterialInterface? GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            _defaultMaterial = UnrealSharp.CoreUObject.UObject.StaticLoadObject<UnrealSharp.Engine.UMaterial>(
                "/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial");
            _defaultMaterial ??= UnrealSharp.CoreUObject.UObject.StaticLoadObject<UnrealSharp.Engine.UMaterial>(
                "/Engine/EngineMaterials/DefaultMaterial.DefaultMaterial");
            UnityEngine.Debug.Log(_defaultMaterial != null
                ? "[StaticMeshRenderer] default material loaded: " + _defaultMaterial.Name
                : "[StaticMeshRenderer] NO default material found");
            return _defaultMaterial;
        }

        private void DetachMaterial()
        {
            if (_mv3Material != null) _mv3Material.MeshBindingChanged -= _onMeshBindingChanged;
            _mv3Material = null;
            _onMeshBindingChanged = null;
        }

        public void Dispose()
        {
            DetachMaterial();
            if (UnrealSharp.CoreUObject.UObjectExtensions.IsValid(_mesh)
                && UnrealSharp.CoreUObject.UObjectExtensions.IsValid(_actor))
            {
                _mesh!.DestroyComponent(_actor!);
            }
            _animatedMesh?.Destroy();
            _mesh = null;
            _animatedMesh = null;
            _localBounds = null;
        }

        public void Destroy() => Dispose();
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
    /// <summary>
    /// Text-command console (IngameDebugConsole in Unity). The ported game queues
    /// strings such as "CameraSetTransform -21.69 -22.48 688 182.87 263.07 531.61"
    /// (MainMenu's init view) and expects them to run through the registered
    /// handlers. Upstream registered one typed Action per command; here the name is
    /// resolved to the matching SceCommand class and constructed reflectively with
    /// the parsed arguments, then dispatched through Pal3Game.Execute — same
    /// dispatch path the .sce parser uses.
    /// </summary>
    public static class DebugLogConsole
    {
        private static readonly Dictionary<string, Type> CommandTypes = new(StringComparer.OrdinalIgnoreCase);

        public static void AddCommand(string command, string description, Action method) { }
        public static void AddCommand<T>(string command, string description, Action<T> method) { }
        public static void AddCommandStatic(string command, string description, Type type, string methodName) { }

        public static void RemoveCommand(string command)
        {
            lock (CommandTypes) { CommandTypes.Remove(command); }
        }

        public static void ExecuteCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0];
            Type? commandType = ResolveType(name);
            if (commandType == null)
            {
                Engine.Logging.EngineLogger.LogWarning($"[Console] Unknown text command: {command}");
                return;
            }

            try
            {
                var ctor = commandType.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
                var parameters = ctor.GetParameters();
                object?[] args = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    string token = i + 1 < parts.Length ? parts[i + 1] : string.Empty;
                    args[i] = ConvertToken(token, parameters[i].ParameterType);
                }

                object instance = ctor.Invoke(args);
                if (instance is Pal3.Core.Command.ICommand pal3Command)
                {
                    global::Pal3.Game.Pal3.Instance.Execute(pal3Command);
                }
            }
            catch (Exception error)
            {
                Engine.Logging.EngineLogger.LogWarning(
                    $"[Console] Failed to execute '{command}': {error.GetBaseException().Message}");
            }
        }

        private static object? ConvertToken(string token, Type type)
        {
            if (type == typeof(string)) return token;
            if (type == typeof(bool)) return token is "1" or "true" or "True";
            if (type == typeof(byte)) return byte.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(short)) return short.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(ushort)) return ushort.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(int)) return int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(uint)) return uint.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(long)) return long.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(token) ? null : Convert.ChangeType(token, type);
        }

        private static Type? ResolveType(string commandName)
        {
            lock (CommandTypes)
            {
                if (CommandTypes.TryGetValue(commandName, out Type? cached)) return cached;

                // Text commands are the Sce command class names without the suffix
                // (CameraSetTransform -> CameraSetTransformCommand).
                Type? found = typeof(Pal3.Core.Command.ICommand).Assembly.GetTypes().FirstOrDefault(t =>
                    t.Namespace == "Pal3.Core.Command.SceCommands" &&
                    t.Name.Equals(commandName + "Command", StringComparison.OrdinalIgnoreCase));
                if (found != null) CommandTypes[commandName] = found;
                return found;
            }
        }

        public static void FetchArgumentsFromCommand(string command, List<string> arguments)
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length; i++) arguments.Add(parts[i]);
        }
    }

    public class DebugLogManager
    {
        private static readonly DebugLogManager _instance = new();
        public static DebugLogManager Instance => _instance;
        public event Action? OnLogWindowShown;
        public event Action? OnLogWindowHidden;
        public UnityEngine.GameObject gameObject => new();

        /// <summary>Drops the previous session's Pal3 subscribers (PIE restart).</summary>
        public static void ResetForNewSession()
        {
            _instance.OnLogWindowShown = null;
            _instance.OnLogWindowHidden = null;
        }
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

// REMOVED: the stub Engine.DataLoader.TgaTextureLoader that used to live here.
// Because Pal3.Game defines the type itself, it shadowed the real TGA decoder in the
// Engine assembly (CS0436) and every .tga texture (all character textures!) became a
// 1x1 white stub with no UE texture → characters rendered untextured/black.

namespace Pal3.Game.Data
{
    /// <summary>
    /// Wraps a UE texture and retains the alpha metadata used by PAL3.
    /// </summary>
    public sealed class UnityTexture2D : ITexture2D
    {
        private readonly UnityEngine.Texture2D _texture;
        private readonly float _firstPixelAlpha = 1f;
        private readonly (float Left, float Top, float Right, float Bottom)? _alphaBounds;
        private bool _destroyed;
        public UnrealSharp.Engine.UTexture2D? UeTexture { get; private set; }
        internal TexturePixelRows PixelRows { get; set; }

        public UnityTexture2D(UnityEngine.Texture2D texture)
        {
            _texture = texture;
        }

        // Full pipeline: rgba bytes -> UE UTexture2D (via UPal3AssetHelper).
        public UnityTexture2D(int width, int height, byte[] rgba)
        {
            _texture = new UnityEngine.Texture2D(width, height);
            long pixels = (long)width * height;
            if (width > 0 && height > 0 && pixels <= rgba.Length / 4)
            {
                // Read alpha metadata before the decoder returns its rented buffer.
                _firstPixelAlpha = rgba[3] / 255f;
                _alphaBounds = FindAlphaBounds(rgba, width, height);
                UeTexture = UnrealSharp.SharpDemo.UPal3AssetHelper.CreateTextureFromRgba(
                    width, height, new ArraySegment<byte>(rgba, 0, (int)pixels * 4));
            }
            Pal3TransientAssets.Track(this, UeTexture);
            if (UeTexture == null && TextureCreateFailures++ < 8)
            {
                UnityEngine.Debug.LogWarning($"[UnityTexture2D] UE texture failed {width}x{height} "
                    + $"len={rgba?.Length ?? -1} expected={(long)width * height * 4}");
            }
        }

        public static int TextureCreateFailures;

        public int Width => _texture?.width ?? 0;
        public int Height => _texture?.height ?? 0;
        public float FirstPixelAlpha => _destroyed ? 1f : _firstPixelAlpha;
        public object NativeObject => UeTexture ?? (object?)_texture ?? this;
        public bool IsNativeObjectDisposed => _destroyed;

        public void SetWrapMode(WrapMode wrapMode) { }
        /// <summary>
        /// Normalized UV rectangle of the non-transparent pixels (avatar portraits
        /// ship with large transparent padding; the HUD dialogue bar crops to this).
        /// Returns false when no sampled pixel meets the alpha threshold.
        /// </summary>
        public bool TryGetAlphaBounds(out float u0, out float v0, out float u1, out float v1)
        {
            if (!_destroyed && _alphaBounds is { } bounds)
            {
                (u0, v0, u1, v1) = bounds;
                return true;
            }
            (u0, v0, u1, v1) = (0f, 0f, 1f, 1f);
            return false;
        }

        private static (float Left, float Top, float Right, float Bottom)? FindAlphaBounds(
            byte[] rgba, int width, int height)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            int step = System.Math.Max(1, System.Math.Min(width, height) / 128);
            for (int y = 0; y < height; y += step)
            {
                int row = y * width;
                for (int x = 0; x < width; x += step)
                {
                    if (rgba[(row + x) * 4 + 3] <= 8) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? null : ((float)minX / width, (float)minY / height,
                (float)(maxX + 1) / width, (float)(maxY + 1) / height);
        }
        public ISprite CreateSprite(float x, float y, float width, float height, float pivotX, float pivotY)
            => new UnitySprite(this, new UnityEngine.Rect(x, y, width, height),
                new UnityEngine.Vector2(pivotX, pivotY));
        public void Destroy()
        {
            _destroyed = true;
            Pal3TransientAssets.Release(UeTexture);
            UeTexture = null;
        }
    }

    public sealed class UnitySprite : ISprite
    {
        public ITexture2D Texture { get; }
        public object NativeObject => this;
        public bool IsNativeObjectDisposed => false;
        public UnityEngine.Rect TextureRect { get; }
        public UnityEngine.Vector2 Pivot { get; }

        public UnitySprite(ITexture2D texture, UnityEngine.Rect textureRect, UnityEngine.Vector2 pivot)
        {
            Texture = texture;
            TextureRect = textureRect;
            Pivot = pivot;
        }

        /// <summary>Normalizes a bottom-origin sprite rectangle to the decoded texture's UV coordinates.</summary>
        public (float Left, float Top, float Right, float Bottom) GetTextureUVs()
            => GetTextureUVs(rowsBottomUp: false);

        internal (float Left, float Top, float Right, float Bottom) GetTextureUVs(bool rowsBottomUp)
        {
            var rect = TextureRect;
            bool flip = !rowsBottomUp;
            return (rect.xMin / Texture.Width, TextureCoordinates.ConvertV(rect.yMax / Texture.Height, flip),
                rect.xMax / Texture.Width, TextureCoordinates.ConvertV(rect.yMin / Texture.Height, flip));
        }

        public void Destroy() { }
    }

    public sealed class UnityTextureFactory : ITextureFactory
    {
        public ITexture2D CreateTexture(int width, int height, byte[] rgba32DataBuffer)
            => new UnityTexture2D(width, height, rgba32DataBuffer);
        public ITexture2D CreateWhiteTexture() => new UnityTexture2D(new UnityEngine.Texture2D(1, 1));
    }

    public sealed class UnityMaterial : IMaterial
    {
        private readonly UnityEngine.Material? _material;
        private readonly string _shaderName = "";
        public UnrealSharp.Engine.UMaterialInterface? UeMaterial { get; private set; }
        internal bool UsesMv3TextureCoordinates { get; set; }
        internal bool FlipMainTextureV { get; set; }
        internal event Action? MeshBindingChanged;

        internal void NotifyMeshBindingChanged() => MeshBindingChanged?.Invoke();

        public UnityMaterial(UnityEngine.Material material, bool isClone) { _material = material; }

        // Runtime material: MID of /Game/PAL3/M_Pal3Tex (Pal3Tex texture param).
        public UnityMaterial() : this("")
        {
        }

        public UnityMaterial(string shaderName)
        {
            _shaderName = shaderName ?? "";
            _blendMode = IsTransparentShader(_shaderName) ? 2 : 0;
            UeMaterial = UnrealSharp.SharpDemo.UPal3AssetHelper.CreatePal3MaterialBlend(_blendMode);
            Pal3TransientAssets.Track(this, UeMaterial);
        }

        // "RealToon/.../Fade Transparency", "Pal3/Transparent" and
        // "Pal3/TransparentOpaquePart" are the game's transparent material variants.
        private static bool IsTransparentShader(string shaderName)
            => shaderName.Contains("Transparent", StringComparison.OrdinalIgnoreCase)
            || shaderName.Contains("Fade", StringComparison.OrdinalIgnoreCase);

        public string ShaderName => _shaderName.Length > 0 ? _shaderName : (_material?.shader?.name ?? "");
        public object NativeObject => UeMaterial ?? (object?)_material ?? this;
        public bool IsNativeObjectDisposed => false;

        public float GetFloat(int propertyId) => _material?.GetFloat(PropertyIdToName(propertyId)) ?? 0f;
        public int GetInt(int propertyId) => _material?.GetInt(PropertyIdToName(propertyId)) ?? 0;

        public void SetInt(int propertyId, int value)
        {
            string? name = UnityEngine.Shader.GetPropertyName(propertyId);
            if (name == "_BlendDstFactor")
            {
                // Unity BlendMode: One = 1 -> additive (InvertColorBlend), 
                // OneMinusSrcAlpha = 10 -> alpha blend.
                if (value == 1) SwitchBlend(1);
                else if (value == 10) SwitchBlend(2);
                return;
            }
            if (name == "_BlendSrcFactor") return; // SrcAlpha for both variants
            SetFloat(propertyId, value);
        }

        // 0 = opaque M_Pal3Tex, 1 = additive, 2 = translucent, 3 = masked (cutout).
        private int _blendMode;
        private float _cutout;
        private UnrealSharp.Engine.UTexture2D? _mainTexture;
        private static UnityTexture2D? _whiteTexture;
        private static readonly HashSet<UnrealSharp.TWeakObjectPtr<UnrealSharp.Engine.UMaterialInterface>> LocalLightMaterials = new();
        private bool _tracksLocalLight;

        private static UnrealSharp.Engine.UTexture2D? GetWhiteTexture()
        {
            _whiteTexture ??= new UnityTexture2D(1, 1, new byte[] { 255, 255, 255, 255 });
            return _whiteTexture.UeTexture;
        }

        internal static void ReleaseSharedAssets()
        {
            _whiteTexture?.Destroy();
            _whiteTexture = null;
            LocalLightMaterials.Clear();
        }

        private void SwitchBlend(int mode)
        {
            if (_blendMode == mode) return;
            var previousMaterial = UeMaterial;
            _blendMode = mode;
            UeMaterial = UnrealSharp.SharpDemo.UPal3AssetHelper.CreatePal3MaterialBlend(mode);
            _tracksLocalLight = false;
            Pal3TransientAssets.Track(this, UeMaterial);
            Pal3TransientAssets.Release(previousMaterial);
            if (_mainTexture != null && UeMaterial != null)
            {
                UnrealSharp.SharpDemo.UPal3AssetHelper.BindTexture(
                    UeMaterial, new UnrealSharp.Core.FName("Pal3Tex"), _mainTexture);
            }
            ApplyTint();
        }


        // The Unity "Pal3/Opaque" shader is ported to /Game/PAL3/M_Pal3Tex:
        //   emissive = MainTex * TintColor * lerp(1, ShadowTex/(1-Exposure), HasShadowTex)
        // The ported game only speaks Unity property ids, so map the handful of
        // parameters the material actually declares.
        public void SetFloat(int propertyId, float value)
        {
            if (UeMaterial == null) return;
            switch (UnityEngine.Shader.GetPropertyName(propertyId))
            {
                case "_HasShadowTex":
                    UnrealSharp.SharpDemo.UPal3AssetHelper.SetScalarParameter(
                        UeMaterial, new UnrealSharp.Core.FName("HasShadowTex"), value);
                    break;
                case "_Exposure":
                    UnrealSharp.SharpDemo.UPal3AssetHelper.SetScalarParameter(
                        UeMaterial, new UnrealSharp.Core.FName("ShadowExposure"), value);
                    break;
                case "_Cutout":
                    // 0.3 = alpha cutout (Mv3 hair/cloth masks), 0 = fully opaque.
                    _cutout = value;
                    if (_blendMode == 0 || _blendMode == 3)
                    {
                        SwitchBlend(value > 0.001f ? 3 : 0);
                    }
                    break;
            }
        }

        public void SetColor(int propertyId, global::Pal3.Core.Primitives.Color value)
        {
            if (UeMaterial == null) return;
            if (UnityEngine.Shader.GetPropertyName(propertyId) != "_TintColor") return;
            _tint = value;
            ApplyTint();
        }

        // Game tint * scene lighting approximation (see UnityEngine.SceneLighting).
        private global::Pal3.Core.Primitives.Color _tint = new(1f, 1f, 1f, 1f);

        private void ApplyTint()
        {
            if (UeMaterial == null) return;
            UnityEngine.Color scale = UnityEngine.SceneLighting.SurfaceScale;
            UnrealSharp.SharpDemo.UPal3AssetHelper.SetVectorParameter(
                UeMaterial,
                new UnrealSharp.Core.FName("TintColor"),
                new UnrealSharp.CoreUObject.FLinearColor(
                    _tint.R * scale.r, _tint.G * scale.g, _tint.B * scale.b, _tint.A));
            if (!_tracksLocalLight)
            {
                _tracksLocalLight = true;
                LocalLightMaterials.Add(new(UeMaterial));
                if (UnityEngine.SceneLighting.LocalLightPosition is { } localLight)
                    ApplyLocalLight(UeMaterial, EncodeLocalLight(localLight));
            }
        }

        internal static void SetSceneLocalLight(UnityEngine.Vector3? position)
        {
            bool changed = UnityEngine.SceneLighting.LocalLightPosition != position;
            UnityEngine.SceneLighting.LocalLightPosition = position;
            var parameter = EncodeLocalLight(position);
            LocalLightMaterials.RemoveWhere(reference =>
            {
                if (reference.Object is not { } material) return true;
                if (changed) ApplyLocalLight(material, parameter);
                return false;
            });
        }

        private static UnrealSharp.CoreUObject.FLinearColor EncodeLocalLight(UnityEngine.Vector3? position)
        {
            if (position is not { } localLight) return new(0, 0, 0, 0);
            var uePosition = Engine.Core.Implementation.UnityUeConvert.ToUePosition(localLight);
            return new((float)uePosition.X, (float)uePosition.Y, (float)uePosition.Z, 430f);
        }

        private static void ApplyLocalLight(UnrealSharp.Engine.UMaterialInterface material,
            UnrealSharp.CoreUObject.FLinearColor parameter)
            => UnrealSharp.SharpDemo.UPal3AssetHelper.SetVectorParameter(material,
                new UnrealSharp.Core.FName("LocalLight"), parameter);


        public void SetMainTexture(ITexture2D texture) => SetTexture(0, texture);

        public void SetTexture(int propertyId, ITexture2D value)
        {
            ++SetTextureCalls;

            // Material-pool reset path: SetMainTexture(null) means "clear". Binding a
            // shared white texture (instead of leaving the previous one) avoids a
            // pooled translucent material showing the last object's texture.
            if (value == null)
            {
                _mainTexture = null;
                if (UeMaterial != null)
                {
                    UnrealSharp.SharpDemo.UPal3AssetHelper.BindTexture(
                        UeMaterial, new UnrealSharp.Core.FName("Pal3Tex"), GetWhiteTexture());
                }
                return;
            }

            if (UeMaterial == null)
            {
                if (++BindFailures <= 3)
                {
                    var st = new System.Diagnostics.StackTrace(1, false);
                    var frames = st.GetFrames();
                    string caller = frames != null && frames.Length > 1 ? frames[1].GetMethod()?.DeclaringType?.Name + "." + frames[1].GetMethod()?.Name : "?";
                    UnityEngine.Debug.LogWarning($"[UnityMaterial] bind skipped: UeMaterial null (this={GetType().Name}#{GetHashCode():X}, caller={caller})");
                }
                return;
            }
            if (value is not UnityTexture2D { UeTexture: not null } tex)
            {
                if (++BindFailures <= 3) UnityEngine.Debug.LogWarning($"[UnityMaterial] bind skipped: texture type {value?.GetType().Name} ueTex={(value as UnityTexture2D)?.UeTexture != null}");
                return;
            }

            string? name = propertyId == 0
                ? "_MainTex"
                : UnityEngine.Shader.GetPropertyName(propertyId) ?? "_MainTex";

            string? parameter = name switch
            {
                "_ShadowTex" => "ShadowTex",
                "_MainTex" or "MainTex" or "_BaseMap" => "Pal3Tex",
                _ => null, // skybox / other slots are not ported yet
            };
            if (parameter == null) return;

            UnrealSharp.SharpDemo.UPal3AssetHelper.BindTexture(
                UeMaterial, new UnrealSharp.Core.FName(parameter), tex.UeTexture);
            if (parameter == "Pal3Tex") _mainTexture = tex.UeTexture;
            ApplyTint();
        }

        public static int SetTextureCalls;
        public static int BindFailures;
        public void SetMainTextureScale(float x, float y) { }
        public void SetVector4(int propertyId, UnityEngine.Vector4 value) { }
        public void Destroy()
        {
            Pal3TransientAssets.Release(UeMaterial);
            UeMaterial = null;
            _mainTexture = null;
        }

        private static string PropertyIdToName(int id) => id.ToString();
    }

    public sealed class UnityMaterialFactory : IMaterialFactory
    {
        public IMaterial CreateMaterial(string shaderName)
        {
            // The Unity shader does not exist in UE; the shader NAME is kept so the
            // clone pool preserves the opaque/transparent variant (M_Pal3Tex /
            // M_Pal3Tex_Alpha / M_Pal3Tex_Add).
            return new UnityMaterial(shaderName);
        }

        public IMaterial CreateMaterialFrom(IMaterial material)
        {
            // Every instance gets its OWN MID (upstream clones materials per
            // instance so pool pops can mutate them independently).
            return new UnityMaterial((material as UnityMaterial)?.ShaderName ?? "");
        }
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

}

namespace Pal3.Game.Settings
{
    /// <summary>
    /// Stub transactional key-value store (Unity-era PlayerPrefs). Values are kept
    /// in memory for the session; nothing is persisted to disk yet.
    /// </summary>
    public sealed class PlayerPrefsStore : global::Pal3.Core.Utilities.ITransactionalKeyValueStore
    {
        // Process-wide seed values consulted when an instance has no value for a
        // key. Lets the bootstrapper (Pal3Boot.RunFullBoot) supply the PAL3 game
        // data folder path before GameResourceInitializer creates its own store.
        private static readonly Dictionary<string, object> _seed = new();

        public static void SetSeed<T>(string key, T value) => _seed[key] = value!;

        private readonly Dictionary<string, object> _values = new();

        public void Set<T>(string key, T value) => _values[key] = value!;

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out object? stored) && stored is T typed)
            {
                value = typed;
                return true;
            }
            if (_seed.TryGetValue(key, out object? seeded) && seeded is T seededTyped)
            {
                value = seededTyped;
                return true;
            }
            value = default!;
            return false;
        }

        public void DeleteKey(string key) => _values.Remove(key);

        public void Commit() { }
    }
}
