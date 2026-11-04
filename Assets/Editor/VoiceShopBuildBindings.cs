using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

// Rebuild on every build, including inactive shops; new products require only SelectVoice(ID).
public sealed class VoiceShopBuildBindings : IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var shop in root.GetComponentsInChildren<VoiceShopUI>(true))
                shop.RebuildProductBindings();
    }
}
