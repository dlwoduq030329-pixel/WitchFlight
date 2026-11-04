using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class PlayerMoneyLabel : MonoBehaviour
{
    private TMP_Text label;

    private void OnEnable()
    {
        label = GetComponent<TMP_Text>();
        DataConfig.MoneyChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => DataConfig.MoneyChanged -= Refresh;
    private void Refresh()
    {
        if (label != null) label.text = DataConfig.money.ToString("N0", CultureInfo.InvariantCulture);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Install(SceneManager.GetSceneAt(i));
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);
    private static void Install(Scene scene)
    {
        if (!scene.isLoaded || (!scene.name.Equals("Main", StringComparison.OrdinalIgnoreCase) &&
            !scene.name.Equals("MainScene", StringComparison.OrdinalIgnoreCase))) return;
        // One scan per scene load, including unopened shop/settings panels. No frame polling.
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name.Equals("MoneyText", StringComparison.OrdinalIgnoreCase) &&
                    text.GetComponent<PlayerMoneyLabel>() == null)
                    text.gameObject.AddComponent<PlayerMoneyLabel>();
    }
}
