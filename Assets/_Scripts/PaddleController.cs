using UnityEngine;
using UnityEngine.InputSystem;

public class PaddleController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float horizontalSpeed = 7f;
    public float depthSpeed = 6f;

    [Header("Boundary Limits")]
    public float xLimit = 1.2f;
    public float tableEnd = 2.2f;
    public float netLimit = 0.15f;

    [Header("Player Setup")]
    public int playerID = 1;

    // Storing the starting position so we can return to it exactly
    private Vector3 startPosition;

    void Start()
    {
        // 1. Calculate the initial position based on ID
        float startZ = (playerID == 1) ? 2.2f : -2.2f;

        // 2. Set the startPosition variable (using current Y height)
        startPosition = new Vector3(0, transform.position.y, startZ);

        // 3. Move the paddle to that position immediately
        transform.position = startPosition;
    }

    // --- RESET FUNCTION ---
    
    public void ResetPaddle()
    {
        transform.position = startPosition;
    }

    void Update()
    {
        float moveX = 0f;
        float moveZ = 0f;

        if (Keyboard.current != null)
        {
            if (playerID == 1) // Positive Z Side 
            {
                if (Keyboard.current.leftArrowKey.isPressed) moveX = -1f;
                else if (Keyboard.current.rightArrowKey.isPressed) moveX = 1f;

                if (Keyboard.current.upArrowKey.isPressed) moveZ = -1f; // Toward net
                else if (Keyboard.current.downArrowKey.isPressed) moveZ = 1f; // Toward end
            }
            else if (playerID == 2) // Negative Z Side (Top)
            {
                if (Keyboard.current.aKey.isPressed) moveX = -1f;
                else if (Keyboard.current.dKey.isPressed) moveX = 1f;

                if (Keyboard.current.wKey.isPressed) moveZ = 1f;  // Toward net
                else if (Keyboard.current.sKey.isPressed) moveZ = -1f; // Toward end
            }
        }

        Vector3 movement = new Vector3(moveX * horizontalSpeed, 0, moveZ * depthSpeed);
        Vector3 newPosition = transform.position + movement * Time.deltaTime;

        // Side Limits
        newPosition.x = Mathf.Clamp(newPosition.x, -xLimit, xLimit);

        // Half-Table Logic
        if (playerID == 1)
        {
            newPosition.z = Mathf.Clamp(newPosition.z, netLimit, tableEnd);
        }
        else
        {
            newPosition.z = Mathf.Clamp(newPosition.z, -tableEnd, -netLimit);
        }

        transform.position = newPosition;
    }
}