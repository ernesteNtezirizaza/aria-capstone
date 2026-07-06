using UnityEngine;

public static class MaterialHelper
{
    private static Material _defaultMaterial;

    public static Material GetDefaultMaterial()
    {
        if (_defaultMaterial == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Mobile/Diffuse");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("UI/Default");

            if (shader != null)
            {
                _defaultMaterial = new Material(shader);
            }
            else
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (temp.GetComponent<Renderer>().sharedMaterial != null && temp.GetComponent<Renderer>().sharedMaterial.shader != null)
                {
                    _defaultMaterial = new Material(temp.GetComponent<Renderer>().sharedMaterial);
                }
                Object.Destroy(temp);
            }
        }
        return new Material(_defaultMaterial);
    }
}
