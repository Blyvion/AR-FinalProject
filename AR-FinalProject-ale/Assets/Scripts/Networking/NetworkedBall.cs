using Fusion;
using UnityEngine;

/// <summary>
/// Add to the Ball prefab alongside the existing Ball component.
/// Also add a NetworkObject component to the same prefab.
///
/// ── Authority Model ──────────────────────────────────────────────────────────
/// The player who last hit the ball is the State Authority.
/// That player's machine runs Ball.cs physics locally at display rate (90 Hz)
/// for zero-latency, perfectly-felt impact. Every Fusion tick (~60 Hz) the
/// authority publishes a position/velocity snapshot to [Networked] properties.
/// All other clients (proxies) freeze their local physics, read the snapshot,
/// and extrapolate a smooth visual in Render().
///
/// NOTE: NetworkRigidbody3D cannot be used here because your Ball.cs uses a
/// custom analytical physics model (not Unity's Rigidbody). This script is the
/// correct replacement.
///
/// ── Fusion Inspector Settings (on the Ball NetworkObject) ───────────────────
/// • Object Type: Prefab
/// • Default Update Flags: FixedUpdateNetwork, Render     (both ticked)
/// • Interpolation Data: None  (we interpolate manually in Render())
/// • Simulation Behaviour: Simulation Only
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(Ball))]
public class NetworkedBall : NetworkBehaviour
{
    [Header("Proxy Correction Tuning")]
    [Tooltip("Velocity-extrapolated position is lerped toward this speed (m/s display units). "
           + "Lower = smoother but laggier. Higher = more responsive but jittery.")]
    [SerializeField] private float _proxyCorrectSpeed = 25f;
    [Tooltip("If the proxy position drifts beyond this distance (m), hard-snap instead of lerp.")]
    [SerializeField] private float _snapThreshold = 0.35f;

    // ─── Networked State ─────────────────────────────────────────────────────
    // Published every tick by the State Authority; read by proxies.

    [Networked] private Vector3      _netPos    { get; set; }
    [Networked] private Vector3      _netVel    { get; set; }
    [Networked] private Vector3      _netAngVel { get; set; }

    // ─── Power-up Networked State ────────────────────────────────────────────
    // Multiplier applied to BOTH the visual transform.localScale AND Ball.radius
    // (which Bouncer.check_for_bounce reads every frame). Set on the host by
    // PowerUpEffect.Apply via SetSizeMultiplier; replicated to every peer and
    // surfaced through ChangeDetector below so proxies update instantly.
    [Networked] public float SizeMultiplier { get; set; }

    // Scales the local physics step on the State Authority. 1 = normal,
    // 0.1 = 10× slower. Used by the SlowBall power-up.
    [Networked] public float     SpeedMultiplier  { get; set; }
    // Tick-based deadline for auto-reverting SpeedMultiplier back to 1. Set
    // by SetSpeedMultiplier when a duration is provided. Reading is safe on
    // every peer, but only the current State Authority writes the revert.
    [Networked]        TickTimer SpeedRevertTimer { get; set; }

    // ─── Local References ────────────────────────────────────────────────────

    private Ball           _ball;
    private Vector3        _baseLocalScale;
    private float          _baseRadius;
    private ChangeDetector _changes;

    // Pending value when SetSizeMultiplier is called on a peer that doesn't
    // yet hold State Authority (e.g. a serving client that just called
    // RequestStateAuthority in the same frame — the transfer is async). The
    // pending value is flushed in FixedUpdateNetwork once authority lands.
    // -1 = no pending write.
    private float _pendingSizeMultiplier  = -1f;

    // Same pattern for speed; -1 = none. _pendingSpeedDuration carries an
    // optional auto-revert duration in seconds; <=0 = no auto-revert.
    private float _pendingSpeedMultiplier = -1f;
    private float _pendingSpeedDuration   = 0f;

    private int _renderTickCount;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    public override void Spawned()
    {
        _ball = GetComponent<Ball>();

        // Capture defaults BEFORE applying any networked size, so a late joiner
        // who arrives mid-rally with SizeMultiplier already non-1 still has the
        // correct base scale/radius to revert to later.
        _baseLocalScale = transform.localScale;
        _baseRadius     = _ball.radius;
        _changes        = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // First-time spawn on the host: initialise to 1 (default(float) is 0).
        if (Object.HasStateAuthority && SizeMultiplier <= 0f)
            SizeMultiplier = 1f;
        if (Object.HasStateAuthority && SpeedMultiplier <= 0f)
            SpeedMultiplier = 1f;

        // Honour the current networked values immediately on every peer
        // (covers late joiners who arrive mid-effect).
        ApplySizeLocally (SizeMultiplier  > 0f ? SizeMultiplier  : 1f);
        ApplySpeedLocally(SpeedMultiplier > 0f ? SpeedMultiplier : 1f);

        var rend = GetComponentInChildren<Renderer>();
        Debug.Log($"[NetBall] Spawned id={Object.Id} hasStateAuth={Object.HasStateAuthority} " +
                  $"pos={transform.position} active={gameObject.activeSelf} " +
                  $"rendererEnabled={(rend != null ? rend.enabled.ToString() : "NULL")} " +
                  $"rendererName={(rend != null ? rend.gameObject.name : "NULL")}");

        if (!Object.HasStateAuthority)
            _ball.freeze = true;

        var pool = FindObjectOfType<Balls>();
        if (pool != null) pool.register_remote_ball(_ball);
    }

