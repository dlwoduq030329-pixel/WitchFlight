using System.Collections;
using Fusion;
using UnityEngine.SceneManagement;

// Activate authored training bots before Fusion attaches scene NetworkObjects.
// Player.Awake must cache MagicTestBot before Player.Spawned initializes its stats.
public sealed class BattleNetworkSceneManager : NetworkSceneManagerDefault
{
    protected override IEnumerator OnSceneLoaded(SceneRef sceneRef, Scene scene,
        NetworkLoadSceneParameters sceneParams)
    {
        NetworkGameManager manager = GetComponentInParent<NetworkGameManager>();
        if (manager != null && manager.IsTrainingScene(scene))
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (MagicTestBot bot in root.GetComponentsInChildren<MagicTestBot>(true))
                    bot.gameObject.SetActive(true);
        }
        // No cloning or participant registration: bots retain their scene positions/configuration.
        return base.OnSceneLoaded(sceneRef, scene, sceneParams);
    }
}
