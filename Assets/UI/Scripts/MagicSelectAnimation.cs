using UnityEngine;
using DG.Tweening;
public class MagicSelectAnimation : MonoBehaviour
{
    public void PlayMyMagicSelect()
    {
        DOTween.Restart(gameObject, "MagicSelect");
    }
}
