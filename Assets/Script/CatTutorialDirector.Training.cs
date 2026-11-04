using UnityEngine;

public sealed partial class CatTutorialDirector
{
    private static CatTutorialDirector trainingDirector;
    private bool practiceStarted, practiceArmed, savedTrainingLoadout, practiceEmote, practiceLockedCast;
    private readonly TrainingPracticeProgress practiceProgress = new();
    private float practiceReadyAt, practiceCompletedAt = -1f;
    private MagicType savedMagic1, savedMagic2;
    private Vector3 trainingStartPosition, targetOriginalPosition;
    private Quaternion trainingStartRotation, targetOriginalRotation;
    private Player practiceTarget;
    private PlayerData practiceData;
    private float nextTargetCheck;

    // Training is a single-player runner. These gates never change online match rules.
    public static bool TrainingSessionActive => BattleManager.Instance != null && BattleManager.Instance.IsTraining &&
        BattleManager.Instance.Phase == BattleStartPhase.TrainingTutorial;
    public static bool PracticeInputActive => TrainingSessionActive && trainingDirector != null && trainingDirector.practiceArmed;
    public static bool CanTrainingBotAttack(MagicTestBot bot) => PracticeInputActive &&
        trainingDirector.CurrentPractice == CatTrainingAction.Parry && trainingDirector.practiceCompletedAt < 0f &&
        trainingDirector.practiceTarget != null && bot.gameObject == trainingDirector.practiceTarget.gameObject;
    private CatTrainingAction CurrentPractice => running && Steps != null && step < Steps.Length
        ? Steps[step].practice : CatTrainingAction.None;

    public static void NotifyPracticeEmote(Player player)
    {
        if (PracticeInputActive && player == Player.LocalPlayer) trainingDirector.practiceEmote = true;
    }

    public static void NotifyPracticeLockedCast(Player player)
    {
        if (PracticeInputActive && player == Player.LocalPlayer) trainingDirector.practiceLockedCast = true;
    }

    private void BeginPractice()
    {
        Player player = Player.LocalPlayer;
        PlayerData data = PlayerData.Local;
        if (!TrainingSessionActive || player == null || data == null || !player.Object.HasStateAuthority) return;
        trainingDirector = this;
        if (!savedTrainingLoadout)
        {
            practiceData = data;
            savedMagic1 = data.magic1; savedMagic2 = data.magic2;
            trainingStartPosition = player.transform.position; trainingStartRotation = player.transform.rotation;
            savedTrainingLoadout = true;
        }
        // Only this temporary, single-player loadout changes. DataConfig/BACKND are untouched.
        if (CurrentPractice == CatTrainingAction.AimAndFire || CurrentPractice == CatTrainingAction.SwitchSlots ||
            CurrentPractice == CatTrainingAction.ChoosePerk)
        { data.magic1 = MagicType.Fire; data.magic2 = MagicType.Ice; }
        player.InitPlayer(data);
        player.PrepareTrainingPractice();
        practiceStarted = true; practiceArmed = false;
        practiceReadyAt = Time.unscaledTime + .25f;
        practiceCompletedAt = -1f;
        practiceEmote = practiceLockedCast = false;
        if (CurrentPractice == CatTrainingAction.ChoosePerk) data.GrantTrainingPracticeChoice();
        practiceProgress.Reset(player.CurrentSpeedStage, player.CastSequence, player.ParrySequence, data.PerkChoicesUsed);
        if (CurrentPractice == CatTrainingAction.AimAndFire || CurrentPractice == CatTrainingAction.Parry)
            PreparePracticeTarget(player);
        next.interactable = false; next.targetGraphic.raycastTarget = false;
        nextHint.gameObject.SetActive(true);
        nextHint.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 520f);
        if (EventSystemExists()) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    private static bool EventSystemExists() => UnityEngine.EventSystems.EventSystem.current != null;

