using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerMovement : NetworkBehaviour
{
    public float speed = 5f;
    
    public override void OnNetworkSpawn()
    {
    	if(!IsOwner)
    	{
		enabled = false;
    		return;
    	}
    }

    void Update()
    {
        // Get horizontal and vertical input (WASD or Arrow keys)
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // Create a movement vector
        Vector3 move = new Vector3(moveX, 0, moveZ);

        // Apply movement to the object's position
        transform.Translate(move * speed * Time.deltaTime);
    }
}
