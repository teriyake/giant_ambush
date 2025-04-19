using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Tracking Target")]
    [Tooltip("If null, will attempt to use Camera.main")]
    [HideInInspector]
    public Transform m_cameraToTrack;

    private Rigidbody m_rigidbody;
    private NetworkTransform m_networkTransform;

    private Vector3 serverTargetPosition;
    private Quaternion serverTargetRotation;
    private bool receivedNewTarget = false;

    public override void OnNetworkSpawn()
    {
        m_rigidbody = GetComponent<Rigidbody>();
        if (m_rigidbody == null)
        {
            Debug.LogError($"PlayerMovement {OwnerClientId}: Rigidbody component not found!", this);
            enabled = false;
            return;
        }

        if (IsOwner)
        {
            m_rigidbody.isKinematic = true;

            if (
                PlatformRoleManager.Instance != null
                && PlatformRoleManager.Instance.IsPlatformReady
            )
            {
                Debug.Log(
                    $"PlayerMovement (Owner: {OwnerClientId}): Platform already ready. Initializing camera tracking."
                );
                InitializeCameraTracking();
            }
            else if (PlatformRoleManager.Instance != null)
            {
                Debug.Log(
                    $"PlayerMovement (Owner: {OwnerClientId}): Platform not ready yet. Subscribing to OnPlatformReady event."
                );
                PlatformRoleManager.Instance.OnPlatformReady += InitializeCameraTracking;
            }
            else
            {
                Debug.LogError(
                    $"PlayerMovement (Owner: {OwnerClientId}): PlatformRoleManager Instance not found on spawn!",
                    this
                );
            }
        }
        else
        {
            m_rigidbody.isKinematic = !IsServer;
            Debug.Log(
                $"PlayerMovement (Remote/Server: {OwnerClientId}, IsServer: {IsServer}): Rigidbody IsKinematic set to {!IsServer}"
            );
        }

        if (IsServer)
        {
            serverTargetPosition = transform.position;
            serverTargetRotation = transform.rotation;
        }
    }

    private void InitializeCameraTracking()
    {
        if (!IsOwner)
            return;

        if (m_cameraToTrack == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                m_cameraToTrack = mainCam.transform;
                Debug.Log(
                    $"PlayerMovementTracker (Owner: {OwnerClientId}): Found and tracking Camera.main: {m_cameraToTrack.gameObject.name}"
                );
            }
            else
            {
                Debug.LogError(
                    $"PlayerMovementTracker (Owner: {OwnerClientId}): Could not find main camera to track! Avatar will not follow device movement.",
                    this
                );
            }
        }
        else
        {
            Debug.Log(
                $"PlayerMovementTracker (Owner: {OwnerClientId}): Tracking pre-assigned camera: {m_cameraToTrack.gameObject.name}"
            );
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

    void Update()
    {
        if (!IsOwner || m_cameraToTrack == null)
        {
            return;
        }

        Vector3 currentCamPos = m_cameraToTrack.position;
        Quaternion currentCamRot = m_cameraToTrack.rotation;

        UpdateServerTargetStateServerRpc(currentCamPos, currentCamRot);
    }

    [ServerRpc]
    private void UpdateServerTargetStateServerRpc(
        Vector3 targetPosition,
        Quaternion targetRotation,
        ServerRpcParams rpcParams = default
    )
    {
        serverTargetPosition = targetPosition;
        serverTargetRotation = targetRotation;
        receivedNewTarget = true;
    }

    void FixedUpdate()
    {
        if (!IsServer)
        {
            return;
        }

        if (receivedNewTarget)
        {
            m_rigidbody.MovePosition(serverTargetPosition);
            m_rigidbody.MoveRotation(serverTargetRotation);

            // Debug.Log($"Server FixedUpdate: Moving Rigidbody for {OwnerClientId} towards Pos: {serverTargetPosition}");

            receivedNewTarget = false;
        }

        // Vector3 newPos = Vector3.MoveTowards(m_rigidbody.position, serverTargetPosition, Time.fixedDeltaTime * moveSpeed); // Define moveSpeed
        // Quaternion newRot = Quaternion.RotateTowards(m_rigidbody.rotation, serverTargetRotation, Time.fixedDeltaTime * rotationSpeed); // Define rotationSpeed
        // m_rigidbody.MovePosition(newPos);
        // m_rigidbody.MoveRotation(newRot);
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
