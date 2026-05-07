using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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

        var kb = Keyboard.current;
        if (kb == null || _state != NetState.Disconnected) return;
        if (kb.hKey.wasPressedThisFrame) HostGame();
        else if (kb.jKey.wasPressedThisFrame) JoinGame();
    }

public void HostGame() => StartSession(GameMode.Host, GetRoomName());

public void JoinGame() => StartSession(GameMode.Client, GetRoomName());

public async void LeaveGame()
    {
        if (_runner == null) return;
        ApplyState(NetState.Connecting, "Disconnecting…");
        await _runner.Shutdown();
    }

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

});

        if (!result.Ok)
            ApplyState(NetState.Disconnected, $"Failed: {result.ShutdownReason}");
    }

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
            _play.start_game();
        }

Transform mySpawn = runner.IsServer ? _sideASpawn : _sideBSpawn;
        if (_play != null && _play.vr_camera != null && mySpawn != null)
        {
            _play.vr_camera.transform.position = mySpawn.position;

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

if (_play != null && _play.balls != null)
            _play.balls.enter_multiplayer();

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

if (_powerUpSpawner != null)
                    _powerUpSpawner.SpawnIfHost(runner);

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
