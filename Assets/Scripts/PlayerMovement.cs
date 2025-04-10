using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(NetworkObject))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Tracking Target")]
    [Tooltip("If null, will attempt to use Camera.main")]
    [HideInInspector] public Transform m_cameraToTrack;

    private Transform m_playerObjectTransform;
    private bool m_isTracking = false;
    private NetworkTransform m_networkTransform;

    public override void OnNetworkSpawn()
    {
        m_playerObjectTransform = transform;

        if (IsOwner)
        {
            if (PlatformRoleManager.Instance != null && PlatformRoleManager.Instance.IsPlatformReady)
            {
                Debug.Log($"PlayerMovement (Owner: {OwnerClientId}): Platform already ready. Initializing camera tracking.");
                InitializeCameraTracking();
            }
            else if (PlatformRoleManager.Instance != null)
            {
                Debug.Log($"PlayerMovement (Owner: {OwnerClientId}): Platform not ready yet. Subscribing to OnPlatformReady event.");
                PlatformRoleManager.Instance.OnPlatformReady += InitializeCameraTracking;
            }
            else
            {
                Debug.LogError($"PlayerMovement (Owner: {OwnerClientId}): PlatformRoleManager Instance not found on spawn!", this);
                m_isTracking = false;
            }

            m_networkTransform = GetComponent<NetworkTransform>();
        } 
        else 
        {
            Debug.Log($"PlayerMovementTracker (Remote: {OwnerClientId}): This is a remote player avatar. Position will be updated by NetworkTransform.");
            m_isTracking = false;
        }
    }

    private void InitializeCameraTracking()
    {
        if (!IsOwner || m_isTracking) return;

        if (m_cameraToTrack == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                m_cameraToTrack = mainCam.transform;
                Debug.Log($"PlayerMovementTracker (Owner: {OwnerClientId}): Found and tracking Camera.main: {m_cameraToTrack.gameObject.name}");
                m_isTracking = true;
            }
            else
            {
                Debug.LogError($"PlayerMovementTracker (Owner: {OwnerClientId}): Could not find main camera to track! Avatar will not follow device movement.", this);
                m_isTracking = false;
            }
        } 
        else 
        {
            Debug.Log($"PlayerMovementTracker (Owner: {OwnerClientId}): Tracking pre-assigned camera: {m_cameraToTrack.gameObject.name}");
            m_isTracking = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= InitializeCameraTracking;
        }
        base.OnNetworkDespawn();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner || !m_isTracking || m_cameraToTrack == null)
        {
            return;
        }

        Vector3 currentCamPos = m_cameraToTrack.transform.position;
        Quaternion currentCamRot = m_cameraToTrack.transform.rotation;

        UpdateServerPositionServerRpc(currentCamPos, currentCamRot);
    }

    [ServerRpc]
    private void UpdateServerPositionServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams rpcParams = default)
    {
        transform.position = position;
        transform.rotation = rotation;

        // Debug.Log($"Server Received Pose from Client {rpcParams.Receive.SenderClientId}: Applying Pos={position}, Rot={rotation.eulerAngles} to NetworkObject {NetworkObjectId}");

    }

    public override void OnDestroy()
    {
        if (IsOwner && PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= InitializeCameraTracking;
        }
        base.OnDestroy();
    }
}
