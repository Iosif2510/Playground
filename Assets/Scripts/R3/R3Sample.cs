using System;
using R3;
using UnityEngine;

public class R3Sample : MonoBehaviour
{
    void Start()
    {
        var d = Disposable.CreateBuilder();
        // 'EveryUpdate' is now an extension on the Observable class
        Observable.EveryUpdate()
            .Where(_ => Input.GetKeyDown(KeyCode.Space))
            .Subscribe(_ => 
            {
                Debug.Log("Space pressed!");
            })
            .AddTo(ref d); // R3 also supports .AddTo(Component)
            
        Observable.Interval(TimeSpan.FromSeconds(1))
            .Subscribe(x => Debug.Log(x)).AddTo(ref d); // Directly pass token
        d.RegisterTo(destroyCancellationToken);
    }
}
