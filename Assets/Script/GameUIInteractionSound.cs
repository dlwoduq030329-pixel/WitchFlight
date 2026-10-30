using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// User input only: ToggleGroup deselection, initialization and network refreshes
// must not emit clicks. Inactive dropdown templates copy this to generated items.
[DisallowMultipleComponent]
public sealed class GameUIInteractionSound : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    private Selectable control;
    private int lastPlayedFrame = -1;
    private void Awake() => control = GetComponent<Selectable>();
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Play();
    }
    public void OnSubmit(BaseEventData eventData) => Play();
    private void Play()
    {
        if (control == null) control = GetComponent<Selectable>();
        if (control == null || !control.IsInteractable() || lastPlayedFrame == Time.frameCount) return;
        lastPlayedFrame = Time.frameCount;
        BattleAudio.Play(BattleSound.UIClick, local: true);
    }
}
