using UnityEngine;

/// <summary>
/// Emission + inverted-hull outline so asteroids are visible in dark space.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class AsteroidVisual : MonoBehaviour
{
    public static Material SharedOutlineMaterial { get; private set; }

    public Color rimColor = new Color(1f, 0.85f, 0.45f);
    public Color emissionColor = new Color(1f, 1f, 1f, 0.3f);
    public float emissionIntensity = 1f;
    public float outlineScale = 1.12f;

    Renderer mainRenderer;
    GameObject outline;
    MaterialPropertyBlock block;
    bool highlighted;

    public void Setup(bool isSpike)
    {
        if (isSpike)
        {
            rimColor = new Color(1f, 0.25f, 0.2f);
            emissionColor = new Color(1f, 0.15f, 0.1f);
        }

        mainRenderer = GetComponentInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        ApplyEmission(emissionIntensity);
        BuildOutline();
    }

    public void SetHighlighted(bool on)
    {
        if (highlighted == on) return;
        highlighted = on;
        ApplyEmission(on ? emissionIntensity * 2.4f : emissionIntensity);
        if (outline != null)
            outline.transform.localScale = Vector3.one * (on ? outlineScale * 1.08f : outlineScale);
    }

    void ApplyEmission(float intensity)
    {
        if (mainRenderer == null) return;

        Color emit = emissionColor * intensity;
        mainRenderer.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", emit);
        block.SetColor("_BaseColor", Color.Lerp(Color.white, rimColor, 1f));
        mainRenderer.SetPropertyBlock(block);

        foreach (var mat in mainRenderer.materials)
        {
            if (mat == null) continue;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emit);
        }
    }

    void BuildOutline()
    {
        var mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        EnsureOutlineMat();
        outline = new GameObject("Outline");
        outline.transform.SetParent(transform, false);
        outline.transform.localScale = Vector3.one * outlineScale;
        outline.layer = gameObject.layer;

        outline.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var mr = outline.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SharedOutlineMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static void EnsureOutlineMat()
    {
        if (SharedOutlineMaterial != null) return;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        SharedOutlineMaterial = new Material(lit) { name = "AsteroidOutlineShared" };
        SharedOutlineMaterial.SetFloat("_Cull", 1f); // front cull = inverted hull
        SharedOutlineMaterial.SetColor("_BaseColor", new Color(1f, 0.82f, 1f));
        SharedOutlineMaterial.EnableKeyword("_EMISSION");
        SharedOutlineMaterial.SetColor("_EmissionColor", new Color(2.5f, 1.6f, 0.4f));
    }

    void OnDestroy()
    {
        if (outline != null) Destroy(outline);
    }
}
