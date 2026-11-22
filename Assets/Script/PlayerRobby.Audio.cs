using UnityEngine;

public sealed partial class PlayerRobby
{
    private BattleAudio.Loop footstepsAudio;
    private bool lobbyAudioInitialized;
    private int previousAudioInteraction;

    private void RenderLobbyAudio()
    {
        if (Object == null || !Object.IsValid)
        {
            StopLobbyAudio();
            return;
        }

        if (!lobbyAudioInitialized)
        {
            previousAudioInteraction = InteractionState;
            lobbyAudioInitialized = true;
        }
        if (InteractionState != previousAudioInteraction)
        {
            previousAudioInteraction = InteractionState;
            if (InteractionState == 1)
                BattleAudio.Play(BattleSound.WaitingRoomDrink, transform.position, transform, Object.HasInputAuthority);
        }

        bool walking = IsWalking && !InteractionBusy && !kickPlaying &&
            Physics.Raycast(transform.position + Vector3.up * .2f, Vector3.down, .5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (!walking) footstepsAudio.Stop();
        else if (!footstepsAudio.IsActive)
            footstepsAudio = BattleAudio.StartLoop(BattleSound.WaitingRoomFootsteps, transform, Object.HasInputAuthority);
    }

    private void PlayKickAudio()
    {
        if (Object != null && Object.IsValid)
            BattleAudio.Play(BattleSound.WaitingRoomKick, transform.position, transform, Object.HasInputAuthority);
    }

    private void StopLobbyAudio()
    {
        footstepsAudio.Stop();
        lobbyAudioInitialized = false;
    }

    private void OnDisable() => StopLobbyAudio();
}
