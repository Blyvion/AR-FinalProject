using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Attach to a persistent GameObject (e.g. "NetworkManager") in the scene.
/// Wire HostGame() and JoinGame() to your existing UI Buttons via the Inspector.
/// </summary>
public class NetworkConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public enum NetState { Disconnected, Connecting, InMatch }

    [Header("Fusion Runner Prefab")]
    [Tooltip("Drag in a prefab that has only a NetworkRunner component on it")]
    [SerializeField] private NetworkRunner _runnerPrefab;

    [Header("UI References (all optional)")]
    [SerializeField] private GameObject     _lobbyPanel;
    [SerializeField] private TMP_Text       _statusText;
    [SerializeField] private TMP_InputField _roomNameField;
    [SerializeField] private Button         _hostButton;
    [SerializeField] private Button         _joinButton;
    [SerializeField] private Button         _leaveButton;
    [SerializeField] private TMP_Text       _hostButtonLabel;
    [SerializeField] private TMP_Text       _joinButtonLabel;

    [Header("Multiplayer Spawn (was on PlayerSpawner)")]
    [SerializeField] private NetworkObject _playerAvatarPrefab;
    [SerializeField] private Transform     _sideASpawn;
    [SerializeField] private Transform     _sideBSpawn;
    [SerializeField] private GameObject    _robotRoot;
    [SerializeField] private Play          _play;
    [SerializeField] private MultiplayerBridge _bridge;
    [SerializeField] private PowerUpSpawner    _powerUpSpawner;

    private NetworkRunner _runner;
    private NetState      _state = NetState.Disconnected;
    private int           _joinCount;
    private bool          _activatedLocally;

    void Start() => ApplyState(NetState.Disconnected, "Status: Disconnected");

    void Update()
    {
        // Editor / desktop test shortcuts: H = Host, J = Join.
        var kb = Keyboard.current;
        if (kb == null || _state != NetState.Disconnected) return;
        if (kb.hKey.wasPressedThisFrame) HostGame();
        else if (kb.jKey.wasPressedThisFrame) JoinGame();
    }

    // ─── Public entry points — wire these to your UI Buttons ────────────────

    /// <summary>Creates a Host session. Bind to your "Host" button's OnClick.</summary>
    public void HostGame() => StartSession(GameMode.Host, GetRoomName());

    /// <summary>Joins an existing session. Bind to your "Join" button's OnClick.</summary>
    public void JoinGame() => StartSession(GameMode.Client, GetRoomName());

    /// <summary>Leaves the current session. Bind to a "Leave" button's OnClick.</summary>
    public async void LeaveGame()
    {
        if (_runner == null) return;
        ApplyState(NetState.Connecting, "Disconnecting…");
        await _runner.Shutdown();
    }

    // ─── Session startup ─────────────────────────────────────────────────────

    private async void StartSession(GameMode mode, string roomName)
    {
        if (_state != NetState.Disconnected) return;
        ApplyState(NetState.Connecting,
            mode == GameMode.Host ? $"Hosting '{roomName}'…" : $"Joining '{roomName}'…");

        if (_runner != null)
        {
            await _runner.Shutdown();
            _runner = null;
        }

        _runner = Instantiate(_runnerPrefab);
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        // Without a NetworkSceneManager, scene-placed NetworkObjects
        // (GameManager → PlayerSpawner/MultiplayerBridge, etc.) are NEVER registered
        // with the runner — IPlayerJoined fires on nothing, no avatars spawn, no
        // replication runs. Without a NetworkObjectProvider, Runner.Spawn cannot
        // resolve prefabs. Both must be present on the runner GameObject; we add
        // them programmatically so the bare NetworkRunner.prefab keeps working.
        var sceneManager = _runner.GetComponent<INetworkSceneManager>()
                        ?? (INetworkSceneManager)_runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        var objectProvider = _runner.GetComponent<INetworkObjectProvider>()
                          ?? (INetworkObjectProvider)_runner.gameObject.AddComponent<NetworkObjectProviderDefault>();

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode       = mode,
            SessionName    = roomName,
            PlayerCount    = 2,
            SceneManager   = sceneManager,
            ObjectProvider = objectProvider,
            // Scene intentionally omitted — passing one causes a LoadScene(Single)
            // which destroys the OVR camera rig and breaks passthrough. With no
            // Scene set, NetworkSceneManagerDefault only registers existing scene
            // network objects; it never calls LoadScene.
        });

        if (!result.Ok)
            ApplyState(NetState.Disconnected, $"Failed: {result.ShutdownReason}");
    }

    // ─── State machine ──────────────────────────────────────────────────────

    private void ApplyState(NetState s, string message)
    {
        _state = s;
        if (_statusText) _statusText.text = message;
        Debug.Log($"[Net] {message}");

        bool idle    = s == NetState.Disconnected;
        bool inMatch = s == NetState.InMatch;

        if (_hostButton)      _hostButton.interactable  = idle;
        if (_joinButton)      _joinButton.interactable  = idle;
        if (_leaveButton)     _leaveButton.interactable = inMatch;
        if (_hostButtonLabel) _hostButtonLabel.text     = idle ? "Host" : "Hosting…";
        if (_joinButtonLabel) _joinButtonLabel.text     = idle ? "Join" : "Joining…";
    }

    private string GetRoomName() =>
        (_roomNameField != null && !string.IsNullOrWhiteSpace(_roomNameField.text))
            ? _roomNameField.text.Trim()
            : "PingPong1v1";

    // ─── INetworkRunnerCallbacks ─────────────────────────────────────────────

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        int count = runner.ActivePlayers.Count();
        Debug.Log($"[Net] OnPlayerJoined player={player} totalPlayers={count} isServer={runner.IsServer}");

        ApplyState(NetState.InMatch, runner.IsServer
            ? $"In Match — {count}/2 players"
            : "In Match");

        if (count >= 2 && _lobbyPanel) _lobbyPanel.SetActive(false);

        if (runner.IsServer && _playerAvatarPrefab != null)
        {
            bool isSideA = (_joinCount == 0);
            _joinCount++;
            Transform spawn = isSideA ? _sideASpawn : _sideBSpawn;

            if (spawn == null)
            {
                Debug.LogError($"[Net] Spawn point missing (sideA={_sideASpawn}, sideB={_sideBSpawn}) — cannot spawn avatar.");
            }
            else
            {
                NetworkObject avatar = runner.Spawn(
                    _playerAvatarPrefab, spawn.position, spawn.rotation, inputAuthority: player);
                runner.SetPlayerObject(player, avatar);
                Debug.Log($"[Net] HOST spawned avatar id={avatar.Id} side={(isSideA ? "A" : "B")} for {player}");
            }
        }

        if (count >= 2 && !_activatedLocally)
        {
            ActivateMultiplayerLocal(runner);
            _activatedLocally = true;
        }
    }

    private void ActivateMultiplayerLocal(NetworkRunner runner)
    {
        Debug.Log($"[Net] ActivateMultiplayerLocal isServer={runner.IsServer} localPlayer={runner.LocalPlayer}");
        if (_robotRoot != null) _robotRoot.SetActive(false);
        if (_play     != null)
        {
            _play.is_multiplayer = true;
            _play.is_host        = runner.IsServer;
            _play.start_game();   // reset scores to 0 and set playing_game = true
        }

        // Move THIS peer's VR rig to its assigned side of the table. Until now the
        // spawn points only positioned the network avatar; the player's real camera
        // and hands stayed at the scene default (side A), which is why the client
        // could not see their own paddle and the host could not see the client's.
        Transform mySpawn = runner.IsServer ? _sideASpawn : _sideBSpawn;
        if (_play != null && _play.vr_camera != null && mySpawn != null)
        {
            _play.vr_camera.transform.position = mySpawn.position;

            // Orient the rig toward the world origin (table center) regardless of
            // how the spawn Transform was rotated in the scene. Without this, a
            // sideBSpawn left at default (0,0,0) rotation puts the client at
            // (0,0,+1.8) looking +Z — i.e. away from the table — and the
            // host-served ball renders behind the client's head, invisible.
            Vector3 toCenter = -mySpawn.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.0001f)
                _play.vr_camera.transform.rotation = Quaternion.LookRotation(toCenter, Vector3.up);
            else
                _play.vr_camera.transform.rotation = mySpawn.rotation;

            Debug.Log($"[Net] Moved local VR rig to side {(runner.IsServer ? "A" : "B")} " +
                      $"pos={mySpawn.position} rotY={_play.vr_camera.transform.rotation.eulerAngles.y} (auto-faced table)");
        }

        if (_bridge != null)
        {
            PlayerRef remote = default;
            foreach (var p in runner.ActivePlayers)
                if (p != runner.LocalPlayer) { remote = p; break; }
            _bridge.Activate(runner, _play, remote);
            Debug.Log($"[Net] Bridge activated, remote={remote}");
        }

        // Switch the ball pool to multiplayer mode on EVERY peer. This hides the
        // local-only scene ball + any clones Play.Start may have made before
        // is_multiplayer flipped, so the pool only ever holds Runner.Spawn'd balls.
        if (_play != null && _play.balls != null)
            _play.balls.enter_multiplayer();

        // HOST: pre-spawn enough networked balls for the rally rotation. Each
        // Runner.Spawn replicates to the client and fires register_remote_ball
        // there, populating both pools symmetrically.
        if (runner.IsServer && _play != null && _play.balls != null)
        {
            if (_play.balls.network_ball_prefab == null)
            {
                Debug.LogError("[Net] FATAL: Balls.network_ball_prefab is NOT WIRED. " +
                               "Drag Assets/Prefabs/ball.prefab into the 'Network Ball Prefab' " +
                               "slot on the Balls component. Until then no networked balls exist.");
            }
            else
            {
                _play.balls.prespawn_networked_balls(_play.balls.ball_count);

                // Spawn the power-up block(s) once, host-side.
                if (_powerUpSpawner != null)
                    _powerUpSpawner.SpawnIfHost(runner);

                // Hand the host the first networked ball to begin serving.
                Ball first = _play.balls.new_ball();
                if (first != null)
                {
                    _play.ball_in_play = first;
                    _play.free_hand.hold_ball(first);
                    Debug.Log($"[Net] HOST holds first networked ball name={first.name}");
                }
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) =>
        ApplyState(NetState.InMatch, $"Player {player} left the session.");

    public void OnConnectedToServer(NetworkRunner runner) =>
        ApplyState(NetState.InMatch, "Status: Connected");
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) =>
        ApplyState(NetState.Disconnected, $"Disconnected: {reason}");
    public void OnConnectFailed(NetworkRunner runner, NetAddress addr, NetConnectFailedReason reason) =>
        ApplyState(NetState.Disconnected, $"Connection failed: {reason}");
    public void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        _runner = null;
        _joinCount = 0;
        _activatedLocally = false;
        ApplyState(NetState.Disconnected, $"Status: Disconnected ({reason})");
        if (_lobbyPanel) _lobbyPanel.SetActive(true);
    }

    // Required interface stubs (no-op)
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // A scene reload (e.g. from a future SceneManager) recreates the OVR camera rig,
        // resetting clearFlags and backgroundColor to their serialized defaults.
        // Re-applying the passthrough toggle restores the correct camera state.
        var settings = FindObjectOfType<SettingsUI>();
        if (settings == null) return;
        var play = FindObjectOfType<Play>();
        if (play != null)
            play.enable_show_room(settings.show_room.isOn);
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}
