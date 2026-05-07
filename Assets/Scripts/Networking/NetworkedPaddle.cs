using Fusion;
using UnityEngine;

public class NetworkedPaddle : NetworkBehaviour
{
    [Header("Scene References")]
    [Tooltip("The Paddle component this player's Hand.cs drives every frame.")]
    [SerializeField] private Paddle _localPaddle;
    [Tooltip("Optional: visual mesh hidden for the owning player (they see their real hand).")]
    [SerializeField] private GameObject _paddleVisualRoot;

[Networked] private Vector3    _netPos    { get; set; }
    [Networked] private Quaternion _netRot    { get; set; }
    [Networked] private Vector3    _netVel    { get; set; }
    [Networked] private Vector3    _netAngVel { get; set; }

private Bouncer[]  _bouncers;
    private Vector3    _lastPos;
    private Quaternion _lastRot;

    private int        _rpcThrottleTick;

private Paddle _prefabPaddle;

    public override void Spawned()
    {
        _bouncers = GetComponentsInChildren<Bouncer>();

if (_localPaddle == null)
            _localPaddle = GetComponentInChildren<Paddle>(includeInactive: true);

        _prefabPaddle = _localPaddle;

        Debug.Log($"[NetPaddle] Spawned id={Object.Id} hasInputAuth={Object.HasInputAuthority} " +
                  $"prefabPaddle={(_prefabPaddle != null ? _prefabPaddle.name : "NULL")} " +
                  $"bouncers={_bouncers?.Length ?? 0}");

        if (Object.HasInputAuthority)
        {
            if (_prefabPaddle != null)
                _prefabPaddle.gameObject.SetActive(false);

            var play = FindObjectOfType<Play>();
            if (play != null && play.paddle_hand != null && play.paddle_hand.held_paddle != null)
                _localPaddle = play.paddle_hand.held_paddle;

            if (_localPaddle != null)
            {
                _lastPos = _localPaddle.transform.position;
                _lastRot = _localPaddle.transform.rotation;
            }
            Debug.Log($"[NetPaddle] OWNER bound to scene paddle={(_localPaddle != null ? _localPaddle.name : "NULL")}");
        }
        else
        {
            var pool = FindObjectOfType<Balls>();
            if (pool != null)
            {
                int added = 0;
                foreach (Bouncer b in _bouncers)
                    if (!pool.bouncers.Contains(b)) { pool.bouncers.Add(b); added++; }
                Debug.Log($"[NetPaddle] PROXY registered {added} bouncers into Balls.bouncers");
            }
        }
    }

public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority || _localPaddle == null) return;

        Vector3    newPos = _localPaddle.transform.position;
        Quaternion newRot = _localPaddle.transform.rotation;

        float dt = Runner.DeltaTime;

        Vector3 vel = (dt > 0f) ? (newPos - _lastPos) / dt : Vector3.zero;

        Quaternion deltaRot = newRot * Quaternion.Inverse(_lastRot);
        deltaRot.ToAngleAxis(out float angDeg, out Vector3 angAxis);
        if (float.IsNaN(angAxis.x) || angAxis == Vector3.zero) angAxis = Vector3.up;
        Vector3 angVel = angAxis * (angDeg * Mathf.Deg2Rad / Mathf.Max(dt, 0.0001f));

        if (Object.HasStateAuthority)
        {

            _netPos = newPos; _netRot = newRot; _netVel = vel; _netAngVel = angVel;
        }
        else
        {

bool paddleMoved = (newPos - _lastPos).sqrMagnitude > 1e-6f
                            || Quaternion.Angle(newRot, _lastRot) > 0.1f;
            _rpcThrottleTick++;
            if (paddleMoved || _rpcThrottleTick >= 64)
            {
                RPC_PublishPaddle(newPos, newRot, vel, angVel);
                _rpcThrottleTick = 0;
            }
        }

        _lastPos = newPos;
        _lastRot = newRot;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_PublishPaddle(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
    {
        _netPos    = pos;
        _netRot    = rot;
        _netVel    = vel;
        _netAngVel = angVel;
    }

public override void Render()
    {
        if (Object.HasInputAuthority || _prefabPaddle == null) return;

Transform t = _prefabPaddle.transform;

float extrap = Runner.DeltaTime * 1.5f;
        Vector3    targetPos = _netPos + _netVel * extrap;
        Quaternion targetRot = _netRot;

        float posError = Vector3.Distance(t.position, targetPos);
        if (posError > 0.4f)
        {

t.position = targetPos;
            t.rotation = targetRot;
        }
        else
        {

float blend = Time.deltaTime * 80f;
            t.position = Vector3.Lerp(t.position, targetPos, blend);
            t.rotation = Quaternion.Slerp(t.rotation, targetRot, blend);
        }

foreach (Bouncer b in _bouncers)
            b.move_wall(_netVel, _netAngVel);
    }
}
