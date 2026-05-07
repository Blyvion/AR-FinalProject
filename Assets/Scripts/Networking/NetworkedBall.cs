using Fusion;
using UnityEngine;

[RequireComponent(typeof(Ball))]
public class NetworkedBall : NetworkBehaviour
{
    [Header("Proxy Correction Tuning")]
    [Tooltip("Velocity-extrapolated position is lerped toward this speed (m/s display units). "
           + "Lower = smoother but laggier. Higher = more responsive but jittery.")]
    [SerializeField] private float _proxyCorrectSpeed = 40f;
    [Tooltip("If the proxy position drifts beyond this distance (m), hard-snap instead of lerp.")]
    [SerializeField] private float _snapThreshold = 0.12f;
    [Tooltip("How many Fusion ticks ahead to extrapolate the proxy position from the last snapshot. "
           + "Higher values compensate for larger network RTTs; 2-3 is a good default.")]
    [SerializeField] private float _extrapolationTicks = 2.0f;

[Networked] private Vector3 _netPos    { get; set; }
    [Networked] private Vector3 _netVel    { get; set; }
    [Networked] private Vector3 _netAngVel { get; set; }

[Networked] public float SizeMultiplier { get; set; }

[Networked] public float     SpeedMultiplier  { get; set; }

[Networked]       TickTimer SpeedRevertTimer { get; set; }

[Networked] public NetworkBool GlowEnabled { get; set; }

    private Material _ballGlowMat;

private Ball           _ball;
    private Vector3        _baseLocalScale;
    private float          _baseRadius;
    private ChangeDetector _changes;

private float _pendingSizeMultiplier  = -1f;

private float _pendingSpeedMultiplier = -1f;
    private float _pendingSpeedDuration   = 0f;

public override void Spawned()
    {
        _ball = GetComponent<Ball>();

_baseLocalScale = transform.localScale;
        _baseRadius     = _ball.radius;
        _changes        = GetChangeDetector(ChangeDetector.Source.SimulationState);

if (Object.HasStateAuthority && SizeMultiplier <= 0f)
            SizeMultiplier = 1f;
        if (Object.HasStateAuthority && SpeedMultiplier <= 0f)
            SpeedMultiplier = 1f;

ApplySizeLocally (SizeMultiplier  > 0f ? SizeMultiplier  : 1f);
        ApplySpeedLocally(SpeedMultiplier > 0f ? SpeedMultiplier : 1f);
        ApplyGlowLocally(GlowEnabled);

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

_ball.radius = _baseRadius * mult;
    }

    void ApplySpeedLocally(float mult)
    {
        if (mult <= 0f) mult = 1f;

_ball.speed_multiplier = mult;
    }

    void ApplyGlowLocally(bool glow)
    {
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;
        if (_ballGlowMat == null)
            _ballGlowMat = new Material(rend.material);
        if (glow)
        {
            _ballGlowMat.EnableKeyword("_EMISSION");
            _ballGlowMat.SetColor("_EmissionColor", new Color(0f, 2f, 4f) * 3f);
            rend.material = _ballGlowMat;
        }
        else
        {
            _ballGlowMat.DisableKeyword("_EMISSION");
            _ballGlowMat.SetColor("_EmissionColor", Color.black);
            rend.material = _ballGlowMat;
        }
    }

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

if (Runner != null && Runner.IsServer)
            Object.RequestStateAuthority();

        _pendingSizeMultiplier = m;
    }

public void SetGlowEnabled(bool glow)
    {
        if (Object == null || !Object.IsValid) return;
        if (Object.HasStateAuthority)
        {
            GlowEnabled = glow;
            ApplyGlowLocally(glow);
            return;
        }
        if (Runner != null && Runner.IsServer)
            Object.RequestStateAuthority();
    }

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

public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {

            if (_pendingSizeMultiplier > 0f)
            {
                SizeMultiplier = _pendingSizeMultiplier;
                ApplySizeLocally(_pendingSizeMultiplier);
                _pendingSizeMultiplier = -1f;
            }

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

_ball.freeze = true;
            _ball.motion.position         = _netPos;
            _ball.motion.velocity         = _netVel;
            _ball.motion.angular_velocity = _netAngVel;
        }
    }

public override void Render()
    {

if (_changes != null)
        {
            foreach (var n in _changes.DetectChanges(this, out _, out _))
            {
                if (n == nameof(SizeMultiplier))
                    ApplySizeLocally(SizeMultiplier);
                else if (n == nameof(SpeedMultiplier))
                    ApplySpeedLocally(SpeedMultiplier);
                else if (n == nameof(GlowEnabled))
                    ApplyGlowLocally(GlowEnabled);
            }
        }

        if (Object.HasStateAuthority)
        {
            return;
        }

Vector3 netPos = _netPos;
        Vector3 netVel = _netVel;

Vector3 target = netPos + netVel * (Runner.DeltaTime * _extrapolationTicks);

        float dist = Vector3.Distance(transform.position, target);

        transform.position = (dist > _snapThreshold)
            ? target
            : Vector3.Lerp(transform.position, target, Time.deltaTime * _proxyCorrectSpeed);

        float angDeg = Mathf.Rad2Deg * _netAngVel.magnitude * Time.deltaTime;
        if (angDeg > 0.01f)
            transform.rotation = Quaternion.AngleAxis(angDeg, _netAngVel.normalized) * transform.rotation;

}

public void RequestAuthorityOnHit()
    {

if (Object == null || !Object.IsValid) return;
        if (Object.HasStateAuthority) return;

        _ball.freeze = false;
        _ball.motion.position         = _netPos;
        _ball.motion.velocity         = _netVel;
        _ball.motion.angular_velocity = _netAngVel;

        Object.RequestStateAuthority();
    }

public void AssignAuthorityToPlayer(PlayerRef player)
    {
        if (Object == null || !Object.IsValid) return;
        if (Runner != null && Runner.IsServer)
            RPC_TakeStateAuthority(player);
    }

[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_TakeStateAuthority([RpcTarget] PlayerRef target)
    {
        RequestAuthorityOnHit();
    }

public void ReleaseAuthorityToHost()
    {
        if (Object == null || !Object.IsValid) return;
        if (!Object.HasStateAuthority) return;
        if (!Runner.IsServer)
            Object.ReleaseStateAuthority();
    }

[Rpc(RpcSources.All, RpcTargets.All)]
    public void RpcSyncScore(NetworkBool hostScored)
    {
        var play = FindFirstObjectByType<Play>();
        if (play == null || play.is_host) return;
        play.receive_mp_point(hostScored);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RpcSyncPlayState(string stateName)
    {
        var play = FindFirstObjectByType<Play>();
        if (play == null) return;
        play.apply_remote_play_state(stateName);
    }

    // Fired by the client when it detects a scoring event (client has state authority
    // over the ball at that moment, so the host's physics never sees the collision).
    // The host is the only one that acts on this; it then syncs both scoreboards via
    // on_score_updated → RpcSyncScore.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RpcClientNotifyScore(NetworkBool clientScored)
    {
        var play = FindFirstObjectByType<Play>();
        if (play == null || !play.is_host) return;
        // 'clientScored' carries the raw player_scored value from keep_score, where
        // "player" refers to the physical HOST side (scene tags are identical on both
        // machines — no perspective flip needed).
        play.host_record_score(clientScored);
    }
}
