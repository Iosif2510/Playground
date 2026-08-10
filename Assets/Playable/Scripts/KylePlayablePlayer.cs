using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

public class KylePlayablePlayer : MonoBehaviour
{
    private const int IdleState = 0;
    private const int JumpStartState = 1;
    private const int OnAirState = 2;
    private const int JumpEndState = 3;

    private Rigidbody robotRigidbody;
    private Collider bodyCollider;
    private Animator animator;
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable jumpMixer;
    private AnimationMixerPlayable finalMixer;
    private AnimationClipPlayable jumpStartPlayable;
    private AnimationClipPlayable onAirPlayable;
    private AnimationClipPlayable jumpEndPlayable;

    [Header("Jump")]
    [SerializeField] private float jumpImpulse = 6f;
    [SerializeField] private float animationBlendSpeed = 8f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckExtraDistance = 0.08f;

    [Header("AnimationClip")]
    [SerializeField] private AnimationClip idle;
    [SerializeField] private AnimationClip jumpStart;
    [SerializeField] private AnimationClip onAir;
    [SerializeField] private AnimationClip jumpEnd;

    private bool jumpRequested;
    private int jumpState = IdleState;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        robotRigidbody = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        animator = GetComponent<Animator>();
        CreateGraph();
    }

    private void Update()
    {
        if (Keyboard.current?.spaceKey.wasPressedThisFrame == true)
        {
            jumpRequested = true;
        }

        UpdateJumpState();
        UpdateMixerWeights(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (jumpRequested)
        {
            Jump();
        }
    }

    public void Jump()
    {
        jumpRequested = false;
        if (jumpState != IdleState || !IsGrounded())
        {
            return;
        }

        robotRigidbody.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
        EnterJumpState(JumpStartState);
    }

    private void UpdateJumpState()
    {
        switch (jumpState)
        {
            case JumpStartState:
                if (jumpStartPlayable.GetTime() >= jumpStart.length)
                {
                    EnterJumpState(OnAirState);
                }
                break;

            case OnAirState:
                if (robotRigidbody.linearVelocity.y <= 0f && IsGrounded())
                {
                    EnterJumpState(JumpEndState);
                }
                break;

            case JumpEndState:
                if (jumpEndPlayable.GetTime() >= jumpEnd.length)
                {
                    EnterJumpState(IdleState);
                }
                break;
        }
    }

    private void UpdateMixerWeights(float deltaTime)
    {
        var maxDelta = animationBlendSpeed * deltaTime;
        var jumpWeight = jumpState == IdleState ? 0f : 1f;

        SetWeight(finalMixer, 0, 1f - jumpWeight, maxDelta);
        SetWeight(finalMixer, 1, jumpWeight, maxDelta);

        SetWeight(jumpMixer, 0, jumpState == JumpStartState ? 1f : 0f, maxDelta);
        SetWeight(jumpMixer, 1, jumpState == OnAirState ? 1f : 0f, maxDelta);
        SetWeight(jumpMixer, 2, jumpState == JumpEndState ? 1f : 0f, maxDelta);
    }

    private static void SetWeight(
        AnimationMixerPlayable mixer,
        int inputIndex,
        float targetWeight,
        float maxDelta)
    {
        var currentWeight = mixer.GetInputWeight(inputIndex);
        mixer.SetInputWeight(
            inputIndex,
            Mathf.MoveTowards(currentWeight, targetWeight, maxDelta));
    }

    private void EnterJumpState(int nextState)
    {
        jumpState = nextState;

        switch (nextState)
        {
            case JumpStartState:
                jumpStartPlayable.SetTime(0d);
                break;

            case OnAirState:
                onAirPlayable.SetTime(0d);
                break;

            case JumpEndState:
                jumpEndPlayable.SetTime(0d);
                break;
        }
    }

    private bool IsGrounded()
    {
        Bounds bounds = bodyCollider.bounds;

        Vector3 origin = bounds.center;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        float castDistance = bounds.extents.y - radius + groundCheckExtraDistance;

        return Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out _,
            castDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    private void CreateGraph()
    {
        playableGraph = PlayableGraph.Create("KylePlayableGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        var output = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);

        finalMixer = AnimationMixerPlayable.Create(playableGraph, 2);
        output.SetSourcePlayable(finalMixer);
        finalMixer.SetInputWeight(0, 1f);
        finalMixer.SetInputWeight(1, 0f);

        var idleClip = AnimationClipPlayable.Create(playableGraph, idle);
        playableGraph.Connect(idleClip, 0, finalMixer, 0);

        jumpMixer = AnimationMixerPlayable.Create(playableGraph, 3);
        playableGraph.Connect(jumpMixer, 0, finalMixer, 1);

        jumpStartPlayable = AnimationClipPlayable.Create(playableGraph, jumpStart);
        onAirPlayable = AnimationClipPlayable.Create(playableGraph, onAir);
        jumpEndPlayable = AnimationClipPlayable.Create(playableGraph, jumpEnd);

        playableGraph.Connect(jumpStartPlayable, 0, jumpMixer, 0);
        playableGraph.Connect(onAirPlayable, 0, jumpMixer, 1);
        playableGraph.Connect(jumpEndPlayable, 0, jumpMixer, 2);

        jumpMixer.SetInputWeight(0, 0f);
        jumpMixer.SetInputWeight(1, 0f);
        jumpMixer.SetInputWeight(2, 0f);

        playableGraph.Play();
    }

    private void OnDestroy()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Destroy();
        }
    }
}
