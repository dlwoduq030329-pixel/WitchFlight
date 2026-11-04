using UnityEngine;
using DG.Tweening;
public class MagicSelectAnimation : MonoBehaviour
{
    public ParticleSystem Vfx;
    public void PlayMyMagicSelect()
    {
        Vfx.Play();
    }
}
