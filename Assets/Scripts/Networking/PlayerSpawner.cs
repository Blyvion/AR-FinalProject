using UnityEngine;

// Spawning has been moved to NetworkConnectionManager.OnPlayerJoined to avoid
// relying on a scene-placed NetworkObject when StartGameArgs.Scene is omitted.
// This component is kept only so existing scene references resolve cleanly.
public class PlayerSpawner : MonoBehaviour { }
