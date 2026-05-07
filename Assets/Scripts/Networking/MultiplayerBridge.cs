using Fusion;
using UnityEngine;

public class MultiplayerBridge : MonoBehaviour
{
    private NetworkRunner _runner;
    private Play          _play;
    private PlayerRef     _remotePlayer;
    private bool          _active;

public void Activate(NetworkRunner runner, Play play, PlayerRef remotePlayer)
    {
        if (_active) return;

        Debug.Log($"[Bridge] Activate runner={runner != null} play={play != null} remote={remotePlayer}");

        _runner       = runner;
        _play         = play;
        _remotePlayer = remotePlayer;
        _active       = true;

if (_play != null && _play.balls != null)
            _play.balls.set_runner(runner);

_play.on_ball_hit     += HandleBallHit;
        _play.on_score_updated += HandleScoreUpdated;
    }

    private void OnDestroy()
    {
        if (_play != null)
        {
            _play.on_ball_hit      -= HandleBallHit;
            _play.on_score_updated -= HandleScoreUpdated;
        }
    }

private void HandleScoreUpdated(int hostScoredInt, int _unused)
    {
        if (!_active || _play == null) return;
        Ball ball = _play.ball_in_play;
        if (ball == null) return;

        NetworkedBall nb = ball.GetComponent<NetworkedBall>();
        if (nb == null || nb.Object == null || !nb.Object.IsValid) return;
        if (!nb.Object.HasStateAuthority) return;

        nb.RpcSyncScore(hostScoredInt == 1);
    }

    private void HandleBallHit(Ball ball, string bouncerTag)
    {
        if (!_active || ball == null) return;

        NetworkedBall nb = ball.GetComponent<NetworkedBall>();
        if (nb == null) return;

        if (bouncerTag == "player_paddle")
        {

            nb.RequestAuthorityOnHit();
        }
        else if (bouncerTag == "robot_paddle")
        {

if (_runner != null && _runner.IsServer && !_remotePlayer.IsNone)
                nb.AssignAuthorityToPlayer(_remotePlayer);
        }
    }
}
