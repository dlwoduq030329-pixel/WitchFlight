using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZakhanStylizedLootDrops
{
    public class Demo : MonoBehaviour
    {
            [Serializable]
            class VFXData
            {
                public string Name;
                public GameObject VFX;
                public List<GameObject> Extras = new List<GameObject>();
            }

            [SerializeField] private List<VFXData> VFX = new List<VFXData>();
            private List<VFXData> CurrentVFXList = new List<VFXData>();
            [SerializeField] private int CurrentSelection = 0;

            public InputSystem_Actions InputAction;

            [Header("UI Settings")]
            [SerializeField] private Demo_UI UI;
            private void Awake()
            {
                InputAction = new InputSystem_Actions();

                SetCurrentList(VFX);
            }

            void Start()
            {
                CurrentSelection = 0;
                CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(true);
                Dummies(true);

                //UI
                UpdateUI();

            }

            private void OnEnable()
            {
                InputAction.Enable();
                InputAction.Player.Next.performed += Next_Performed;
                InputAction.Player.Previous.performed += Back_Performed;
                InputAction.UI.HideUI.performed += HideUI_performed;
            }

            private void OnDisable()
            {
                InputAction.Disable();
                InputAction.Player.Next.performed -= Next_Performed;
                InputAction.Player.Previous.performed -= Back_Performed;
                InputAction.UI.HideUI.performed -= HideUI_performed;
            }

            private void HideUI_performed(InputAction.CallbackContext context)
            {
                UI.EnableCanvas();
            }

            private void Next_Performed(InputAction.CallbackContext context)
            {
                Next();
            }

            private void Back_Performed(InputAction.CallbackContext context)
            {
                Back();
            }
            public void Next()
            {
                if (CurrentSelection >= 0 && CurrentSelection != CurrentVFXList.Count - 1)
                {
                    Dummies(false);
                    CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(false);
                    CurrentSelection++;
                    CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(true);
                    Dummies(true);

                    UpdateUI();
                }
            }
            public void Back()
            {
                if (CurrentSelection > 0)
                {
                    Dummies(false);
                    CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(false);
                    CurrentSelection--;
                    CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(true);
                    Dummies(true);

                    UpdateUI();
                }
            }
            private void UpdateUI()
            {
                
                UI.ChangeName(CurrentVFXList[CurrentSelection].Name);

                if (CurrentSelection > 0)
                {
                    UI.EnableBackButton(true);
                }
                else if (CurrentSelection == 0)
                {
                    UI.EnableBackButton(false);
                }


                if (CurrentSelection >= 0 && CurrentSelection != CurrentVFXList.Count - 1)
                {
                    UI.EnableNextButton(true);
                }
                else if (CurrentSelection == CurrentVFXList.Count - 1)
                {
                    UI.EnableNextButton(false);
                }

            }
            private void SetCurrentList(List<VFXData> Clone)
            {
                if (CurrentVFXList.Count > 0)
                {
                    Dummies(false);
                    CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(false);
                }


                CurrentVFXList.Clear();

                foreach (var VFX in Clone)
                {
                    CurrentVFXList.Add(VFX);
                }

                CurrentSelection = 0;

                CurrentVFXList[CurrentSelection].VFX.gameObject.SetActive(true);
                Dummies(true);

                UpdateUI();

            }

            private void Dummies(bool State)
            {
                foreach (GameObject Dummy in CurrentVFXList[CurrentSelection].Extras)
                {
                    Dummy.gameObject.SetActive(State);
                }
            }

     }
}

