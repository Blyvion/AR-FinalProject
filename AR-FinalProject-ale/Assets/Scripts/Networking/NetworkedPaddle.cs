using Fusion;
using UnityEngine;

/// <summary>
/// Add to the Player Avatar prefab's paddle root alongside NetworkObject.
///
/// ── How it works ─────────────────────────────────────────────────────────────
/// The player with InputAuthority (the owner) reads their local Paddle.cs
/// position/rotation from Hand.cs (unchanged) and publishes it via [Networked]
/// properties every Fusion tick.
///
/// All other clients receive those properties and:
///   1. Move the paddle's Transform so the remote paddle is visible.
///   2. Call Bouncer.move_wall() on all child Bouncers so that when the
///      State Authority runs Balls.move_balls() it sees the remote paddle at
///      the correct world position for accurate physics hit detection.
///
/// ── Hierarchy expected ───────────────────────────────────────────────────────
///   PlayerAvatar (NetworkObject + NetworkedPaddle)
///     └── PaddleRoot (Paddle.cs)
///           ├── Bouncer — rubber forehand   (z_bottom = true)
///           └── Bouncer — rubber backhand
///
/// ── Fusion Inspector Settings (on the PlayerAvatar NetworkObject) ────────────
/// • Object Type: Prefab
/// • Default Update Flags: FixedUpdateNetwork, Render
/// • Input Authority Behaviour: Proxy
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class NetworkedPaddle : NetworkBehaviour
{
    [Header("Scene References")]
    [Tooltip("The Paddle component this player's Hand.cs drives every frame.")]
    [SerializeField] private Paddle _localPaddle;
    [Tooltip("Optional: visual mesh hidden for the owning player (they see their real hand).")]
    [SerializeField] private GameObject _paddleVisualRoot;

    // ─── Networked State ─────────────────────────────────────────────────────

    [Networked] private Vector3    _netPos    { get; set; }
    [Networked] private Quaternion _netRot    { get; set; }
    [Networked] private Vector3    _netVel    { get; set; }
    [Networked] private Vector3    _netAngVel { get; set; }

    // ─── Local State ─────────────────────────────────────────────────────────

    private Bouncer[]  _bouncers;
    private Vector3    _lastPos;
    private Quaternion _lastRot;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    // The prefab Paddle child — used for visuals on REMOTE peers. We capture this
    // before _localPaddle is re-bound to the scene's held_paddle on the owner.
    private Paddle _prefabPaddle;

    public override void Spawned()
    {
        _bouncers = GetComponentsInChildren<Bouncer>();

        // Defensive: if the inspector reference wasn't wired on the prefab, find the
        // prefab's child Paddle automatically. Without this, Render() silently
        // returns and the remote paddle never moves.
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

    // ─── FixedUpdateNetwork — publish local paddle state ────────────────────

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
            // Host's own avatar: write directly.
            _netPos = newPos; _netRot = newRot; _netVel = vel; _netAngVel = angVel;
        }
        else
        {
            // Client's avatar: only the host (StateAuthority) can write [Networked]
            // properties in host/client mode. Send the pose to the host via RPC and
            // let it write to the networked state on our behalf.
            RPC_PublishPaddle(newPos, newRot, vel, angVel);
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

    // ─── Render — apply remote paddle to scene ───────────────────────────────

    public override void Render()
    {
        if (Object.HasInputAuthority || _prefabPaddle == null) return;

        // Drive the prefab Paddle child's WORLD transform — not the avatar root.
        // The Paddle child has a non-zero localPosition/Rotation in the prefab,
        // so writing to the root displaces the visible paddle (and its bouncer
        // children) by that offset, putting them off-screen.
        Transform t = _prefabPaddle.transform;
        t.position = Vector3.Lerp(t.position, _netPos, Time.deltaTime * 60f);
        t.rotation = Quaternion.Slerp(t.rotation, _netRot, Time.deltaTime * 60f);

        // Keep every Bouncer's wall_state current so the physics simulation
        // can detect a collision with the remote paddle correctly.
        foreach (Bouncer b in _bouncers)
            b.move_wall(_netVel, _netAngVel);
    }
}
