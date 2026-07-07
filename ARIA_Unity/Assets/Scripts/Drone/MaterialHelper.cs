using UnityEngine;

public static class MaterialHelper
{
    private static Material _defaultMaterial;

    public static Material GetDefaultMaterial()
    {
        if (_defaultMaterial == null)
        {
            _defaultMaterial = Resources.Load<Material>("DummyStandardMaterial");
        }

        return _defaultMaterial != null ? new Material(_defaultMaterial) : null;
    }
}
