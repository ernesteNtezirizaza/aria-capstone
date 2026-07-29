using UnityEngine;

namespace ARIA.Drone
{
    /// Builds a real tree instance (Assets/Resources/RealTree0.prefab or
    /// RealTree1.prefab, imported from the "More Realistic Trees Free!"
    /// glTF -- see Assets/Models/RealTrees/license.txt for the required
    /// CC-BY-4.0 attribution) instead of TreeBuilder's procedural
    /// primitive-shape trees. Only 2 real mesh variants exist, but the
    /// system has 5 species that should still read as visually distinct,
    /// so each species gets its own combination of (which of the 2
    /// meshes, overall scale, colour tint) rather than all 5 looking
    /// identical.
    public static class RealTreeBuilder
    {
        private static readonly string[] PrefabNames = { "RealTree0", "RealTree1" };

        /* Mesh choice, scale, and tint per species -- same differentiation
           intent as TreeBuilder's per-species trunk/canopy profiles, just
           expressed as variations on the 2 real meshes instead of separate
           procedural geometry per species. */
        private static readonly int[]   MeshIndex   = { 0, 1, 0, 1, 0 };
        private static readonly float[] ScaleFactor = { 1.00f, 0.85f, 0.95f, 1.05f, 1.25f };
        private static readonly Color[] Tint =
        {
            new Color(0.55f, 0.85f, 0.75f), // 0 Eucalyptus globulus  -- blue-green
            new Color(0.85f, 0.95f, 0.55f), // 1 Grevillea robusta    -- yellow-green
            new Color(0.85f, 0.95f, 0.65f), // 2 Eucalyptus maculata  -- olive
            new Color(0.65f, 0.80f, 0.95f), // 3 Eucalyptus maidenii  -- blue-grey (glaucous)
            new Color(0.55f, 0.85f, 0.55f), // 4 Artocarpus heterophyllus -- dense dark green
        };

        private static GameObject[] _prefabCache = new GameObject[2];

        public static GameObject Build(int speciesId, bool existing = false)
        {
            speciesId = Mathf.Clamp(speciesId, 0, 4);
            int meshIdx = MeshIndex[speciesId];

            var prefab = _prefabCache[meshIdx];
            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>(PrefabNames[meshIdx]);
                _prefabCache[meshIdx] = prefab;
            }
            if (prefab == null)
            {
                Debug.LogWarning($"[RealTreeBuilder] {PrefabNames[meshIdx]} not found in Resources -- no tree spawned.");
                return null;
            }

            var tree = Object.Instantiate(prefab);
            tree.name = "RealTree_" + GetName(speciesId);
            tree.transform.localScale = Vector3.one * ScaleFactor[speciesId];
            tree.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Color tint = existing ? Tint[speciesId] * 0.6f : Tint[speciesId];
            foreach (var rend in tree.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in rend.materials)
                {
                    /* Confirmed live: this material's shader is glTFast's
                       own 'glTF/PbrMetallicRoughness', which has neither
                       "_Color" nor "_BaseColor" -- that's why two attempts
                       at guessing a specific property name both silently
                       no-opped. Rather than guess a third name, enumerate
                       the shader's actual declared properties at runtime
                       and set whichever one is really a Color, regardless
                       of what it's called. */
                    var shader = mat.shader;
                    int propCount = shader.GetPropertyCount();
                    for (int i = 0; i < propCount; i++)
                    {
                        if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                        {
                            mat.SetColor(shader.GetPropertyName(i), tint);
                        }
                    }
                }
            }

            return tree;
        }

        public static string GetName(int species)
        {
            string[] names = {
                "Eucalyptus_globulus", "Grevillea_robusta", "Eucalyptus_maculata",
                "Eucalyptus_maidenii", "Artocarpus_heterophyllus"
            };
            return names[Mathf.Clamp(species, 0, 4)];
        }
    }
}
