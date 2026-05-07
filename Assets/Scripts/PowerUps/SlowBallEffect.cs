using UnityEngine;

[CreateAssetMenu(fileName = "SlowBallEffect", menuName = "PowerUps/Slow Ball")]
public class SlowBallEffect : PowerUpEffect
{
    [Tooltip("Fraction of normal speed while the effect is active. 0.1 = 10x slower.")]
    public float speedMultiplier = 0.1f;

    public override void Apply(NetworkedBall ball)
    {

ball.SetSpeedMultiplier(speedMultiplier, durationSeconds);
    }

    public override void Revert(NetworkedBall ball) => ball.SetSpeedMultiplier(1f);
}
