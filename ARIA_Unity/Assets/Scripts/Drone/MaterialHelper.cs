using UnityEngine;

public static class MaterialHelper
{
    private static Material _defaultMaterial;

    public static Material GetDefaultMaterial()
    {
        if (_defaultMaterial == null)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _defaultMaterial = new Material(temp.GetComponent<Renderer>().sharedMaterial);
            Object.Destroy(temp);
        }
        return new Material(_defaultMaterial);
    }
}
