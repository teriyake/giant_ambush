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

    private bool m_canInteract = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (PlatformRoleManager.Instance != null && PlatformRoleManager.Instance.IsPlatformReady)
        {
            Debug.Log($"InteractionController (Owner: {OwnerClientId}): Platform already ready. Initializing interaction camera.");
            InitializeInteractionCamera();
        }
        else if (PlatformRoleManager.Instance != null)
        {
            Debug.Log($"InteractionController (Owner: {OwnerClientId}): Platform not ready yet. Subscribing to OnPlatformReady event.");
            PlatformRoleManager.Instance.OnPlatformReady += InitializeInteractionCamera;
        }
        else
        {
             Debug.LogError($"InteractionController (Owner: {OwnerClientId}): PlatformRoleManager Instance not found on spawn!", this);
             m_canInteract = false;
        }
    }

    private void InitializeInteractionCamera()
    {
        if (!IsOwner || m_canInteract) return;

        Debug.Log($"InteractionController (Owner: {OwnerClientId}): InitializeInteractionCamera called.");

        if (m_playerCamera == null)
        {
            m_playerCamera = Camera.main;
            if (m_playerCamera != null)
            {
                Debug.Log($"InteractionController (Owner: {OwnerClientId}): Found Camera.main: {m_playerCamera.name}", m_playerCamera.gameObject);
            }
        }
        else 
        {
            Debug.Log($"InteractionController (Owner: {OwnerClientId}): Using pre-assigned camera: {m_playerCamera.name}");
        }

        if (m_playerCamera == null)
        {
            Debug.LogError($"InteractionController (Owner: {OwnerClientId}): Player Camera not found AFTER platform ready! Interaction will not work.", this);
            m_canInteract = false;
            enabled = false; 
        }
        else
        {
            m_canInteract = true;
            if (m_interactableLayer == 0) 
            {
                Debug.LogWarning("InteractionController: Interactable Layer is not set.", this);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= InitializeInteractionCamera;
            Debug.Log($"InteractionController (Owner: {OwnerClientId}): Unsubscribed from OnPlatformReady.");
        }
        base.OnNetworkDespawn();
    }

    // Start is called before the first frame update
    void Start() {  }

    // Update is called once per frame
    void Update()
    {
        if (!m_canInteract || m_playerCamera == null) return;

        Pointer currentPointer = Pointer.current;
        if (currentPointer == null || !currentPointer.press.wasPressedThisFrame) return;

        Vector2 screenPosition = currentPointer.position.ReadValue();

        Ray ray = m_playerCamera.ScreenPointToRay(screenPosition);
        Debug.DrawRay(ray.origin, ray.direction * m_maxInteractionDistance, Color.yellow, 1.0f);

        if (Physics.Raycast(ray, out RaycastHit hit, m_maxInteractionDistance, m_interactableLayer))
        {
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

    public override void OnDestroy()
    {
        if (IsOwner && PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= InitializeInteractionCamera;
        }
        base.OnDestroy();
    }
}
