using UnityEditor;
using UnityEngine;

/// One-off asset upgrade: DummyStandardMaterial.mat (used as the base
/// material for every procedural object in the scene -- terrain, trees,
/// seeds, hazard markers) was set to the built-in VertexLit shader, which
/// barely responds to lighting/shadows and reads as flat and plastic-looking
/// regardless of what's underneath it. Standard is Unity's PBR shader --
/// same procedural geometry, but it actually shades against the directional
/// light/shadows/ambient set up in SceneBootstrapper.
public static class MaterialUpgrader
{
    private const string MaterialPath = "Assets/Resources/DummyStandardMaterial.mat";

    public static void UpgradeToStandardShader()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Debug.LogError($"[MaterialUpgrader] Could not load material at '{MaterialPath}'.");
            EditorApplication.Exit(1);
            return;
        }

        var standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError("[MaterialUpgrader] Shader.Find(\"Standard\") returned null -- built-in Standard shader not available.");
            EditorApplication.Exit(1);
            return;
        }

        mat.shader = standard;
        // Matte, natural-material default -- a fresh Standard material
        // defaults to Smoothness 0.5, which reads as wet/shiny plastic
        // under a directional sun. Every procedural object (ground, bark,
        // canopy, hazard spheres) should look dry and non-reflective by
        // default; species-specific colour is still set per-object via
        // mat.color, unaffected by this.
        mat.SetFloat("_Glossiness", 0.1f);
        mat.SetFloat("_Metallic", 0f);

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MaterialUpgrader] '{MaterialPath}' now uses shader '{mat.shader.name}'.");
        EditorApplication.Exit(0);
    }
}
