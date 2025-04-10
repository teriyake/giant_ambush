using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class InteractionController : NetworkBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private LayerMask m_interactableLayer;
    [SerializeField] private float m_maxInteractionDistance = 50f;
    [SerializeField] private Camera m_playerCamera;
    //
    // Start is called before the first frame update
    void Start()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (m_playerCamera == null)
        {
            m_playerCamera = Camera.main;
        }
        if (m_playerCamera == null)
        {
            Debug.LogError("InteractionController: Player Camera not found! Interaction will not work.", this);
            enabled = false;
        }

        if (m_interactableLayer == 0) 
        {
            Debug.LogWarning("InteractionController: Interactable Layer is not set. Interactions might fail.", this);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner || m_playerCamera == null) return;

        Pointer currentPointer = Pointer.current;
        if (currentPointer == null || !currentPointer.press.wasPressedThisFrame)
        {
            return;
        }

        Vector2 screenPosition = currentPointer.position.ReadValue();

        Ray ray = m_playerCamera.ScreenPointToRay(screenPosition);
        Debug.DrawRay(ray.origin, ray.direction * m_maxInteractionDistance, Color.yellow, 1.0f);

        if (Physics.Raycast(ray, out RaycastHit hit, m_maxInteractionDistance, m_interactableLayer))
        {
            Debug.Log($"InteractionController: Ray hit object '{hit.collider.gameObject.name}' on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

            NetworkedLevelPiece levelPiece = hit.collider.GetComponent<NetworkedLevelPiece>();
            if (levelPiece != null)
            {
                Debug.Log($"InteractionController: Hit NetworkedLevelPiece {levelPiece.NetworkObjectId}. Requesting destroy.");
                levelPiece.RequestDestroy();
            }
            else
            {
                Debug.Log($"InteractionController: Hit object '{hit.collider.gameObject.name}' does not have NetworkedLevelPiece component.");
            }
        }
        else
        {
            Debug.Log("InteractionController: Tap/click did not hit any interactable object.");
        }
        
    }
}
