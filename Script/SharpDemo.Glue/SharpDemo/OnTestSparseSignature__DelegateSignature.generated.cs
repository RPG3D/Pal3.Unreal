using UnrealSharp.Attributes;
using UnrealSharp.Core.Attributes;
using UnrealSharp.Interop;
using UnrealSharp.Core.Marshallers;

namespace UnrealSharp.SharpDemo;


[GeneratedType("OnTestSparseSignature__DelegateSignature", "UnrealSharp.SharpDemo.OnTestSparseSignature__DelegateSignature"), UMultiDelegate]
public delegate void OnTestSparseSignature(float floatParam, int intParam);

public partial class UOnTestSparseSignature : MulticastDelegate<OnTestSparseSignature>
{
    // FOnTestSparseSignature
    static IntPtr FOnTestSparseSignature_NativeFunction;
    static int FOnTestSparseSignature_ParamsSize;
    static int FOnTestSparseSignature_FloatParam_Offset;
    static int FOnTestSparseSignature_IntParam_Offset;
    
    protected void Invoker(float floatParam, int intParam)
    {
        unsafe
        {
            byte* paramsBufferAllocation = stackalloc byte[FOnTestSparseSignature_ParamsSize];
            nint paramsBuffer = (nint) paramsBufferAllocation;
            BlittableMarshaller<float>.ToNative(IntPtr.Add(paramsBuffer, FOnTestSparseSignature_FloatParam_Offset), 0, floatParam);
            BlittableMarshaller<int>.ToNative(IntPtr.Add(paramsBuffer, FOnTestSparseSignature_IntParam_Offset), 0, intParam);
            
            ProcessDelegate(paramsBuffer);
            
        }
    }
    static public void InitializeUnrealDelegate(IntPtr nativeDelegateProperty)
    {
        FOnTestSparseSignature_NativeFunction = FMulticastDelegatePropertyExporter.CallGetSignatureFunction(nativeDelegateProperty);
        FOnTestSparseSignature_ParamsSize = UFunctionExporter.CallGetNativeFunctionParamsSize(FOnTestSparseSignature_NativeFunction);
        FOnTestSparseSignature_FloatParam_Offset = FPropertyExporter.CallGetPropertyOffsetFromName(FOnTestSparseSignature_NativeFunction, "FloatParam");
        FOnTestSparseSignature_IntParam_Offset = FPropertyExporter.CallGetPropertyOffsetFromName(FOnTestSparseSignature_NativeFunction, "IntParam");
    }
}