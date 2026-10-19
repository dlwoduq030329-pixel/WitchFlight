using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class BattleHud
{
    // Assign this to an authored Battle-scene exit button; no persistent-manager reference needed.
    public void ExitTraining() => NetworkGameManager.Instance?.ExitTraining();

    private void EnsureTrainingMenu()
    {
        if (menuPanel != null) return;
        menuPanel = new GameObject("Training menu (generated)", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        menuPanel.transform.SetParent(transform, false);
        Canvas canvas = menuPanel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        CanvasScaler scaler = menuPanel.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(menuPanel.transform, false);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(460, 290);
        panel.GetComponent<Image>().color = new Color(.025f, .06f, .1f, .97f);
        TrainingMenuLabel(panel.transform, "훈련장 · 제한 시간 없음", new Vector2(0, 90), new Vector2(420, 48));
        TrainingMenuButton(panel.transform, "계속 연습", new Vector2(0, 10), Resume);
        TrainingMenuButton(panel.transform, "메인 화면으로", new Vector2(0, -75), ExitTraining);
        menuPanel.SetActive(false);
    }

    private TMP_Text TrainingMenuLabel(Transform parent, string text, Vector2 position, Vector2 size)
    {
        var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);
        TMP_Text view = label.GetComponent<TMP_Text>();
        view.rectTransform.anchoredPosition = position;
        view.rectTransform.sizeDelta = size;
        if (feedbackFont != null) view.font = feedbackFont;
        view.text = text;
        view.fontSize = 28;
        view.alignment = TextAlignmentOptions.Center;
        view.raycastTarget = false;
        view.color = Color.white;
        return view;
    }

    private void TrainingMenuButton(Transform parent, string text, Vector2 position, UnityAction action)
    {
        var buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(380, 64);
        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(.04f, .35f, .38f, 1);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        TrainingMenuLabel(buttonObject.transform, text, Vector2.zero, rect.sizeDelta);
    }
}
