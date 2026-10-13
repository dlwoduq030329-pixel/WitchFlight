using UnityEngine;
using DG.Tweening;
public class MagicSelectAnimation : MonoBehaviour
{
    public GameObject Vfx;
    public GameObject other1;
    public GameObject other2;
    public GameObject other3;
    public void PlayMyMagicSelect()
    {
        Vfx.SetActive(true);
        other1.SetActive(false);
        other2.SetActive(false);
        other3.SetActive(false);
    }
}
