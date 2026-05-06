using UnityEngine;

[CreateAssetMenu(fileName = "BigBallEffect", menuName = "PowerUps/Big Ball")]
public class BigBallEffect : PowerUpEffect
{
    public float sizeMultiplier = 10f;

    public override void Apply(NetworkedBall ball)  => ball.SetSizeMultiplier(sizeMultiplier);
    public override void Revert(NetworkedBall ball) => ball.SetSizeMultiplier(1f);
}
