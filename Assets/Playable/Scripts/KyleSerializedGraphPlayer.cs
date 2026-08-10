using System;
using Playground.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator), typeof(Rigidbody), typeof(Collider))]
public sealed class KyleSerializedGraphPlayer : MonoBehaviour
{
    private const string MovementMixerKey = "MovementMixer";
    private const string JumpMixerKey = "JumpMixer";
    private const string FinalMixerKey = "FinalMixer";

    private const int MovementIdleInput = 0;
    private const int MovementForwardInput = 1;
    private const int MovementBackwardInput = 2;

    private const int JumpStartInput = 0;
    private const int JumpOnAirInput = 1;
    private const int JumpEndInput = 2;

    private const int FinalMovementInput = 0;
    private const int FinalJumpInput = 1;

    private const int GroundedState = 0;
    private const int JumpStartState = 1;
    private const int OnAirState = 2;
    private const int JumpEndState = 3;

    [Header("Playable Graph")]
    [SerializeField] private RuntimePlayableGraph graphAsset;
    [SerializeField] private DirectorUpdateMode updateMode = DirectorUpdateMode.GameTime;
    [SerializeField, Min(0f)] private float animationBlendSpeed = 8f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField, Min(0f)] private float rotationSpeed = 180f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpVelocity = 6f;
    [SerializeField, Min(0f)] private float jumpStartDurationFallback = 0.35f;
    [SerializeField, Min(0f)] private float jumpEndDurationFallback = 0.35f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0f)] private float groundCheckExtraDistance = 0.08f;

    private Animator animator;
    private Rigidbody robotRigidbody;
    private Collider bodyCollider;
    private RuntimePlayableGraphInstance graphInstance;
    private AnimationMixerPlayable movementMixer;
    private AnimationMixerPlayable jumpMixer;
    private AnimationMixerPlayable finalMixer;
    private Vector2 movementInput;
    private bool jumpRequested;
    private int movementState = MovementIdleInput;
    private int jumpState = GroundedState;

    public RuntimePlayableGraphInstance GraphInstance => graphInstance;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        robotRigidbody = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            CreateGraphInstance();
        }
    }

    private void Update()
    {
        if (graphInstance == null || !graphInstance.IsValid)
        {
            return;
        }

        ReadInput();
        UpdateMovementState();
        LoopActiveMovementPlayable();
        UpdateJumpState();
        UpdateMixerWeights(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (graphInstance == null || !graphInstance.IsValid)
        {
            return;
        }

        ApplyMovement();
        if (jumpRequested)
        {
            jumpRequested = false;
            TryJump();
        }
    }

    private void OnDisable()
    {
        DestroyGraphInstance();
    }

    private void ReadInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            movementInput = Vector2.zero;
            return;
        }

        movementInput = new Vector2(
            ReadAxis(keyboard.aKey.isPressed, keyboard.dKey.isPressed),
            ReadAxis(keyboard.sKey.isPressed, keyboard.wKey.isPressed));

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpRequested = true;
        }
    }

    private static float ReadAxis(bool negativePressed, bool positivePressed)
    {
        return (positivePressed ? 1f : 0f) - (negativePressed ? 1f : 0f);
    }

    private void ApplyMovement()
    {
        var planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        var movement = planarForward * movementInput.y;

        var velocity = robotRigidbody.linearVelocity;
        velocity.x = movement.x * moveSpeed;
        velocity.z = movement.z * moveSpeed;
        robotRigidbody.linearVelocity = velocity;

        if (Mathf.Abs(movementInput.x) > Mathf.Epsilon)
        {
            var rotationDelta = Quaternion.Euler(
                0f,
                movementInput.x * rotationSpeed * Time.fixedDeltaTime,
                0f);
            robotRigidbody.MoveRotation(robotRigidbody.rotation * rotationDelta);
        }
    }

    private void TryJump()
    {
        if (jumpState != GroundedState || !IsGrounded())
        {
            return;
        }

        robotRigidbody.AddForce(Vector3.up * jumpVelocity, ForceMode.Impulse);
        EnterJumpState(JumpStartState);
    }

    private void UpdateJumpState()
    {
        switch (jumpState)
        {
            case JumpStartState:
                if (IsJumpClipFinished(JumpStartInput, jumpStartDurationFallback))
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
                if (IsJumpClipFinished(JumpEndInput, jumpEndDurationFallback))
                {
                    EnterJumpState(GroundedState);
                }
                break;
        }
    }

    private void UpdateMixerWeights(float deltaTime)
    {
        var blendFactor = animationBlendSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-animationBlendSpeed * deltaTime);

        UpdateMovementMixerWeights(blendFactor);
        UpdateJumpMixerWeights(blendFactor);

        var jumpWeight = jumpState == GroundedState ? 0f : 1f;
        BlendWeight(finalMixer, FinalMovementInput, 1f - jumpWeight, blendFactor);
        BlendWeight(finalMixer, FinalJumpInput, jumpWeight, blendFactor);
    }

    private void UpdateMovementMixerWeights(float blendFactor)
    {
        BlendWeight(
            movementMixer,
            MovementIdleInput,
            movementState == MovementIdleInput ? 1f : 0f,
            blendFactor);
        BlendWeight(
            movementMixer,
            MovementForwardInput,
            movementState == MovementForwardInput ? 1f : 0f,
            blendFactor);
        BlendWeight(
            movementMixer,
            MovementBackwardInput,
            movementState == MovementBackwardInput ? 1f : 0f,
            blendFactor);
    }

    private void UpdateMovementState()
    {
        var nextState = movementInput.y > Mathf.Epsilon
            ? MovementForwardInput
            : movementInput.y < -Mathf.Epsilon
                ? MovementBackwardInput
                : MovementIdleInput;

        if (nextState == movementState)
        {
            return;
        }

        movementState = nextState;
        RestartPlayable(movementMixer.GetInput(movementState));
    }

    private void LoopActiveMovementPlayable()
    {
        var playable = movementMixer.GetInput(movementState);
        if (!playable.IsValid())
        {
            return;
        }

        var duration = playable.GetDuration();
        if (!IsFinitePositiveDuration(duration) || playable.GetTime() < duration)
        {
            return;
        }

        playable.SetTime(playable.GetTime() % duration);
        playable.SetDone(false);
        playable.SetSpeed(1d);
    }

    private void UpdateJumpMixerWeights(float blendFactor)
    {
        BlendWeight(
            jumpMixer,
            JumpStartInput,
            jumpState == GroundedState || jumpState == JumpStartState ? 1f : 0f,
            blendFactor);
        BlendWeight(
            jumpMixer,
            JumpOnAirInput,
            jumpState == OnAirState ? 1f : 0f,
            blendFactor);
        BlendWeight(
            jumpMixer,
            JumpEndInput,
            jumpState == JumpEndState ? 1f : 0f,
            blendFactor);
    }

    private static void BlendWeight(
        AnimationMixerPlayable mixer,
        int inputIndex,
        float targetWeight,
        float blendFactor)
    {
        var currentWeight = mixer.GetInputWeight(inputIndex);
        mixer.SetInputWeight(inputIndex, Mathf.Lerp(currentWeight, targetWeight, blendFactor));
    }

    private void EnterJumpState(int nextState)
    {
        jumpState = nextState;

        switch (nextState)
        {
            case JumpStartState:
                ResetJumpPlayable(JumpStartInput);
                break;

            case OnAirState:
                ResetJumpPlayable(JumpOnAirInput);
                break;

            case JumpEndState:
                ResetJumpPlayable(JumpEndInput);
                break;
        }
    }

    private void ResetJumpPlayable(int inputIndex)
    {
        RestartPlayable(jumpMixer.GetInput(inputIndex));
    }

    private bool IsJumpClipFinished(int inputIndex, float fallbackDuration)
    {
        var playable = jumpMixer.GetInput(inputIndex);
        if (!playable.IsValid())
        {
            return true;
        }

        var duration = playable.GetDuration();
        if (!IsFinitePositiveDuration(duration))
        {
            duration = fallbackDuration;
        }

        return playable.GetTime() >= duration;
    }

    private static void RestartPlayable(Playable playable)
    {
        if (!playable.IsValid())
        {
            return;
        }

        playable.SetTime(0d);
        playable.SetDone(false);
        playable.SetSpeed(1d);
    }

    private static bool IsFinitePositiveDuration(double duration)
    {
        return duration > 0d &&
               !double.IsInfinity(duration) &&
               !double.IsNaN(duration) &&
               duration < double.MaxValue;
    }

    private bool IsGrounded()
    {
        var bounds = bodyCollider.bounds;
        var origin = bounds.center;
        var radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        var castDistance = bounds.extents.y - radius + groundCheckExtraDistance;

        return Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out _,
            castDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);
    }

    private void CreateGraphInstance()
    {
        DestroyGraphInstance();

        if (graphAsset == null)
        {
            Debug.LogError("RuntimePlayableGraph is not assigned.", this);
            enabled = false;
            return;
        }

        try
        {
            graphInstance = graphAsset.CreateInstance(animator, updateMode);
            if (!TryCacheMixers())
            {
                DestroyGraphInstance();
                enabled = false;
                return;
            }

            InitializeMixerWeights();
            graphInstance.Play();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            DestroyGraphInstance();
            enabled = false;
        }
    }

    private bool TryCacheMixers()
    {
        if (!graphInstance.TryGetAnimationMixer(MovementMixerKey, out movementMixer))
        {
            Debug.LogError($"AnimationMixerPlayable '{MovementMixerKey}' was not found.", this);
            return false;
        }

        if (!graphInstance.TryGetAnimationMixer(JumpMixerKey, out jumpMixer))
        {
            Debug.LogError($"AnimationMixerPlayable '{JumpMixerKey}' was not found.", this);
            return false;
        }

        if (!graphInstance.TryGetAnimationMixer(FinalMixerKey, out finalMixer))
        {
            Debug.LogError($"AnimationMixerPlayable '{FinalMixerKey}' was not found.", this);
            return false;
        }

        if (movementMixer.GetInputCount() < 3 ||
            jumpMixer.GetInputCount() < 3 ||
            finalMixer.GetInputCount() < 2)
        {
            Debug.LogError(
                "Mixer input layout must be MovementMixer=3, JumpMixer=3, FinalMixer=2 or greater.",
                this);
            return false;
        }

        return true;
    }

    private void InitializeMixerWeights()
    {
        movementMixer.SetInputWeight(MovementIdleInput, 1f);
        movementMixer.SetInputWeight(MovementForwardInput, 0f);
        movementMixer.SetInputWeight(MovementBackwardInput, 0f);

        jumpMixer.SetInputWeight(JumpStartInput, 1f);
        jumpMixer.SetInputWeight(JumpOnAirInput, 0f);
        jumpMixer.SetInputWeight(JumpEndInput, 0f);

        finalMixer.SetInputWeight(FinalMovementInput, 1f);
        finalMixer.SetInputWeight(FinalJumpInput, 0f);

        RestartPlayable(movementMixer.GetInput(MovementIdleInput));
        RestartPlayable(jumpMixer.GetInput(JumpStartInput));
    }

    private void DestroyGraphInstance()
    {
        graphInstance?.Dispose();
        graphInstance = null;
        movementMixer = default;
        jumpMixer = default;
        finalMixer = default;
        movementInput = Vector2.zero;
        jumpRequested = false;
        movementState = MovementIdleInput;
        jumpState = GroundedState;
    }
}
