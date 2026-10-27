using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Added only to attackable summons, never to lobby/VS/broom companions.
[DefaultExecutionOrder(350)]
public sealed class CatSummonFeedback : MonoBehaviour
{
    private CatAvatar avatar;
    private MagicProjectile source;
    private Outline outline;
    private GameObject haloRoot, healthRoot;
    private Canvas haloCanvas, healthCanvas;
    private CombatFeedbackGraphic glow;
    private Image fill;
    private TMP_Text nameLabel;
    private Material nameMaterial;
    private float checkAt;
    private bool sight;
    private Color lastColor;

    public void Initialize(CatAvatar cat, MagicProjectile projectile)
    {
        avatar = cat; source = projectile;
        outline = cat.Cat.GetComponent<Outline>();
        if (outline == null) outline = cat.Cat.gameObject.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.enabled = false;
        haloRoot = new GameObject("Summoned cat team halo", typeof(RectTransform), typeof(Canvas));
        haloRoot.transform.SetParent(transform, false);
        haloCanvas = haloRoot.GetComponent<Canvas>(); haloCanvas.renderMode = RenderMode.WorldSpace;
        var halo = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(CombatFeedbackGraphic));
        halo.transform.SetParent(haloRoot.transform, false);
        glow = halo.GetComponent<CombatFeedbackGraphic>(); glow.Glow = true; glow.raycastTarget = false;
        healthRoot = new GameObject("Summoned cat health", typeof(RectTransform), typeof(Canvas));
        healthRoot.transform.SetParent(transform, false);
        healthCanvas = healthRoot.GetComponent<Canvas>(); healthCanvas.renderMode = RenderMode.WorldSpace;
        var background = MakeImage("Background", healthRoot.transform, new Color(0, 0, 0, .85f));
        fill = MakeImage("Health", background.transform, Color.white);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = new Vector2(.003f, .003f); fill.rectTransform.offsetMax = new Vector2(-.003f, -.003f);
        var label = new GameObject("Cat name", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(healthRoot.transform, false);
        nameLabel = label.GetComponent<TextMeshProUGUI>();
        var font = FindFirstObjectByType<BattleHud>()?.FeedbackFont;
        if (font != null) nameLabel.font = font;
        nameLabel.fontSize = 18;
        nameLabel.alignment = TextAlignmentOptions.Center;
        nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        nameLabel.richText = false; nameLabel.raycastTarget = false;
        nameLabel.rectTransform.localScale = Vector3.one * .01f;
        nameLabel.rectTransform.sizeDelta = new Vector2(180, 28);
        nameLabel.rectTransform.pivot = new Vector2(.5f, 0);
        nameLabel.rectTransform.anchoredPosition = new Vector2(0, .045f);
        if (nameLabel.fontSharedMaterial != null)
        {
            nameMaterial = new Material(nameLabel.fontSharedMaterial);
            nameMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            nameMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .2f);
            nameMaterial.EnableKeyword("OUTLINE_ON");
            nameLabel.fontSharedMaterial = nameMaterial;
            nameLabel.extraPadding = true; nameLabel.UpdateMeshPadding();
        }
        BattleWorldUI.ApplyLayer(haloRoot); BattleWorldUI.ApplyLayer(healthRoot);
        haloRoot.SetActive(false); healthRoot.SetActive(false);
    }

    private static Image MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        return image;
    }

    private void LateUpdate()
    {
        var local = Player.LocalPlayer;
        var camera = Camera.main;
        bool valid = avatar != null && source != null && source.IsLiveSummon && local != null &&
            local.Object != null && local.Object.IsValid && local.Runner == source.Runner && camera != null;
        if (!valid) { Hide(); return; }
        var table = CatCompanionTable.Default;
        bool enemy = source.ShooterTeam != local.TeamIndex;
        bool concealed = enemy && BattleFlag.Instance != null && BattleFlag.Instance.ContainsSmoke(avatar.transform.position);
        avatar.SetVisible(!concealed);
        if (concealed) { Hide(); return; }
        Color color = enemy ? table.enemyColor : table.allyColor;
        if (lastColor != color) { outline.OutlineColor = color; lastColor = color; }
        outline.OutlineWidth = table.summonOutlineWidth;
        if (!outline.enabled) outline.enabled = true;
        Vector3 center = avatar.transform.position + avatar.transform.up * (table.catHeight * .5f);
        haloCanvas.worldCamera = healthCanvas.worldCamera = camera;
        haloRoot.transform.SetPositionAndRotation(center + camera.transform.forward * .28f, camera.transform.rotation);
        glow.rectTransform.sizeDelta = Vector2.one * table.summonHaloSize;
        color.a = table.summonHaloAlpha; glow.color = color;
        haloRoot.SetActive(true);
        if (Time.unscaledTime >= checkAt)
        { sight = MagicProjectile.HasSummonSight(camera.transform.position, center); checkAt = Time.unscaledTime + .1f; }
        var viewport = camera.WorldToViewportPoint(center);
        bool showLabel = sight && viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
        healthRoot.SetActive(showLabel);
        if (!showLabel) return;
        fill.transform.parent.gameObject.SetActive(true);
        string catName = table.GetCatName(avatar.BodyIndex);
        if (nameLabel.text != catName) nameLabel.text = catName;
        healthRoot.transform.SetPositionAndRotation(center + Vector3.up * (table.catHeight * .5f + table.summonHealthBarOffset), camera.transform.rotation);
        // Keep small summons readable at distance without changing their world hit sphere.
        float pixelWorld = camera.orthographic ? 2 * camera.orthographicSize : 2 * viewport.z * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
        float scale = Mathf.Max(1, pixelWorld * 5 / (Mathf.Max(1, camera.pixelHeight) * Mathf.Max(.01f, table.summonHealthBarSize.y)));
        healthRoot.transform.localScale = Vector3.one * scale;
        ((RectTransform)fill.transform.parent).sizeDelta = table.summonHealthBarSize;
        fill.color = enemy ? table.enemyColor : table.allyColor;
        fill.rectTransform.anchorMax = new Vector2(source.SummonHealthFraction, 1);
    }

    private void Hide()
    {
        if (outline != null && outline.enabled) outline.enabled = false;
        if (haloRoot != null) haloRoot.SetActive(false);
        if (healthRoot != null) healthRoot.SetActive(false);
    }
    private void OnDisable() => Hide();
    private void OnDestroy() { if (nameMaterial != null) Destroy(nameMaterial); }
}
