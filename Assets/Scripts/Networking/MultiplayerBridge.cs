using Fusion;
using UnityEngine;

/// <summary>
/// Bridges the Fusion-agnostic Play.cs with the networking layer.
/// This keeps Fusion types out of Play.cs entirely.
///
/// Listens to Play.on_ball_hit callbacks and:
///   • When the LOCAL player hits: calls NetworkedBall.RequestAuthorityOnHit()
///   • When the REMOTE player hits (host-side): assigns authority to the remote client
/// </summary>
public class MultiplayerBridge : MonoBehaviour
{
    private NetworkRunner _runner;
    private Play          _play;
    private PlayerRef     _remotePlayer;
    private bool          _active;

    /// <summary>Called by PlayerSpawner when session is full (2 players connected).</summary>
    public void Activate(NetworkRunner runner, Play play, PlayerRef remotePlayer)
    {
        if (_active) return;

        Debug.Log($"[Bridge] Activate runner={runner != null} play={play != null} remote={remotePlayer}");

        _runner       = runner;
        _play         = play;
        _remotePlayer = remotePlayer;
        _active       = true;

        // Hand the runner to Balls so new_ball() can route through Runner.Spawn.
        if (_play != null && _play.balls != null)
            _play.balls.set_runner(runner);

        // Subscribe to the play's ball-hit event (see Play.cs changes below)
        _play.on_ball_hit += HandleBallHit;
    }

    private void OnDestroy()
    {
        if (_play != null)
            _play.on_ball_hit -= HandleBallHit;
    }

    // ─── Event Handler ───────────────────────────────────────────────────────

    private void HandleBallHit(Ball ball, string bouncerTag)
    {
        if (!_active || ball == null) return;

        NetworkedBall nb = ball.GetComponent<NetworkedBall>();
        if (nb == null) return;

        if (bouncerTag == "player_paddle")
        {
            // Local player hit the ball — this player should own the physics
            nb.RequestAuthorityOnHit();
        }
        else if (bouncerTag == "robot_paddle")
        {
            // The remote player's paddle hit the ball on the HOST's simulation.
            // PlayerRef.IsValid does not exist in Fusion 2 — use IsNone instead.
            if (_runner != null && _runner.IsServer && !_remotePlayer.IsNone)
                nb.AssignAuthorityToPlayer(_remotePlayer);
        }
    }
}