    void ApplySizeLocally(float mult)
    {
        if (mult <= 0f) mult = 1f;
        transform.localScale = _baseLocalScale * mult;
        // Bouncer.check_for_bounce reads ball.radius every frame, so updating
        // it here keeps collision math consistent with the visible ball on
        // whichever peer currently owns physics.
        _ball.radius = _baseRadius * mult;
    }

    void ApplySpeedLocally(float mult)
    {
        if (mult <= 0f) mult = 1f;
        // Only the State Authority's Ball.move_ball actually consumes this,
        // but we apply it on every peer so an authority handover doesn't
        // briefly run physics at the wrong speed.
        _ball.speed_multiplier = mult;
    }

    /// <summary>
    /// Sets the networked size multiplier.
    /// • If the caller already owns State Authority, the write is immediate.
    /// • Otherwise the value is queued in _pendingSizeMultiplier and flushed
    ///   in FixedUpdateNetwork on the first tick where authority is held.
    ///   This supports two real callers:
    ///     1. The HOST applying a power-up effect on a ball currently owned
    ///        by a client — host calls RequestStateAuthority + queues.
    ///     2. The SERVING peer (host or client) resetting size to 1 in
    ///        Play.hold_ball — the same call site requested authority just
    ///        before this, but the transfer is asynchronous.
    /// </summary>
    public void SetSizeMultiplier(float m)
    {
        if (Object == null || !Object.IsValid) return;

        if (Object.HasStateAuthority)
        {
            SizeMultiplier = m;
            ApplySizeLocally(m);
            _pendingSizeMultiplier = -1f;
            return;
        }

        // Host can grab authority for power-up writes; non-server peers must
        // already be in the middle of an authority transfer (e.g. from
        // Play.hold_ball) for the queued value to actually flush.
        if (Runner != null && Runner.IsServer)
            Object.RequestStateAuthority();

        _pendingSizeMultiplier = m;
    }

    /// <summary>
    /// Sets the networked ball-speed multiplier and (optionally) an
    /// auto-revert duration. Same authority-deferred semantics as
    /// SetSizeMultiplier — a non-authority caller queues the write and lets
    /// FixedUpdateNetwork flush it once it gains authority.
    /// </summary>
    public void SetSpeedMultiplier(float m, float durationSeconds = 0f)
    {
        if (Object == null || !Object.IsValid) return;

        if (Object.HasStateAuthority)
        {
            SpeedMultiplier  = m;
            SpeedRevertTimer = (durationSeconds > 0f)
                ? TickTimer.CreateFromSeconds(Runner, durationSeconds)
                : default;
            ApplySpeedLocally(m);
            _pendingSpeedMultiplier = -1f;
            _pendingSpeedDuration   = 0f;
            return;
        }

        if (Runner != null && Runner.IsServer)
            Object.RequestStateAuthority();

        _pendingSpeedMultiplier = m;
        _pendingSpeedDuration   = durationSeconds;
    }

    // ─── FixedUpdateNetwork — runs at the Fusion tick rate (~60 Hz) ──────────

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            // Flush a pending size write that arrived before authority did.
            if (_pendingSizeMultiplier > 0f)
            {
                SizeMultiplier = _pendingSizeMultiplier;
                ApplySizeLocally(_pendingSizeMultiplier);
                _pendingSizeMultiplier = -1f;
            }

            // Flush pending speed write + arm any requested auto-revert timer.
            if (_pendingSpeedMultiplier > 0f)
            {
                SpeedMultiplier  = _pendingSpeedMultiplier;
                SpeedRevertTimer = (_pendingSpeedDuration > 0f)
                    ? TickTimer.CreateFromSeconds(Runner, _pendingSpeedDuration)
                    : default;
                ApplySpeedLocally(_pendingSpeedMultiplier);
                _pendingSpeedMultiplier = -1f;
                _pendingSpeedDuration   = 0f;
            }

            // Auto-revert: when the SlowBall timer expires, the current
            // authority restores SpeedMultiplier to 1. .Expired stays true
            // forever after firing, so we clear the timer to make this
            // self-healing if authority changes hands repeatedly.
            if (SpeedMultiplier != 1f && SpeedRevertTimer.Expired(Runner))
            {
                SpeedMultiplier  = 1f;
                SpeedRevertTimer = default;
                ApplySpeedLocally(1f);
            }

