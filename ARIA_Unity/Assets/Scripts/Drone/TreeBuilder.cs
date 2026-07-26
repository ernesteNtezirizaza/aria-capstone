using UnityEngine;

public static class TreeBuilder
{
    // Species visual profiles, indexed to match ARIAConstants.SPECIES_NAMES exactly.
    // Widely separated hues so all 5 species read as visually distinct at a glance
    // (the 3 eucalyptus variants used to share nearly the same blue-green tone).
    private static readonly Color[] CanopyColors = {
        new Color(0.30f, 0.55f, 0.40f),  // 0 Eucalyptus globulus  -- blue-green
        new Color(0.42f, 0.58f, 0.30f),  // 1 Grevillea robusta    -- silvery yellow-green
        new Color(0.45f, 0.55f, 0.25f),  // 2 Eucalyptus maculata  -- olive/yellow-green
        new Color(0.15f, 0.40f, 0.55f),  // 3 Eucalyptus maidenii  -- glaucous blue (real "blue gum" foliage)
        new Color(0.08f, 0.38f, 0.10f),  // 4 Artocarpus heterophyllus -- dense dark green
    };

    // Falling-seed appearance -- lets a seed be recognisable as its species before
    // it ever becomes a sprout, rather than every species dropping an identical marker.
    private static readonly Color[] SeedColors = {
        new Color(0.85f, 0.60f, 0.10f),  // 0 Eucalyptus globulus  -- deep amber
        new Color(0.75f, 0.25f, 0.15f),  // 1 Grevillea robusta    -- reddish-brown
        new Color(0.65f, 0.75f, 0.15f),  // 2 Eucalyptus maculata  -- yellow-green
        new Color(0.35f, 0.50f, 0.65f),  // 3 Eucalyptus maidenii  -- blue-grey
        new Color(0.95f, 0.85f, 0.35f),  // 4 Artocarpus heterophyllus -- pale creamy yellow
    };
    private static readonly float[] SeedScales = { 0.45f, 0.5f, 0.45f, 0.5f, 0.75f }; // jackfruit seed is notably larger

    // Sprout (Dropped/Germinating) marker scale -- a rough preview of the eventual
    // canopy proportions, so early growth stages hint at species too.
    private static readonly float[] SproutScales = { 0.5f, 0.6f, 0.5f, 0.55f, 0.7f };

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

    public static Color GetSeedColor(int species)
    {
        return SeedColors[Mathf.Clamp(species, 0, 4)];
    }

    public static float GetSeedScale(int species)
    {
        return SeedScales[Mathf.Clamp(species, 0, 4)];
    }

    public static float GetSproutScale(int species)
    {
        return SproutScales[Mathf.Clamp(species, 0, 4)];
    }
}
