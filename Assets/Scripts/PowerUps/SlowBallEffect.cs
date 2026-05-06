using UnityEngine;

[CreateAssetMenu(fileName = "SlowBallEffect", menuName = "PowerUps/Slow Ball")]
public class SlowBallEffect : PowerUpEffect
{
    [Tooltip("Fraction of normal speed while the effect is active. 0.1 = 10x slower.")]
    public float speedMultiplier = 0.1f;

    public override void Apply(NetworkedBall ball)
    {
        // Pass durationSeconds through so the ball arms an auto-revert timer;
        // when it expires the ball restores SpeedMultiplier to 1 on the host.
        ball.SetSpeedMultiplier(speedMultiplier, durationSeconds);
    }

    public override void Revert(NetworkedBall ball) => ball.SetSpeedMultiplier(1f);
}
