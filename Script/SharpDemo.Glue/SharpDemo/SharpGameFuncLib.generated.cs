using UnrealSharp.Attributes;
using UnrealSharp.Core.Attributes;
using UnrealSharp.Interop;
using UnrealSharp.Core.Marshallers;

namespace UnrealSharp.SharpDemo;

[UClass, GeneratedType("SharpGameFuncLib", "UnrealSharp.SharpDemo.SharpGameFuncLib")]
public partial class USharpGameFuncLib : UnrealSharp.Engine.UBlueprintFunctionLibrary
{
    static readonly IntPtr NativeClassPtr;
    static USharpGameFuncLib()
    {
        NativeClassPtr = UCoreUObjectExporter.CallGetNativeClassFromName(typeof(USharpGameFuncLib).GetAssemblyName(), "UnrealSharp.SharpDemo", "SharpGameFuncLib");
        LoadAsset_NativeFunction = UClassExporter.CallGetNativeFunctionFromClassAndName(NativeClassPtr, "LoadAsset");
        LoadAsset_ParamsSize = UFunctionExporter.CallGetNativeFunctionParamsSize(LoadAsset_NativeFunction);
        LoadAsset_InAssetPath_Offset = FPropertyExporter.CallGetPropertyOffsetFromName(LoadAsset_NativeFunction, "InAssetPath");
        LoadAsset_ReturnValue_Offset = FPropertyExporter.CallGetPropertyOffsetFromName(LoadAsset_NativeFunction, "ReturnValue");
    }
    // LoadAsset
    static IntPtr LoadAsset_NativeFunction;
    static int LoadAsset_ParamsSize;
    static int LoadAsset_InAssetPath_Offset;
    static int LoadAsset_ReturnValue_Offset;
    
    [UFunction, GeneratedType("LoadAsset", "UnrealSharp.SharpDemo.LoadAsset")]
    public static UnrealSharp.CoreUObject.UObject LoadAsset(string assetPath)
    {
        unsafe
        {
            byte* paramsBufferAllocation = stackalloc byte[LoadAsset_ParamsSize];
            nint paramsBuffer = (nint) paramsBufferAllocation;
            UFunctionExporter.CallInitializeFunctionParams(LoadAsset_NativeFunction, paramsBuffer);
            IntPtr InAssetPath_NativePtr = IntPtr.Add(paramsBuffer, LoadAsset_InAssetPath_Offset);
            StringMarshaller.ToNative(InAssetPath_NativePtr,0,assetPath);
            
            UObjectExporter.CallInvokeNativeStaticFunction(NativeClassPtr, LoadAsset_NativeFunction, paramsBuffer, paramsBuffer + LoadAsset_ReturnValue_Offset);
            
            UnrealSharp.CoreUObject.UObject returnValue;
            returnValue = ObjectMarshaller<UnrealSharp.CoreUObject.UObject>.FromNative(IntPtr.Add(paramsBuffer, LoadAsset_ReturnValue_Offset), 0);
            
            StringMarshaller.DestructInstance(InAssetPath_NativePtr, 0);
            return returnValue;
        }
    }
    
    
}