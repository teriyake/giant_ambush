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
    [SerializeField] private Transform m_cameraToTrack;

    [Header("Smoothing (Optional)")]
    [Tooltip("Apply smoothing to remote players' movement?")]
    [SerializeField] private bool m_smoothRemoteMovement = true;
    [Tooltip("How quickly remote players snap to their target position/rotation.")]
    [SerializeField] private float m_remoteSmoothingFactor = 8.0f;

    private Transform m_playerObjectTransform;
    private bool m_isTracking = false;
    private NetworkTransform m_networkTransform;

    public override void OnNetworkSpawn()
    {
        m_playerObjectTransform = transform;

        if (IsOwner)
        {
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

            m_networkTransform = GetComponent<NetworkTransform>();
        } 
        else 
        {
            Debug.Log($"PlayerMovementTracker (Remote: {OwnerClientId}): This is a remote player avatar. Position will be updated by NetworkTransform.");
            m_isTracking = false;
        }
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
}
