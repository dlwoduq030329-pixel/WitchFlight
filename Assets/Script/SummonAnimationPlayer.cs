using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Local presentation only. Three clips require no Animator Controller or animation events.
// Network simulation decides movement, successful shots and destruction independently.
public sealed class SummonAnimationPlayer : IDisposable
{
    private const int Move = 0, Attack = 1, Death = 2;
    private readonly AnimationClip[] clips;
    private readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[3];
    private readonly Animator animator;
    private readonly bool originalRootMotion;
    private PlayableGraph graph;
    private AnimationPlayableOutput output;
    private int lastSequence, motion = -1, outputMotion = -1;
    private float elapsed;
    private bool dying;

    public SummonAnimationPlayer(Animator target, MagicStatEntry stats, int initialSequence)
    {
        animator = target;
        clips = new[] { stats.summonMoveAnimation, stats.summonAttackAnimation, stats.summonDeathAnimation };
        lastSequence = initialSequence; // Do not replay historical shots on a newly visible proxy.
        if (animator == null || (clips[Move] == null && clips[Attack] == null && clips[Death] == null)) return;
        originalRootMotion = animator.applyRootMotion;
        animator.applyRootMotion = false;
        graph = PlayableGraph.Create("Summon animations");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        output = AnimationPlayableOutput.Create(graph, "Summon body", animator);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
            playables[i].SetSpeed(0); // Explicit time: only Move loops, even if Attack/Death import settings loop.
        }
        graph.Play();
    }

    public void Render(bool moving, int attackSequence, float deltaTime)
    {
        if (!graph.IsValid() || animator == null || !animator.isActiveAndEnabled) return;
        float dt = Mathf.Max(0f, deltaTime);
        if (dying)
        {
            elapsed += dt;
            Sample(Death, elapsed);
            return;
        }
        if (attackSequence != lastSequence)
        {
            lastSequence = attackSequence;
            if (clips[Attack] != null) { motion = Attack; elapsed = 0f; }
        }
        if (motion == Attack)
        {
            Sample(Attack, elapsed);
            elapsed += dt;
            if (elapsed >= Length(Attack)) { motion = -1; elapsed = 0f; }
            return;
        }
        if (clips[Move] == null) return;
        if (motion != Move) { motion = Move; elapsed = 0f; }
        elapsed = moving ? Mathf.Repeat(elapsed + dt, Length(Move)) : 0f;
        Sample(Move, elapsed);
    }

    // Returns the local visual lifetime; never extends the network object's hitbox/lifetime.
    public float PlayDeath()
    {
        if (dying) return 0f;
        dying = true;
        if (!graph.IsValid() || animator == null || !animator.isActiveAndEnabled || clips[Death] == null) return 0f;
        motion = Death; elapsed = 0f;
        Sample(Death, 0f);
        return Length(Death);
    }

    private float Length(int index) => Mathf.Max(.01f, clips[index].length);

    private void Sample(int index, float time)
    {
        if (clips[index] == null) return;
        if (outputMotion != index)
        {
            output.SetSourcePlayable(playables[index]);
            outputMotion = index;
        }
        playables[index].SetTime(Mathf.Min(time, Length(index) - .0001f));
        graph.Evaluate(0f);
    }

    public void Dispose()
    {
        if (!graph.IsValid()) return;
        graph.Destroy();
        if (animator != null) animator.applyRootMotion = originalRootMotion;
    }
}