    private void UpdatePractice()
    {
        if (!practiceStarted) { SetPracticeCursor(false); return; }
        if (CombatPresentation.MenuOpen) return;
        // The click which starts practice must never become a cast/choice or skip a lesson.
        if (!practiceArmed)
        {
            if (Time.unscaledTime < practiceReadyAt || Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
            practiceArmed = true;
        }
        SetPracticeCursor(true);
        Player player = Player.LocalPlayer;
        if (player == null || player.Object == null || !player.Object.IsValid) return;
        if (practiceCompletedAt >= 0f)
        {
            nextHint.text = "성공!";
            if (Time.unscaledTime - practiceCompletedAt >= .8f && !Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            { step++; ShowStep(); }
            return;
        }
        bool completed = practiceProgress.Observe(CurrentPractice, player.CurrentSpeedStage, player.CurrentMagicSlot,
            player.IsFullyLocked || practiceLockedCast, player.CastSequence, player.ParrySequence, PlayerData.Local != null ? PlayerData.Local.PerkChoicesUsed : 0,
            player.IsBoosting, player.CobraActive, practiceEmote, Time.deltaTime);
        switch (CurrentPractice)
        {
            case CatTrainingAction.SpeedUpAndDown:
                nextHint.text = practiceProgress.IncreasedSpeed ? "S로 속도 낮추기" : "속도 높이기";
                break;
            case CatTrainingAction.AimAndFire:
                nextHint.text = practiceProgress.Charged ? "조준 키를 떼어 발사" : "봇을 조준해 록온 완료";
                MaintainPracticeTarget(player);
                break;
            case CatTrainingAction.SwitchSlots:
                // Start on slot 1; require an actual change to 2 and back to 1.
                nextHint.text = practiceProgress.SelectedSecond ? "1번 슬롯 선택" : "2번 슬롯 선택";
                break;
            case CatTrainingAction.Parry:
                nextHint.text = "노란 느낌표에 맞춰 패링";
                MaintainPracticeTarget(player);
                break;
            case CatTrainingAction.ChoosePerk:
                nextHint.text = "Space 유지 + 좌/우클릭";
                break;
            case CatTrainingAction.Boost:
                nextHint.text = "전진하며 부스트 유지";
                break;
            case CatTrainingAction.QuickBrake:
                nextHint.text = player.CurrentSpeedStage < 3 ? "속도를 3단계까지 높이기" : "최고 속도에서 급제동 키";
                break;
            case CatTrainingAction.Emote:
                nextHint.text = "감정표현 키 유지 → 방향 선택 → 떼기";
                break;
        }
        if (completed) practiceCompletedAt = Time.unscaledTime;
    }

    private void SetPracticeCursor(bool locked)
    {
        if (!TrainingSessionActive || CombatPresentation.MenuOpen || !Application.isFocused) return;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void EndPracticeStep()
    {
        if (practiceTarget != null) practiceTarget.GetComponent<MagicTestBot>()?.ResetTrainingAttack();
        practiceStarted = practiceArmed = false;
        practiceCompletedAt = -1f;
        if (trainingScene) SetPracticeCursor(false);
    }

    private void PreparePracticeTarget(Player player)
    {
        if (practiceTarget == null)
        {
            foreach (Player candidate in Player.ActiveCombatants)
                if (candidate != null && candidate.IsTestBot && candidate.Runner == player.Runner && candidate.IsAlive &&
                    (practiceTarget == null || (candidate.transform.position - player.transform.position).sqrMagnitude <
                    (practiceTarget.transform.position - player.transform.position).sqrMagnitude)) practiceTarget = candidate;
            if (practiceTarget != null)
            { targetOriginalPosition = practiceTarget.transform.position; targetOriginalRotation = practiceTarget.transform.rotation; }
        }
        MovePracticeTarget(player);
    }

    private void MaintainPracticeTarget(Player player)
    {
        if (Time.unscaledTime < nextTargetCheck) return;
        nextTargetCheck = Time.unscaledTime + 2f;
        if (practiceTarget == null) { PreparePracticeTarget(player); return; }
        if ((player.LockAimPoint - practiceTarget.LockAimPoint).sqrMagnitude > 60f * 60f ||
            !MagicProjectile.HasBlastSight(player.LockAimPoint, practiceTarget)) MovePracticeTarget(player);
    }

    private void MovePracticeTarget(Player player)
    {
        if (practiceTarget == null) return;
        Camera view = Camera.main;
        Vector3 forward = view != null ? view.transform.forward : player.transform.forward;
        Vector3 origin = player.LockAimPoint;
        for (int i = 0; i < 12; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(i * 30f, Vector3.up) * forward;
            Vector3 point = origin + direction * 25f;
            // Physics query explicitly ignores the player/bot but rejects solid map geometry.
            bool clear = true;
            foreach (var hit in Physics.SphereCastAll(origin, 1.2f, direction, 25f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<Player>() == null) { clear = false; break; }
            if (!clear) continue;
            practiceTarget.SetTrainingPosition(point, Quaternion.LookRotation(-direction));
            practiceTarget.GetComponent<MagicTestBot>()?.ResetTrainingAttack();
            practiceTarget.RestoreHealth(practiceTarget.MaxHp);
            return;
        }
    }

    private void RestoreTrainingPractice()
    {
        EndPracticeStep();
        if (savedTrainingLoadout)
        {
            PlayerData data = practiceData;
            Player player = Player.LocalPlayer;
            if (data != null && data.Object != null && data.Object.IsValid && data.Object.HasStateAuthority)
            {
                data.magic1 = savedMagic1; data.magic2 = savedMagic2;
                data.ResetMatchPerks();
                if (player != null && player.Object != null && player.Object.IsValid && player.Runner == data.Runner &&
                    player.ParticipantRef == data.ParticipantRef)
                { player.InitPlayer(data); player.SetTrainingPosition(trainingStartPosition, trainingStartRotation); }
            }
            savedTrainingLoadout = false;
            practiceData = null;
        }
        if (practiceTarget != null && practiceTarget.Object != null && practiceTarget.Object.IsValid)
            practiceTarget.SetTrainingPosition(targetOriginalPosition, targetOriginalRotation);
        practiceTarget = null;
        if (trainingDirector == this) trainingDirector = null;
    }

}
