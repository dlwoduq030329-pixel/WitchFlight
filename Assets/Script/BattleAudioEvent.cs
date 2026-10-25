using UnityEngine;

// Optional adapter for authored Button/Animation events; all clips still live in the table.
public sealed class BattleAudioEvent : MonoBehaviour
{
    [SerializeField] private BattleSound sound;
    [SerializeField] private bool screenSound = true;
    public void PlaySound() => BattleAudio.Play(sound, transform.position, transform, screenSound);
}
