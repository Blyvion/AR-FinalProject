using UnityEngine;

public abstract class PowerUpEffect : ScriptableObject
{
    [Tooltip("Stable id used in logs/telemetry. Don't change after release.")]
    public byte effectId;

    [Tooltip("0 = permanent until externally reset (e.g. on next serve).")]
    public float durationSeconds = 0f;

    [Tooltip("Local-only VFX prefab spawned on every peer when the effect lands.")]
    public GameObject pickupVfxPrefab;

    public abstract void Apply(NetworkedBall ball);
    public virtual  void Revert(NetworkedBall ball) { }
}
