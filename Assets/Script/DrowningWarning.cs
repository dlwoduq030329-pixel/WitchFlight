using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DrowningWarning : MonoBehaviour
{
    private Player owner;
    private RawImage vignette;
    private Texture2D texture;
    private BattleAudio.Loop bubbles;
    private bool sounding, surfacedLocally;
    private void Awake() => owner = GetComponent<Player>();
    private void Update()
    {
        bool active = owner != null && owner == Player.LocalPlayer && owner.Object != null && owner.Object.IsValid &&
            owner.IsAlive && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (!active) { Hide(); surfacedLocally = true; return; }
        // Suppress immediately on local surface exit, even before the server snapshot arrives.
        bool below = owner.IsBelowWaterSurface();
        if (!below) surfacedLocally = true;
        if (owner.SubmergedSeconds < Player.WaterWarningSeconds) surfacedLocally = false;
        if (!below || surfacedLocally || owner.SubmergedSeconds < Player.WaterWarningSeconds) { Hide(); return; }
        EnsureVignette();
        float urgency = Mathf.InverseLerp(Player.WaterWarningSeconds,Player.WaterDeathSeconds,owner.SubmergedSeconds);
        float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(5f,9f,urgency));
        vignette.color = new Color(1f,1f,1f,Mathf.Lerp(.2f,.85f,pulse) * Mathf.Lerp(.65f,1f,urgency));
        vignette.enabled = true;
        if (!sounding) { bubbles = BattleAudio.StartLoop(BattleSound.DrowningWarning,owner.transform,true); sounding = true; }
    }
    private void EnsureVignette()
    {
        if (vignette != null) return;
        var canvasObject = new GameObject("Underwater warning",typeof(RectTransform),typeof(Canvas));
        canvasObject.transform.SetParent(transform,false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 6500;
        var imageObject = new GameObject("Pulsing red edge",typeof(RectTransform),typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform,false);
        vignette = imageObject.GetComponent<RawImage>(); vignette.raycastTarget = false;
        var rect = vignette.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        const int size = 256;
        texture = new Texture2D(size,size,TextureFormat.RGBA32,false) { name="Drowning edge gradient",wrapMode=TextureWrapMode.Clamp };
        var pixels = new Color[size*size];
        for (int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            float u = Mathf.Abs(x/(float)(size-1)*2f-1f), v = Mathf.Abs(y/(float)(size-1)*2f-1f);
            float edge = Mathf.Pow(Mathf.Max(u,v),3.5f);
            pixels[y*size+x] = new Color(.85f,.015f,.025f,Mathf.SmoothStep(0f,1f,edge));
        }
        texture.SetPixels(pixels); texture.Apply(false,true); vignette.texture = texture;
    }
    private void Hide()
    {
        if (vignette != null) vignette.enabled = false;
        bubbles.Stop(); sounding = false;
    }
    private void OnDisable() => Hide();
    private void OnDestroy() { Hide(); if (texture != null) Destroy(texture); }
}
