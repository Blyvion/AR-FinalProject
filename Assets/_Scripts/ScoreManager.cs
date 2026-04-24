using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    [Header("Score Tracking")]
    public int player1Score = 0;
    public int player2Score = 0;

    [Header("References")]
    public GameObject ball;

    // --- NEW PADDLE REFERENCES ---
    public PaddleController paddle1;
    public PaddleController paddle2;

    private Vector3 ballStartPosition;

    [Header("Settings")]
    public float launchForce = 5f;
    public float resetDelay = 2f;

    void Start()
    {
        if (ball != null)
        {
            ballStartPosition = ball.transform.position;
        }

        Invoke("LaunchBall", resetDelay);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            if (gameObject.CompareTag("Goal1"))
            {
                player2Score++;
                Debug.Log("Goal for Player 2! Score: P1: " + player1Score + " | P2: " + player2Score);
                ResetEverything(); // Changed to reset everything
            }
            else if (gameObject.CompareTag("Goal2"))
            {
                player1Score++;
                Debug.Log("Goal for Player 1! Score: P1: " + player1Score + " | P2: " + player2Score);
                ResetEverything(); // Changed to reset everything
            }
            else if (gameObject.name.Contains("Floor"))
            {
                Debug.Log("Out of Bounds! Resetting ball...");
                ResetEverything(); // Changed to reset everything
            }
        }
    }

    // --- RESET LOGIC ---
    void ResetEverything()
    {
        // 1. Reset the Ball
        ball.transform.position = ballStartPosition;
        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2. Reset the Paddles
        //if (paddle1 != null) paddle1.ResetPaddle();
       // if (paddle2 != null) paddle2.ResetPaddle();

        // 3. Wait, then serve again
        Invoke("LaunchBall", resetDelay);
    }

    void LaunchBall()
    {
        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            float zDirection = Random.Range(0, 2) == 0 ? -1f : 1f;
            rb.AddForce(new Vector3(0, 0, zDirection * launchForce), ForceMode.Impulse);
        }
    }
}