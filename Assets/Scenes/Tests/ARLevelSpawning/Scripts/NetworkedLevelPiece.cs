using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class NetworkedLevelPiece : NetworkBehaviour
{
    [Header("Visuals")]
    [SerializeField] private MeshRenderer m_meshRenderer;

    public NetworkVariable<bool> IsDestroyed = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server 
    );

    void Awake()
    {
        if (m_meshRenderer == null)
        {
            m_meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
        if (m_meshRenderer == null)
        {
            Debug.LogError("NetworkedLevelPiece: MeshRenderer not found!", this);
        }
    }

    public override void OnNetworkSpawn()
    {
        IsDestroyed.OnValueChanged += OnDestroyStateChanged;

        ApplyDestroyState(IsDestroyed.Value);

        Debug.Log($"NetworkedLevelPiece {NetworkObjectId} spawned. Initial IsDestroyed state: {IsDestroyed.Value}");
    }

    public override void OnNetworkDespawn()
    {
        IsDestroyed.OnValueChanged -= OnDestroyStateChanged;
    }

    private void OnDestroyStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"NetworkedLevelPiece {NetworkObjectId}: IsDestroyed changed from {previousValue} to {newValue}. Applying state.");
        ApplyDestroyState(newValue);
    }

    private void ApplyDestroyState(bool isDestroyed)
    {
        if (m_meshRenderer != null)
        {
            m_meshRenderer.enabled = !isDestroyed;
        }

        GetComponent<Collider>().enabled = !isDestroyed;

        if (isDestroyed) {
             Debug.Log($"NetworkedLevelPiece {NetworkObjectId} is now visually destroyed.");
        } else {
             Debug.Log($"NetworkedLevelPiece {NetworkObjectId} is now visually active.");
        }
    }

    public void RequestDestroy()
    {
        if (!IsDestroyed.Value)
        {
            Debug.Log($"NetworkedLevelPiece {NetworkObjectId}: Requesting destruction from client {NetworkManager.Singleton.LocalClientId}.");
            RequestDestroyServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDestroyServerRpc(ServerRpcParams rpcParams = default)
    {
        Debug.Log($"NetworkedLevelPiece {NetworkObjectId}: Server received destroy request from client {rpcParams.Receive.SenderClientId}.");
        if (!IsDestroyed.Value)
        {
            IsDestroyed.Value = true;
            Debug.Log($"NetworkedLevelPiece {NetworkObjectId}: Server setting IsDestroyed to true.");
        }
        else
        {
            Debug.Log($"NetworkedLevelPiece {NetworkObjectId}: Server received destroy request, but already destroyed.");
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
