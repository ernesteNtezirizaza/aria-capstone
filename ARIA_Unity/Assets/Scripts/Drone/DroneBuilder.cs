using UnityEngine;

public static class DroneBuilder
{
    public static GameObject Build()
    {
        GameObject root = new GameObject("ARIA_Drone");

        // ── Body ─────────────────────────────────────────────────────────────
        GameObject body = MakeBox("Body", root, Vector3.zero,
            new Vector3(2.0f, 0.4f, 2.0f), new Color(0.15f, 0.15f, 0.15f));

        // Top dome (sensor)
        GameObject dome = MakeSphere("Dome", root, new Vector3(0, 0.35f, 0),
            new Vector3(0.7f, 0.4f, 0.7f), new Color(0.05f, 0.05f, 0.05f));

        // ── 4 Arms ───────────────────────────────────────────────────────────
        float armLen = 1.8f;
        MakeBox("Arm_FL", root, new Vector3(-armLen * 0.5f, 0,  armLen * 0.5f),
            new Vector3(armLen, 0.15f, 0.15f), new Color(0.2f, 0.2f, 0.2f));
        MakeBox("Arm_FR", root, new Vector3( armLen * 0.5f, 0,  armLen * 0.5f),
            new Vector3(armLen, 0.15f, 0.15f), new Color(0.2f, 0.2f, 0.2f));
        MakeBox("Arm_BL", root, new Vector3(-armLen * 0.5f, 0, -armLen * 0.5f),
            new Vector3(armLen, 0.15f, 0.15f), new Color(0.2f, 0.2f, 0.2f));
        MakeBox("Arm_BR", root, new Vector3( armLen * 0.5f, 0, -armLen * 0.5f),
            new Vector3(armLen, 0.15f, 0.15f), new Color(0.2f, 0.2f, 0.2f));

        // ── 4 Rotors ─────────────────────────────────────────────────────────
        Vector3[] rotorPos = {
            new Vector3(-armLen, 0.2f,  armLen),
            new Vector3( armLen, 0.2f,  armLen),
            new Vector3(-armLen, 0.2f, -armLen),
            new Vector3( armLen, 0.2f, -armLen),
        };
        for (int i = 0; i < 4; i++)
        {
            // Motor hub
            MakeCylinder("Motor_" + i, root, rotorPos[i],
                new Vector3(0.3f, 0.3f, 0.3f), new Color(0.3f, 0.3f, 0.3f));

            // Rotor blade (flat disc)
            GameObject rotor = MakeCylinder("Rotor_" + i, root,
                rotorPos[i] + Vector3.up * 0.2f,
                new Vector3(1.4f, 0.04f, 1.4f), new Color(0.6f, 0.6f, 0.6f, 0.7f));

            // Add spinning component
            rotor.AddComponent<RotorSpin>().speed = (i % 2 == 0) ? 800f : -800f;
        }

        // ── Landing Gear ─────────────────────────────────────────────────────
        MakeBox("Gear_L", root, new Vector3(-0.9f, -0.4f, 0),
            new Vector3(0.1f, 0.5f, 1.8f), new Color(0.25f, 0.25f, 0.25f));
        MakeBox("Gear_R", root, new Vector3( 0.9f, -0.4f, 0),
            new Vector3(0.1f, 0.5f, 1.8f), new Color(0.25f, 0.25f, 0.25f));

        // ── Seed Bag ─────────────────────────────────────────────────────────
        // Bag body — green canvas bag hanging below
        GameObject bag = MakeBox("SeedBag", root, new Vector3(0, -1.0f, 0),
            new Vector3(0.8f, 0.9f, 0.8f), new Color(0.2f, 0.55f, 0.15f));

        // Bag strap
        MakeBox("BagStrap", root, new Vector3(0, -0.55f, 0),
            new Vector3(0.08f, 0.4f, 0.08f), new Color(0.4f, 0.3f, 0.1f));

        // Seed dispenser nozzle at bottom of bag
        MakeCylinder("Nozzle", root, new Vector3(0, -1.5f, 0),
            new Vector3(0.2f, 0.25f, 0.2f), new Color(0.5f, 0.4f, 0.1f));

        // ── Navigation Lights ─────────────────────────────────────────────────
        // Front = green, Back = red (aviation standard)
        GameObject lightF = MakeSphere("Light_Front", root,
            new Vector3(0, 0.1f, 1.1f), new Vector3(0.2f, 0.2f, 0.2f),
            Color.green);
        lightF.AddComponent<BlinkLight>().rate = 1.5f;

        GameObject lightB = MakeSphere("Light_Back", root,
            new Vector3(0, 0.1f, -1.1f), new Vector3(0.2f, 0.2f, 0.2f),
            Color.red);
        lightB.AddComponent<BlinkLight>().rate = 1.5f;

        // ── Camera (optional FPV look) ────────────────────────────────────────
        MakeBox("Camera", root, new Vector3(0, -0.25f, 1.05f),
            new Vector3(0.3f, 0.25f, 0.2f), new Color(0.1f, 0.1f, 0.1f));

        return root;
    }

    // ── Primitive Helpers ─────────────────────────────────────────────────────
    static GameObject MakeBox(string name, GameObject parent, Vector3 pos,
                               Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    static GameObject MakeSphere(string name, GameObject parent, Vector3 pos,
                                  Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    static GameObject MakeCylinder(string name, GameObject parent, Vector3 pos,
                                    Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        SetMat(go, col);
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    static void SetMat(GameObject go, Color col)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = col;
        if (col.a < 1f) {
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
        }
        rend.material = mat;
    }
}

// ── Rotor Spin Component ──────────────────────────────────────────────────────
public class RotorSpin : MonoBehaviour
{
    public float speed = 800f;
    void Update() {
        transform.Rotate(Vector3.up, speed * Time.deltaTime);
    }
}

// ── Blink Light Component ─────────────────────────────────────────────────────
public class BlinkLight : MonoBehaviour
{
    public float rate = 1.5f;
    private Renderer rend;
    void Start() { rend = GetComponent<Renderer>(); }
    void Update() {
        if (rend == null) return;
        bool on = (Time.time * rate) % 1f > 0.5f;
        rend.enabled = on;
    }
}
