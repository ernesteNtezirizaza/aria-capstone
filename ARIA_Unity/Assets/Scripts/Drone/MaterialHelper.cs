using UnityEngine;

public static class MaterialHelper
{
    private static Material _defaultMaterial;

    public static Material GetDefaultMaterial()
    {
        if (_defaultMaterial == null)
        {
            // Loaded from Assets/Resources so it always ships with the WebGL build,
            // and its shader is explicitly listed in GraphicsSettings.m_AlwaysIncludedShaders,
            // so it can never be stripped regardless of what else is in the scene.
            _defaultMaterial = Resources.Load<Material>("DummyStandardMaterial");
        }

        return _defaultMaterial != null ? new Material(_defaultMaterial) : null;
    }
}
