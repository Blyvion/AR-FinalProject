using Fusion;
using UnityEngine;

public class PowerUpSpawner : MonoBehaviour
{
    [SerializeField] NetworkObject _powerUpPrefab;
    [SerializeField] Transform     _anchor;

    [Tooltip("List of variants — one is chosen at random on EVERY spawn " +
             "(initial spawn and every respawn). Leave empty to always use " +
             "_powerUpPrefab.")]
    [SerializeField] NetworkObject[] _variants;

    [Tooltip("Seconds between a power-up's despawn and the next random spawn. " +
             "This is the gap players see with no block on the table.")]
    [SerializeField] float _respawnDelaySeconds = 8f;

    NetworkRunner _runner;
    NetworkObject _current;
    float         _despawnedAt = -1f;
    bool          _initialSpawnDone;

    public void SpawnIfHost(NetworkRunner runner)
    {
        if (runner == null || !runner.IsServer) return;
        _runner = runner;
        if (_initialSpawnDone) return;
        SpawnNow();
        _initialSpawnDone = true;
    }

    void Update()
    {
        if (_runner == null || !_runner.IsServer) return;

bool alive = _current != null && _current.IsValid;
        if (alive)
        {
            _despawnedAt = -1f;
            return;
        }

        if (_despawnedAt < 0f)
            _despawnedAt = Time.time;

        if (Time.time - _despawnedAt >= _respawnDelaySeconds)
        {
            SpawnNow();
            _despawnedAt = -1f;
        }
    }

    void SpawnNow()
    {
        if (_anchor == null)
        {
            Debug.LogWarning("[PowerUpSpawner] _anchor is null — power-up not spawned.");
            return;
        }

        NetworkObject prefab = (_variants != null && _variants.Length > 0)
            ? _variants[Random.Range(0, _variants.Length)]
            : _powerUpPrefab;

        if (prefab == null)
        {
            Debug.LogWarning("[PowerUpSpawner] No prefab wired — power-up not spawned.");
            return;
        }

        _current = _runner.Spawn(prefab, _anchor.position, _anchor.rotation,
                                 inputAuthority: PlayerRef.None);
        Debug.Log($"[PowerUpSpawner] HOST spawned power-up id={_current.Id} " +
                  $"prefab={prefab.name} at {_anchor.position}");
    }
}
