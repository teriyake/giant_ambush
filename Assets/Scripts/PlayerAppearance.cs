using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class PlayerAppearance : NetworkBehaviour 
{
    [SerializeField] private Material m_hostMaterial;
    [SerializeField] private Material m_clientMaterial;

    private Renderer m_objectRenderer;

    void Awake()
    {
        m_objectRenderer = GetComponentInChildren<Renderer>();
        if (m_objectRenderer == null)
        {
            Debug.LogError("PlayerAppearance: No Renderer found on this object or its children!", this);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (m_objectRenderer == null) return;

        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            Debug.Log($"NetworkObject {NetworkObjectId} belongs to Host (ClientId {OwnerClientId}). Applying Host Material.");
            m_objectRenderer.material = m_hostMaterial;
        }
        else
        {
            Debug.Log($"NetworkObject {NetworkObjectId} belongs to Client (ClientId {OwnerClientId}). Applying Client Material.");
            m_objectRenderer.material = m_clientMaterial;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
