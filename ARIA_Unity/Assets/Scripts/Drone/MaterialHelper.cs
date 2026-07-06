using UnityEngine;

public static class MaterialHelper
{
    private static Material _defaultMaterial;

    public static Material GetDefaultMaterial()
    {
        if (_defaultMaterial == null)
        {
            // Canvas.GetDefaultCanvasMaterial() is built directly into the engine's core C++ binaries.
            // It is completely immune to WebGL stripping and does not rely on string lookups.
            // It provides an unlit shader that perfectly supports color tints.
            _defaultMaterial = Canvas.GetDefaultCanvasMaterial();
            
            if (_defaultMaterial == null) 
            {
                // Absolute worst case fallback to prevent crashes
                return null;
            }
        }
        
        return new Material(_defaultMaterial);
    }
}
