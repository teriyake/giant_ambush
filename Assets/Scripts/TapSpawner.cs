using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class TapSpawner : NetworkBehaviour
{
    [Header("Spawning")]
    [SerializeField] private GameObject m_objectToSpawnPrefab;
    [Header("AR Setup")]
    [SerializeField] private ARRaycastManager m_raycastManager;

    private List<ARRaycastHit> m_raycastHits = new List<ARRaycastHit>();

    // Start is called before the first frame update
    void Start()
    {
        if (m_raycastManager == null)
        {
            m_raycastManager = FindObjectOfType<ARRaycastManager>();
        }

        if (m_raycastManager == null)
        {
            Debug.LogError("PlayerTapSpawner: ARRaycastManager not found in the scene!", this);
        }

        if (m_objectToSpawnPrefab == null)
        {
            Debug.LogError("PlayerTapSpawner: Object To Spawn Prefab is not assigned!", this);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner) return;

        if (m_raycastManager == null || m_objectToSpawnPrefab == null) return;

        Pointer currentPointer = Pointer.current;

        if (currentPointer == null) {
            return;
        }

        if (currentPointer != null && currentPointer.press.wasPressedThisFrame)
        {
            Vector2 screenPosition = currentPointer.position.ReadValue();

            if (m_raycastManager.Raycast(screenPosition, m_raycastHits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
            {
                Pose hitPose = m_raycastHits[0].pose;
                RequestSpawnObjectServerRpc(hitPose.position, hitPose.rotation);
                Debug.Log($"Local player pointer press detected. Requesting spawn at {hitPose.position}");
            }
            else
            {
                 Debug.Log("Pointer press did not hit an AR plane.");
            }
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestSpawnObjectServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams serverRpcParams = default)
    {
        if (m_objectToSpawnPrefab == null)
        {
             Debug.LogError("ServerRpc: Cannot spawn, objectToSpawnPrefab is null!");
             return;
        }

        ulong requestingClientId = serverRpcParams.Receive.SenderClientId;
        Debug.Log($"Server received spawn request from ClientId: {requestingClientId} at position {position}");

        GameObject spawnedObject = Instantiate(m_objectToSpawnPrefab, position, rotation);

        NetworkObject networkObject = spawnedObject.GetComponent<NetworkObject>();

        if (networkObject == null)
        {
            Debug.LogError("ServerRpc: Spawned object is missing NetworkObject component!");
            Destroy(spawnedObject);
            return;
        }

        networkObject.SpawnWithOwnership(requestingClientId, true);

        Debug.Log($"Server spawned object {networkObject.NetworkObjectId} and assigned ownership to ClientId: {requestingClientId}");
    }
}
