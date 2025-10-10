using UnrealSharp.Attributes;
using UnrealSharp.Core.Attributes;
using UnrealSharp.Interop;
using UnrealSharp.Core.Marshallers;

namespace UnrealSharp.SharpDemo;

[UClass, GeneratedType("SharpDemoCharacter", "UnrealSharp.SharpDemo.SharpDemoCharacter")]
public partial class ASharpDemoCharacter : UnrealSharp.Engine.ACharacter
{
    static readonly IntPtr NativeClassPtr;
    static ASharpDemoCharacter()
    {
        NativeClassPtr = UCoreUObjectExporter.CallGetNativeClassFromName(typeof(ASharpDemoCharacter).GetAssemblyName(), "UnrealSharp.SharpDemo", "SharpDemoCharacter");
        OnTestSparseEvent_NativeProperty = FPropertyExporter.CallGetNativePropertyFromName(NativeClassPtr, "OnTestSparseEvent");
        OnTestSparseEvent_Offset = FPropertyExporter.CallGetPropertyOffset(OnTestSparseEvent_NativeProperty);
        UnrealSharp.SharpDemo.UOnTestSparseSignature.InitializeUnrealDelegate(OnTestSparseEvent_NativeProperty);
    }
    
    static int OnTestSparseEvent_Offset;
    static IntPtr OnTestSparseEvent_NativeProperty;
    private TMulticastDelegate<UnrealSharp.SharpDemo.OnTestSparseSignature> OnTestSparseEvent_BackingField;
    
    /// <summary>
    /// Called when the component has been activated, with parameter indicating if it was from a reset
    /// </summary>
    public TMulticastDelegate<UnrealSharp.SharpDemo.OnTestSparseSignature> OnTestSparseEvent
    {
        get
        {
            if (OnTestSparseEvent_BackingField == null)
            {
                OnTestSparseEvent_BackingField = MulticastDelegateMarshaller<UnrealSharp.SharpDemo.OnTestSparseSignature>.FromNative(IntPtr.Add(NativeObject, OnTestSparseEvent_Offset), OnTestSparseEvent_NativeProperty, 0);
            }
            return OnTestSparseEvent_BackingField;
        }
        set
        {
            if (value == OnTestSparseEvent_BackingField)
            {
                return;
            }
            OnTestSparseEvent_BackingField = value;
            MulticastDelegateMarshaller<UnrealSharp.SharpDemo.OnTestSparseSignature>.ToNative(IntPtr.Add(NativeObject, OnTestSparseEvent_Offset), 0, value);
        }
    }
    
    
}