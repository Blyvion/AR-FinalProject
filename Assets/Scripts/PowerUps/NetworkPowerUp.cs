using Fusion;
using UnityEngine;

// Networked floating power-up block.
//
// Authority model: the SERVER (host) owns this NetworkObject for its entire
// lifetime. Only the server runs the proximity check and only the server
// flips State / TriggerSeq. Clients are pure observers — they react to the
// networked properties via ChangeDetector. Double-trigger is impossible
// without any extra locking because no client can ever set the consumed flag.
public class NetworkPowerUp : NetworkBehaviour
{
    public enum PUState : byte { Idle, Consumed }

    [Header("Effect")]
    [SerializeField] PowerUpEffect _effect;

    [Header("Trigger")]
    [SerializeField] float _triggerRadius       = 0.15f;   // metres; ball must enter within this distance
    [Tooltip("Seconds between consume and Runner.Despawn. Kept brief — long " +
             "enough for every peer's Render() to fire the burst VFX once " +
             "before the NetworkObject disappears. Per-match respawn timing " +
             "is owned by PowerUpSpawner._respawnDelaySeconds.")]
    [SerializeField] float _despawnDelaySeconds = 0.3f;

    [Header("Visuals")]
    [SerializeField] GameObject _idleVisual;          // floating block — hidden when consumed
    [SerializeField] GameObject _triggerVfxPrefab;    // local-only burst spawned on every peer

    [Header("Movement")]
    [Tooltip("Half-amplitude of the X-axis sweep, in metres. Default 0.7 keeps the " +
             "block within the 1.524 m table width with a small margin on each side.")]
    [SerializeField] float _xRange = 0.7f;
    [Tooltip("Seconds for one full left→right→left cycle.")]
    [SerializeField] float _periodSeconds = 4f;

    // Networked state. ChangeDetector below maps property changes to local
    // visual reactions on every peer, so VFX stays synchronised without RPCs.
    [Networked] public PUState     State        { get; set; }
    [Networked]        TickTimer   DespawnTimer { get; set; }
    // OPTIMISATION: was [Networked] int TriggerSeq.
    // TriggerSeq is only ever written to signal "a trigger just happened" and
    // read by ChangeDetector — the actual numeric value is never inspected.
    // NetworkBool achieves the same toggle semantic at 1 bit instead of 32.
    // To revert: change back to [Networked] int TriggerSeq and use TriggerSeq += 1.
    [Networked]        NetworkBool TriggerSeq   { get; set; }

    // Spawn pose. Runner.Spawn(prefab, pos, rot, …) only applies pos/rot on
    // the authority's local transform; without NetworkTransform on the prefab
    // proxies would instantiate at the prefab's authored origin (0,0,0). We
    // replicate the spawn pose ourselves through these networked fields.
    [Networked] Vector3    NetSpawnPos { get; set; }
    [Networked] Quaternion NetSpawnRot { get; set; }
    // PoseValid distinguishes "host has written the pose" from "uninitialised
    // default" — the default Quaternion(0,0,0,0) is not identity, so we'd
    // otherwise have to compare against a sentinel that could legitimately
    // appear in the data.
    [Networked] NetworkBool PoseValid { get; set; }

    Balls           _ballPool;
    ChangeDetector  _changes;

    public override void Spawned()
    {
        _ballPool = FindFirstObjectByType<Balls>();
        _changes  = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (Object.HasStateAuthority)
        {
            // Capture the Runner.Spawn pose so proxies can replicate it. This
            // is the SPAWN centre — the actual transform.position oscillates
            // around it via ComputeCurrentPosition() below.
            NetSpawnPos = transform.position;
            NetSpawnRot = transform.rotation;
            PoseValid   = true;
        }

        // Snap to the deterministic position right away so we don't render a
        // single frame at the prefab's authored origin.
        if (PoseValid)
        {
            transform.position = ComputeCurrentPosition();
            transform.rotation = NetSpawnRot;
        }

        ApplyVisualState();
    }

    // Deterministic sine sweep along the world X axis around NetSpawnPos.
    // Both authority and proxies call this with Runner.SimulationTime, which
    // is tick-aligned and approximately equal across peers, so they agree on
    // the position without needing a per-tick networked field.
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

        // Update transform on the host so the proximity check uses the live
        // moving position. Render() will overwrite this on the same frame for
        // smoother visuals at display rate, but FUN's value is what the
        // proximity check below sees.
        transform.position = ComputeCurrentPosition();

        if (State == PUState.Consumed)
        {
            // After the brief delay, despawn so PowerUpSpawner can roll a
            // fresh random variant for the next appearance.
            if (DespawnTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
            return;
        }

        if (_ballPool == null) return;

        // Sphere-vs-sphere proximity. We don't use Unity OnTriggerEnter because
        // Ball.cs runs custom analytical physics — transform.position is moved
        // outside the Rigidbody pipeline, and OnTrigger would also fire on
        // proxies (where it shouldn't). The host-only swept check is the
        // single source of truth.
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
        TriggerSeq   = !(bool)TriggerSeq;  // toggle — ChangeDetector fires on any value change
        DespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(_despawnDelaySeconds, 0.05f));

        if (_effect != null) _effect.Apply(nb);
    }

    public override void Render()
    {
        // Drive the visible transform on every peer (authority and proxies).
        // Because every peer evaluates the same sine of Runner.SimulationTime
        // around the same NetSpawnPos, they all see the block at the same
        // place — no per-frame networked field needed.
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
        // Local-only spawn — every peer instantiates its own copy at the
        // current world position. Both peers compute the same position from
        // Runner.SimulationTime, so the bursts appear at the same place
        // without needing an RPC.
        Vector3 burstAt = transform.position;
        if (_triggerVfxPrefab != null)
            Instantiate(_triggerVfxPrefab, burstAt, Quaternion.identity);
        if (_effect != null && _effect.pickupVfxPrefab != null)
            Instantiate(_effect.pickupVfxPrefab, burstAt, Quaternion.identity);
    }
}
