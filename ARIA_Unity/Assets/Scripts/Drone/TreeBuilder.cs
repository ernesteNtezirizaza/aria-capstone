using UnityEngine;

public static class TreeBuilder
{
    // Species visual profiles, indexed to match ARIAConstants.SPECIES_NAMES exactly.
    private static readonly Color[] TrunkColors = {
        new Color(0.55f, 0.50f, 0.42f),  // 0 Eucalyptus globulus  -- pale grey-brown bark
        new Color(0.42f, 0.30f, 0.16f),  // 1 Grevillea robusta    -- warm mid brown
        new Color(0.50f, 0.44f, 0.38f),  // 2 Eucalyptus maculata  -- mottled grey (base tone)
        new Color(0.58f, 0.54f, 0.48f),  // 3 Eucalyptus maidenii  -- pale blue-grey bark
        new Color(0.32f, 0.20f, 0.10f),  // 4 Artocarpus heterophyllus -- dark thick brown bark
    };
    private static readonly Color[] CanopyColors = {
        new Color(0.28f, 0.50f, 0.42f),  // 0 Eucalyptus globulus  -- blue-green
        new Color(0.42f, 0.58f, 0.30f),  // 1 Grevillea robusta    -- silvery yellow-green
        new Color(0.25f, 0.48f, 0.38f),  // 2 Eucalyptus maculata  -- blue-green (slightly darker)
        new Color(0.22f, 0.46f, 0.40f),  // 3 Eucalyptus maidenii  -- deep blue-green
        new Color(0.08f, 0.38f, 0.10f),  // 4 Artocarpus heterophyllus -- dense dark green
    };
    private static readonly float[] TrunkWidths   = { 0.16f, 0.17f, 0.16f, 0.15f, 0.26f };
    private static readonly float[] CanopyWidths  = { 1.3f,  2.0f,  1.35f, 1.15f, 2.6f  };
    private static readonly float[] CanopyHeights = { 2.6f,  1.7f,  2.5f,  2.9f,  1.7f  };

