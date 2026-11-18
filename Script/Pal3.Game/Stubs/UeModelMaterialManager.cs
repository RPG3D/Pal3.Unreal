using Engine.Core.Abstraction;
using Pal3.Core.Primitives;
using Pal3.Game.Data;
using Pal3.Game.Rendering;
using Pal3.Game.Rendering.Material;
using Color = Pal3.Core.Primitives.Color;

namespace Engine.Renderer;

// The shared material managers already pass the model type and texture name.
// Preserve that information on the UE material where the mesh is uploaded.
internal sealed class UeModelMaterialManager(IMaterialManager materialManager) : IMaterialManager
{
    public MaterialShaderType ShaderType => materialManager.ShaderType;

    public IMaterial CreateOpaqueSpriteMaterial() => materialManager.CreateOpaqueSpriteMaterial();

    public IMaterial CreateOpaqueSpriteMaterial(ITexture2D texture) =>
        materialManager.CreateOpaqueSpriteMaterial(texture);

    public IMaterial[] CreateStandardMaterials(RendererType rendererType,
        (string name, ITexture2D texture) mainTexture, (string name, ITexture2D texture) shadowTexture,
        Color tintColor, GameBoxBlendFlag blendFlag)
    {
        IMaterial[] materials = materialManager.CreateStandardMaterials(rendererType, mainTexture, shadowTexture,
            tintColor, blendFlag);
        if (materials == null) return null!;

        bool flipV = rendererType switch
        {
            RendererType.Mv3 => TextureCoordinates.FlipCharacterV(mainTexture.texture, fallback: true),
            RendererType.Msh => TextureCoordinates.FlipCharacterV(mainTexture.texture,
                fallback: mainTexture.name?.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) == true),
            _ => false
        };

        foreach (IMaterial material in materials)
            SetTextureCoordinates(material, rendererType == RendererType.Mv3, flipV);
        return materials;
    }

    public IMaterial CreateWaterMaterial((string name, ITexture2D texture) mainTexture,
        (string name, ITexture2D texture) shadowTexture, float opacity, GameBoxBlendFlag blendFlag)
    {
        var material = materialManager.CreateWaterMaterial(mainTexture, shadowTexture, opacity, blendFlag);
        SetTextureCoordinates(material, false, false);
        return material;
    }

    public IMaterial CreateSkyboxMaterial(ITexture2D rightTex, ITexture2D backTex, ITexture2D leftTex,
        ITexture2D frontTex, ITexture2D upTex, ITexture2D downTex) =>
        materialManager.CreateSkyboxMaterial(rightTex, backTex, leftTex, frontTex, upTex, downTex);

    public void UpdateMaterial(IMaterial material, ITexture2D newMainTexture, GameBoxBlendFlag blendFlag)
    {
        materialManager.UpdateMaterial(material, newMainTexture, blendFlag);
        if (material is UnityMaterial { UsesMv3TextureCoordinates: true } ueMaterial)
        {
            if (newMainTexture != null)
                ueMaterial.FlipMainTextureV = TextureCoordinates.FlipCharacterV(newMainTexture, fallback: true);
            ueMaterial.NotifyMeshBindingChanged();
        }
    }

    public void AllocateMaterialPool() => materialManager.AllocateMaterialPool();
    public void DeallocateMaterialPool() => materialManager.DeallocateMaterialPool();
    public void ReturnToPool(IMaterial[] materials)
    {
        if (materials == null) return;
        foreach (IMaterial material in materials)
            SetTextureCoordinates(material, false, false);
        materialManager.ReturnToPool(materials);
    }

    private static void SetTextureCoordinates(IMaterial material, bool usesMv3, bool flipV)
    {
        if (material is not UnityMaterial ueMaterial) return;
        ueMaterial.UsesMv3TextureCoordinates = usesMv3;
        ueMaterial.FlipMainTextureV = flipV;
    }
}