            _netPos    = _ball.motion.position;
            _netVel    = _ball.motion.velocity;
            _netAngVel = _ball.motion.angular_velocity;
        }
        else
        {
            // Proxy: freeze local physics so Balls.move_balls() doesn't fight the
            // network update, and pre-load BallState in case this peer is granted
            // authority mid-rally. Do NOT toggle gameObject.SetActive — once a
            // NetworkObject's GameObject becomes inactive, Fusion stops calling
            // FixedUpdateNetwork on it, deadlocking the proxy invisible forever.
            _ball.freeze = true;
            _ball.motion.position         = _netPos;
            _ball.motion.velocity         = _netVel;
            _ball.motion.angular_velocity = _netAngVel;
        }
    }

    // ─── Render — runs every display frame for smooth visuals ───────────────

    public override void Render()
    {
        // Always run change detection — both authority and proxies need to
        // react to SizeMultiplier updates (host applies its own change
        // immediately in SetSizeMultiplier, but this also covers the case
        // where authority changed hands between writes).
        if (_changes != null)
        {
            foreach (var n in _changes.DetectChanges(this, out _, out _))
            {
                if (n == nameof(SizeMultiplier))
                    ApplySizeLocally(SizeMultiplier);
                else if (n == nameof(SpeedMultiplier))
                    ApplySpeedLocally(SpeedMultiplier);
            }
        }

        if (Object.HasStateAuthority)
        {
            return;
        }

        // Read the snapshot once. Reading [Networked] properties multiple times
        // inside Render can return drift-between-ticks on this prefab's settings
        // (Interpolation Data: None, Simulation Only) — cache locally instead.
        Vector3 netPos = _netPos;
        Vector3 netVel = _netVel;

        // Velocity-extrapolate by half a tick so the proxy doesn't visibly trail
        // behind the authority by the network round-trip + tick interval.
        Vector3 target = netPos + netVel * (Runner.DeltaTime * 0.5f);

        float dist = Vector3.Distance(transform.position, target);

        transform.position = (dist > _snapThreshold)
            ? target
            : Vector3.Lerp(transform.position, target, Time.deltaTime * _proxyCorrectSpeed);

        float angDeg = Mathf.Rad2Deg * _netAngVel.magnitude * Time.deltaTime;
        if (angDeg > 0.01f)
            transform.rotation = Quaternion.AngleAxis(angDeg, _netAngVel.normalized) * transform.rotation;

        if ((_renderTickCount++ % 90) == 0) {
            var rend = GetComponentInChildren<Renderer>();
            Debug.Log($"[NetBall] PROXY Render id={Object.Id} netPos={netPos} " +
                      $"netVel={netVel} target={target} transform.pos={transform.position} " +
                      $"active={gameObject.activeSelf} " +
                      $"rendererEnabled={(rend != null ? rend.enabled.ToString() : "NULL")}");
        }
    }

    // ─── Authority Transfer API (called by MultiplayerBridge) ────────────────

    /// <summary>
    /// Called when the LOCAL player's paddle strikes this ball.
    /// Requests physics authority so this player simulates the ball locally
    /// for zero-latency impact response.
    /// </summary>
    public void RequestAuthorityOnHit()
    {
        // Guard against a Ball that was Object.Instantiate'd locally (e.g. when
        // Balls.network_ball_prefab is unwired). Its NetworkObject is inert and
        // Object.HasStateAuthority would NRE inside Fusion.
        if (Object == null || !Object.IsValid) return;
        if (Object.HasStateAuthority) return;

        _ball.freeze = false;
        _ball.motion.position         = _netPos;
        _ball.motion.velocity         = _netVel;
        _ball.motion.angular_velocity = _netAngVel;

        Object.RequestStateAuthority();
    }

    /// <summary>
    /// Called on the HOST when the remote player's paddle hits the ball.
    /// Fusion 2 has no AssignStateAuthority — the server uses an RPC with [RpcTarget]
    /// to tell exactly one client to call RequestStateAuthority() on their machine.
    /// </summary>
    public void AssignAuthorityToPlayer(PlayerRef player)
    {
        if (Object == null || !Object.IsValid) return;
        if (Runner != null && Runner.IsServer)
            RPC_TakeStateAuthority(player);
    }

    // [RpcTarget] routes this RPC to the single PlayerRef passed as the first argument.
    // Only that peer's machine executes the method body. Fusion 2 requires the
    // outer attribute to specify RpcTargets.All when [RpcTarget] is in use — the
    // runtime then narrows delivery to the targeted player. This is the documented
    // pattern; do not change to RpcTargets.Proxies (it disables [RpcTarget] routing).
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_TakeStateAuthority([RpcTarget] PlayerRef target)
    {
        RequestAuthorityOnHit();
    }

    /// <summary>
    /// Called when a new serve starts, returning authority to the Host so the
    /// serve position is always controlled centrally.
    /// </summary>
    public void ReleaseAuthorityToHost()
    {
        if (Object == null || !Object.IsValid) return;
        if (!Object.HasStateAuthority) return;
        if (!Runner.IsServer)
            Object.ReleaseStateAuthority();
    }
}
