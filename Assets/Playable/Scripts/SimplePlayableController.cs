using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

public class SimplePlayableController : MonoBehaviour
{
    private Animator animator;
    private PlayableGraph graph;
    
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip forwardClip;
    [SerializeField] private AnimationClip backwardClip;
    
    private AnimationPlayableOutput animationOutput;
    
    private AnimationMixerPlayable finalMixer;

    private float direction = 0f;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        animator = GetComponent<Animator>();
        ConstructGraph();
    }

    private void Update()
    {
        var dir = 0f;
        if (Keyboard.current.wKey.isPressed) dir += 1;
        if (Keyboard.current.sKey.isPressed) dir += -1;
        
        direction = Mathf.Lerp(direction, dir, Time.deltaTime);
        
        var idleWeight = 1 - Mathf.Abs(direction);
        var forwardWeight = Mathf.Max(direction, 0);
        var backwardWeight = Mathf.Max(-direction, 0);
        
        finalMixer.SetInputWeight(0, idleWeight);
        finalMixer.SetInputWeight(1, forwardWeight);
        finalMixer.SetInputWeight(2, backwardWeight);
    }

    private void ConstructGraph()
    {
        graph = PlayableGraph.Create($"{gameObject.name}_SimplePlayableController");
        
        animationOutput = AnimationPlayableOutput.Create(graph, "Animation", animator);
        
        finalMixer = AnimationMixerPlayable.Create(graph, 3);
        animationOutput.SetSourcePlayable(finalMixer);
        
        var simpleIdle = AnimationClipPlayable.Create(graph, idleClip);
        var forward = AnimationClipPlayable.Create(graph, forwardClip);
        var backward = AnimationClipPlayable.Create(graph, backwardClip);
        
        graph.Connect(simpleIdle, 0, finalMixer, 0);
        graph.Connect(forward, 0, finalMixer, 1);
        graph.Connect(backward, 0, finalMixer, 2);
        
        graph.Play();
    }

    private void OnEnable()
    {
        graph.Play();
    }

    private void OnDisable()
    {
        graph.Stop();
    }

    private void OnDestroy()
    {
        graph.Destroy();
    }
}
