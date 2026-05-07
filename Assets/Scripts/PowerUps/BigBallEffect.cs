using UnityEngine;

[CreateAssetMenu(fileName = "GlowBallEffect", menuName = "PowerUps/Glow Ball")]
public class GlowBallEffect : PowerUpEffect
{
    public override void Apply(NetworkedBall ball)  => ball.SetGlowEnabled(true);
    public override void Revert(NetworkedBall ball) => ball.SetGlowEnabled(false);
}