    public static GameObject Build(int species, float height, bool existing = false)
    {
        species = Mathf.Clamp(species, 0, 4);
        GameObject root = new GameObject("Tree_" + GetName(species));

        float sizeJitter   = Random.Range(0.97f, 1.03f);
        float rotationY    = Random.Range(0f, 360f);
        float leanDeg      = Random.Range(-1f, 1f);
        root.transform.rotation = Quaternion.Euler(leanDeg, rotationY, leanDeg * 0.6f);

        float trunkH = height * 0.55f * sizeJitter;
        float trunkW = TrunkWidths[species] * (height / 5f) * sizeJitter;

        // ── Trunk -- slight taper via two stacked cylinders instead of ──
        // a single uniform-width cylinder, for a more natural trunk.
        GameObject trunkBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunkBase.name = "TrunkBase";
        trunkBase.transform.SetParent(root.transform, false);
        trunkBase.transform.localPosition = new Vector3(0, trunkH * 0.28f, 0);
        trunkBase.transform.localScale    = new Vector3(trunkW * 1.15f, trunkH * 0.28f, trunkW * 1.15f);
        SetMat(trunkBase, TrunkColors[species]);
        Object.Destroy(trunkBase.GetComponent<Collider>());

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(root.transform, false);
        trunk.transform.localPosition = new Vector3(0, trunkH * 0.28f * 2f + trunkH * 0.22f, 0);
        trunk.transform.localScale    = new Vector3(trunkW * 0.85f, trunkH * 0.22f, trunkW * 0.85f);
        SetMat(trunk, TrunkColors[species]);
        Object.Destroy(trunk.GetComponent<Collider>());

        // Species 2 (Eucalyptus maculata) gets mottled bark patches --
        // its real-world defining feature ("maculata" = spotted).
        if (species == 2)
        {
            for (int i = 0; i < 4; i++)
            {
                float spotY = Random.Range(0.15f, 0.85f) * trunkH;
                float spotAngle = Random.Range(0f, 360f);
                var spot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spot.name = "BarkSpot";
                spot.transform.SetParent(root.transform, false);
                Object.Destroy(spot.GetComponent<Collider>());
                float radius = trunkW * 1.05f;
                spot.transform.localPosition = new Vector3(
                    Mathf.Cos(spotAngle * Mathf.Deg2Rad) * radius, spotY,
                    Mathf.Sin(spotAngle * Mathf.Deg2Rad) * radius);
                spot.transform.localScale = Vector3.one * trunkW * 0.5f;
                SetMat(spot, Color.Lerp(TrunkColors[species], new Color(0.75f, 0.7f, 0.6f), 0.6f));
            }
        }

        // ── Mid branch layer ─────────────────────────────────────────────────
        if (height > 3f && species != 1) // Grevillea's feathery crown starts lower, no separate branch layer
        {
            float branchY = trunkH * 0.75f;
            Color branchCol = TrunkColors[species] * 1.1f;
            AddBranch(root, new Vector3( trunkW * 3,  branchY,  trunkW * 3), 30f,  trunkW * 0.55f, branchCol);
            AddBranch(root, new Vector3(-trunkW * 3,  branchY, -trunkW * 3), 30f,  trunkW * 0.55f, branchCol);
            AddBranch(root, new Vector3( trunkW * 3,  branchY, -trunkW * 3), -30f, trunkW * 0.55f, branchCol);
            AddBranch(root, new Vector3(-trunkW * 3,  branchY,  trunkW * 3), -30f, trunkW * 0.55f, branchCol);
        }

        // ── Canopy ───────────────────────────────────────────────────────────
        float canopyY  = trunkH + CanopyHeights[species] * 0.4f;
        float canopyW  = CanopyWidths[species]  * (height / 5f) * sizeJitter;
        float canopyH  = CanopyHeights[species] * (height / 5f) * sizeJitter;
        Color canopy   = existing
            ? new Color(0.05f, 0.30f, 0.05f)   // existing forest -- darker
            : CanopyColors[species];
        // Small per-instance colour variance so a stand of the same
        // species isn't perfectly uniform.
        canopy = Color.Lerp(canopy, canopy * Random.Range(0.85f, 1.12f), 1f);

        switch (species)
        {
            case 1:
                // Grevillea robusta -- feathery, fern-like pyramidal
                // crown: several progressively narrower tiers.
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 4f;
                    float ly = canopyY - canopyH * 0.35f + t * canopyH * 0.85f;
                    float lw = canopyW * (1f - t * 0.65f);
                    AddCanopyLayer(root, new Vector3(0, ly, 0),
                        new Vector3(lw, canopyH * 0.16f, lw), canopy);
                }
                break;

            case 4:
                // Artocarpus heterophyllus (jackfruit) -- broad, dense,
                // rounded crown built from several overlapping spheres
                // for a fuller, heavier canopy than the eucalyptus species.
                AddCanopySphere(root, new Vector3(0, canopyY, 0),
                    new Vector3(canopyW, canopyH, canopyW), canopy);
                AddCanopySphere(root, new Vector3(canopyW * 0.35f, canopyY + canopyH * 0.15f, canopyW * 0.2f),
                    new Vector3(canopyW * 0.75f, canopyH * 0.8f, canopyW * 0.75f), canopy * 0.95f);
                AddCanopySphere(root, new Vector3(-canopyW * 0.3f, canopyY - canopyH * 0.1f, -canopyW * 0.25f),
                    new Vector3(canopyW * 0.7f, canopyH * 0.7f, canopyW * 0.7f), canopy * 0.9f);
                break;

            case 0: case 2: case 3:
                // The three eucalyptus species -- tall, narrow,
                // slightly wind-swept crown, each already
                // differentiated by trunk colour/bark (species 2) and
                // proportions (widths/heights above).
                AddCanopyLayer(root, new Vector3(canopyW * 0.08f, canopyY, 0),
                    new Vector3(canopyW * 0.55f, canopyH, canopyW * 0.55f), canopy);
                AddCanopySphere(root, new Vector3(canopyW * 0.15f, canopyY + canopyH * 0.42f, 0),
                    new Vector3(canopyW * 0.5f, canopyH * 0.35f, canopyW * 0.5f), canopy * 1.05f);
                break;
        }

        return root;
    }

    static void AddBranch(GameObject parent, Vector3 pos, float tiltZ, float w, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Branch";
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = new Vector3(w, w * 2f, w);
        go.transform.localRotation = Quaternion.Euler(0, 0, tiltZ);
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
    }

    static void AddCanopyLayer(GameObject parent, Vector3 pos, Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Canopy";
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
    }

    static void AddCanopySphere(GameObject parent, Vector3 pos, Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Canopy";
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
    }

    static void SetMat(GameObject go, Color col)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = col;
        rend.material = mat;
    }

    public static string GetName(int species)
    {
        string[] names = {
            "Eucalyptus_globulus", "Grevillea_robusta", "Eucalyptus_maculata",
            "Eucalyptus_maidenii", "Artocarpus_heterophyllus"
        };
        return names[Mathf.Clamp(species, 0, 4)];
    }

    public static Color GetCanopyColor(int species)
    {
        return CanopyColors[Mathf.Clamp(species, 0, 4)];
    }
}
