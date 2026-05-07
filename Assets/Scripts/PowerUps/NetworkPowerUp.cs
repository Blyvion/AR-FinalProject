using Fusion;
using UnityEngine;

public class NetworkPowerUp : NetworkBehaviour
{
    public enum PUState : byte { Idle, Consumed }

    [Header("Effect")]
    [SerializeField] PowerUpEffect _effect;

    [Header("Trigger")]
    [SerializeField] float _triggerRadius       = 0.15f;
    [Tooltip("Seconds between consume and Runner.Despawn. Kept brief — long " +
             "enough for every peer's Render() to fire the burst VFX once " +
             "before the NetworkObject disappears. Per-match respawn timing " +
             "is owned by PowerUpSpawner._respawnDelaySeconds.")]
    [SerializeField] float _despawnDelaySeconds = 0.3f;

    [Header("Visuals")]
    [SerializeField] GameObject _idleVisual;
    [SerializeField] GameObject _triggerVfxPrefab;

    [Header("Movement")]
    [Tooltip("Half-amplitude of the X-axis sweep, in metres. Default 0.7 keeps the " +
             "block within the 1.524 m table width with a small margin on each side.")]
    [SerializeField] float _xRange = 0.7f;
    [Tooltip("Seconds for one full left→right→left cycle.")]
    [SerializeField] float _periodSeconds = 4f;

[Networked] public PUState   State        { get; set; }
    [Networked]        TickTimer DespawnTimer { get; set; }
    [Networked]        int       TriggerSeq   { get; set; }

[Networked] Vector3    NetSpawnPos { get; set; }
    [Networked] Quaternion NetSpawnRot { get; set; }

[Networked] NetworkBool PoseValid { get; set; }

    Balls           _ballPool;
    ChangeDetector  _changes;

    public override void Spawned()
    {
        _ballPool = FindFirstObjectByType<Balls>();
        _changes  = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (Object.HasStateAuthority)
        {

NetSpawnPos = transform.position;
            NetSpawnRot = transform.rotation;
            PoseValid   = true;
        }

if (PoseValid)
        {
            transform.position = ComputeCurrentPosition();
            transform.rotation = NetSpawnRot;
        }

        ApplyVisualState();
    }

Vector3 ComputeCurrentPosition()
    {
        if (!PoseValid) return transform.position;
        float t      = (float)Runner.SimulationTime;
        float phase  = (_periodSeconds > 0f) ? (t * 2f * Mathf.PI / _periodSeconds) : 0f;
        float offset = Mathf.Sin(phase) * _xRange;
        return new Vector3(NetSpawnPos.x + offset, NetSpawnPos.y, NetSpawnPos.z);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

transform.position = ComputeCurrentPosition();

        if (State == PUState.Consumed)
        {

if (DespawnTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
            return;
        }

        if (_ballPool == null) return;

Vector3 myPos = transform.position;
        foreach (var b in _ballPool.AllBalls)
        {
            if (b == null || !b.gameObject.activeSelf) continue;
            var nb = b.GetComponent<NetworkedBall>();
            if (nb == null || nb.Object == null || !nb.Object.IsValid) continue;

            float reach = _triggerRadius + b.radius;
            if ((b.motion.position - myPos).sqrMagnitude <= reach * reach)
            {
                Trigger(nb);
                break;
            }
        }
    }

    void Trigger(NetworkedBall nb)
    {
        State        = PUState.Consumed;
        TriggerSeq  += 1;
        DespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(_despawnDelaySeconds, 0.05f));

        if (_effect != null) _effect.Apply(nb);
    }

    public override void Render()
    {

if (PoseValid)
        {
            transform.position = ComputeCurrentPosition();
            transform.rotation = NetSpawnRot;
        }

        foreach (var n in _changes.DetectChanges(this, out _, out _))
        {
            switch (n)
            {
                case nameof(State):      ApplyVisualState(); break;
                case nameof(TriggerSeq): OnTriggerPulse();   break;
            }
        }
    }

    void ApplyVisualState()
    {
        if (_idleVisual != null) _idleVisual.SetActive(State == PUState.Idle);
    }

    void OnTriggerPulse()
    {

Vector3 burstAt = transform.position;
        if (_triggerVfxPrefab != null)
            Instantiate(_triggerVfxPrefab, burstAt, Quaternion.identity);
        if (_effect != null && _effect.pickupVfxPrefab != null)
            Instantiate(_effect.pickupVfxPrefab, burstAt, Quaternion.identity);
    }
}
