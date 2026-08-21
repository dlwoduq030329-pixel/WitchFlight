using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZakhanStylizedLootDrops
{
    public class Demo_UI : MonoBehaviour
    {
        [SerializeField] private Demo Demo;
        [SerializeField] private Canvas Canvas;
        [SerializeField] private TMP_Text PrefabName;
        [SerializeField] private Button NextButton;
        [SerializeField] private Button BackButton;

        private void Start()
        {
            NextButton.onClick.AddListener(Demo.Next);
            BackButton.onClick.AddListener(Demo.Back);
        }
        public void EnableCanvas()
        {
            bool state = Canvas.gameObject.activeInHierarchy;
            switch (state)
            {
                case true:
                    Canvas.gameObject.SetActive(false);
                    break;
                case false:
                    Canvas.gameObject.SetActive(true);
                    break;
            }
        }
        public void ChangeName(string NewName)
        {
            PrefabName.text = NewName;

        }
        public void EnableNextButton(bool state)
        {
            NextButton.gameObject.SetActive(state);
        }
        public void EnableBackButton(bool state)
        {
            BackButton.gameObject.SetActive(state);
        }
    }
}
