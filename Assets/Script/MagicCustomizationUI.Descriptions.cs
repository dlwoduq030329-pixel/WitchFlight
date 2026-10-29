using DG.Tweening;
using UnityEngine;

public sealed partial class MagicCustomizationUI
{
    [Header("Separate hat / broom description panels")]
    [SerializeField] private EquipmentDescriptionUI hatDescriptionUI;
    [SerializeField] private EquipmentDescriptionUI broomDescriptionUI;
    private bool descriptionPanelsInitialized;
    private bool descriptionAnimationPending;
    private DOTweenAnimation[] descriptionTweens = System.Array.Empty<DOTweenAnimation>();

    private void InitializeDescriptionPanels()
    {
        if (descriptionPanelsInitialized) return;
        descriptionPanelsInitialized = true;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Explanation_Hat" && hatDescriptionUI == null)
                hatDescriptionUI = child.GetComponent<EquipmentDescriptionUI>();
            if (child.name == "Explanation_Broom" && broomDescriptionUI == null)
                broomDescriptionUI = child.GetComponent<EquipmentDescriptionUI>();
        }
        var tweens = new System.Collections.Generic.List<DOTweenAnimation>();
        foreach (GameObject panel in new[] { selectionPanel,
            hatDescriptionUI != null ? hatDescriptionUI.gameObject : null,
            broomDescriptionUI != null ? broomDescriptionUI.gameObject : null })
        {
            if (panel == null) continue;
            foreach (DOTweenAnimation animation in panel.GetComponentsInChildren<DOTweenAnimation>(true))
            {
                if (tweens.Contains(animation)) continue;
                tweens.Add(animation);
                animation.autoPlay = false;
                animation.autoKill = false;
                if (animation.tween != null && animation.tween.IsActive())
                    animation.tween.SetAutoKill(false).Pause();
            }
        }
        descriptionTweens = tweens.ToArray();
    }

    private GameObject SelectedDescriptionPanel => SelectedItemCategory switch
    {
        EquipmentCategory.Hat => hatDescriptionUI != null ? hatDescriptionUI.gameObject : null,
        EquipmentCategory.Broom => broomDescriptionUI != null ? broomDescriptionUI.gameObject : null,
        _ => selectionPanel
    };

    private void RefreshDescriptionPanelVisibility()
    {
        GameObject selected = HasSelectedItem && selectionDetailsOpen ? SelectedDescriptionPanel : null;
        SetVisible(selectionPanel, selected != null && selected == selectionPanel);
        if (hatDescriptionUI != null) SetVisible(hatDescriptionUI.gameObject, selected == hatDescriptionUI.gameObject);
        if (broomDescriptionUI != null) SetVisible(broomDescriptionUI.gameObject, selected == broomDescriptionUI.gameObject);
        foreach (DOTweenAnimation animation in descriptionTweens)
            if (animation != null && !animation.gameObject.activeInHierarchy &&
                animation.tween != null && animation.tween.IsActive()) animation.tween.Pause();
    }

    private void PlayPendingDescriptionAnimation()
    {
        if (!descriptionAnimationPending) return;
        descriptionAnimationPending = false;
        GameObject panel = SelectedDescriptionPanel;
        if (!selectionDetailsOpen || panel == null || !panel.activeInHierarchy) return;
        // Run after all Button.OnClick callbacks so old SetActive actions cannot show another category.
        foreach (DOTweenAnimation animation in descriptionTweens)
        {
            if (animation == null || !animation.transform.IsChildOf(panel.transform) ||
                !animation.isActiveAndEnabled || !animation.isActive || !animation.isValid) continue;
            animation.CreateTween(false, false);
            if (animation.tween != null && animation.tween.IsActive())
                animation.tween.SetAutoKill(false).SetUpdate(true).Restart();
        }
    }
}
