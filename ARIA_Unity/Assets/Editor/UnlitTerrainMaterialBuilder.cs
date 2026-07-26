using UnityEditor;
using UnityEngine;

/// Creates a real, asset-serialized Unlit material for the terrain to use,
/// instead of resolving Shader.Find("Unlit/Texture") at runtime. Nothing
/// else in the project references that shader from a serialized asset, so
/// Unity's WebGL build pipeline can strip it as unused -- meaning
/// Shader.Find would work fine in the Editor (which always has every
/// built-in shader available) but silently return null in the actual
/// deployed build, invisibly falling back to whatever shared lit material
/// happened to be passed instead. Baking the shader reference into a real
/// .mat asset guarantees it survives stripping, the same fix already used
/// for DummyStandardMaterial earlier this session.
public static class UnlitTerrainMaterialBuilder
{
    private const string MaterialPath = "Assets/Resources/TerrainUnlitMaterial.mat";

    public static void BuildMaterial()
    {
        var shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            Debug.LogError("[UnlitTerrainMaterialBuilder] Shader.Find(\"Unlit/Texture\") returned null even in the Editor.");
            EditorApplication.Exit(1);
            return;
        }

        var mat = new Material(shader) { name = "TerrainUnlitMaterial" };
        AssetDatabase.CreateAsset(mat, MaterialPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[UnlitTerrainMaterialBuilder] Created '{MaterialPath}' with shader '{mat.shader.name}'.");
        EditorApplication.Exit(0);
    }
}
