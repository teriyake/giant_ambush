using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Tracking Target")]
    [Tooltip("If null, will attempt to use Camera.main")]
    [HideInInspector]
    public Transform m_cameraToTrack;

    [SerializeField]
    private GameObject antTrailVisualObject;
    private AntTrailController antTrailController;
    private bool _hasInitializedTrail = false;

    private Transform m_playerObjectTransform;
    private bool m_isTracking = false;
    private NetworkTransform m_networkTransform;

    public override void OnNetworkSpawn()
    {
        m_playerObjectTransform = transform;

        if (IsOwner)
        {
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
                m_isTracking = false;
            }

            m_networkTransform = GetComponent<NetworkTransform>();
        }
        else
        {
            Debug.Log(
                $"PlayerMovementTracker (Remote: {OwnerClientId}): This is a remote player avatar. Position will be updated by NetworkTransform."
            );
            m_isTracking = false;
        }
    }

    void Start()
    {
        if (antTrailVisualObject != null)
        {
            antTrailController = antTrailVisualObject.GetComponent<AntTrailController>();
            if (antTrailController == null)
            {
                Debug.LogError(
                    $"PlayerMovement ({OwnerClientId}): AntTrailController script not found on the assigned Ant Trail Visual Object!",
                    antTrailVisualObject
                );
                antTrailVisualObject.SetActive(false);
                return;
            }
            antTrailVisualObject.SetActive(false);
        }
        else
        {
            Debug.LogError(
                $"PlayerMovement ({OwnerClientId}): Ant Trail Visual Object is not assigned in the Inspector!",
                this
            );
            return;
        }

        StartCoroutine(WaitForGameManagerAndSubscribeRoles());
    }

    private IEnumerator WaitForGameManagerAndSubscribeRoles()
    {
        while (GameManager.Instance == null)
        {
            yield return null;
        }

        GameManager.Instance.VRClientId.OnValueChanged += OnRoleAssignedCheck;
        GameManager.Instance.ARClientId.OnValueChanged += OnRoleAssignedCheck;

        CheckAndSetupAntTrail();
    }

    private void OnRoleAssignedCheck(ulong previousValue, ulong newValue)
    {
        CheckAndSetupAntTrail();
    }

    private void CheckAndSetupAntTrail()
    {
        if (_hasInitializedTrail || GameManager.Instance == null || antTrailController == null)
        {
            return;
        }

        ulong vrId = GameManager.Instance.VRClientId.Value;
        ulong arId = GameManager.Instance.ARClientId.Value;

        if (vrId == ulong.MaxValue || arId == ulong.MaxValue)
        {
            return;
        }

        bool shouldShowTrail = false;
        if (!IsOwner)
        {
            bool isThisAvatarTheAnt = RoleManager.IsClientVR(OwnerClientId);

            bool isLocalObserverTheGiant = RoleManager.IsClientAR(
                NetworkManager.Singleton.LocalClientId
            );

            if (isThisAvatarTheAnt && isLocalObserverTheGiant)
            {
                shouldShowTrail = true;
                Debug.Log(
                    $"PlayerMovement ({OwnerClientId}): Role check PASSED. This is the Ant's avatar, and I ({NetworkManager.Singleton.LocalClientId}) am the Giant. Activating trail.",
                    this
                );
            }
            else
            {
                Debug.Log(
                    $"PlayerMovement ({OwnerClientId}): Role check complete. Trail not needed. Is Ant Avatar: {isThisAvatarTheAnt}, Is Observer Giant: {isLocalObserverTheGiant}",
                    this
                );
            }
        }
        else
        {
            Debug.Log(
                $"PlayerMovement ({OwnerClientId}): Role check skipped (IsOwner=true). Trail not needed for self.",
                this
            );
        }

        if (shouldShowTrail)
        {
            antTrailVisualObject.SetActive(true);
            antTrailController.InitializeTrail(this.transform);
        }
        else
        {
            antTrailVisualObject.SetActive(false);
        }

        _hasInitializedTrail = true;

        GameManager.Instance.VRClientId.OnValueChanged -= OnRoleAssignedCheck;
        GameManager.Instance.ARClientId.OnValueChanged -= OnRoleAssignedCheck;
    }

    private void InitializeCameraTracking()
    {
        if (!IsOwner || m_isTracking)
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
                m_isTracking = true;
            }
            else
            {
                Debug.LogError(
                    $"PlayerMovementTracker (Owner: {OwnerClientId}): Could not find main camera to track! Avatar will not follow device movement.",
                    this
                );
                m_isTracking = false;
            }
        }
        else
        {
            Debug.Log(
                $"PlayerMovementTracker (Owner: {OwnerClientId}): Tracking pre-assigned camera: {m_cameraToTrack.gameObject.name}"
            );
            m_isTracking = true;
        }
    }

    private void SetupAntTrail()
    {
        if (antTrailVisualObject == null)
        {
            Debug.LogError(
                $"PlayerMovement ({OwnerClientId}): Ant Trail Visual Object is not assigned in the Inspector!",
                this
            );
            return;
        }

        antTrailController = antTrailVisualObject.GetComponent<AntTrailController>();
        if (antTrailController == null)
        {
            Debug.LogError(
                $"PlayerMovement ({OwnerClientId}): AntTrailController script not found on the assigned Ant Trail Visual Object!",
                antTrailVisualObject
            );
            antTrailVisualObject.SetActive(false);
            return;
        }

        bool shouldShowTrail = false;
        if (!IsOwner)
        {
            bool isThisAvatarTheAnt = RoleManager.IsClientVR(OwnerClientId);

            bool isLocalObserverTheGiant = RoleManager.IsClientAR(
                NetworkManager.Singleton.LocalClientId
            );

            if (isThisAvatarTheAnt && isLocalObserverTheGiant)
            {
                shouldShowTrail = true;
                Debug.Log(
                    $"PlayerMovement ({OwnerClientId}): This is the Ant's avatar, and I ({NetworkManager.Singleton.LocalClientId}) am the Giant. Activating trail.",
                    this
                );
            }
        }

        if (shouldShowTrail)
        {
            antTrailVisualObject.SetActive(true);
            antTrailController.InitializeTrail(this.transform);
        }
        else
        {
            antTrailVisualObject.SetActive(false);
            if (antTrailController.enabled)
                antTrailController.StopTrail();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= InitializeCameraTracking;
        }
        if (antTrailController != null && antTrailController.enabled)
        {
            antTrailController.StopTrail();
        }
        if (antTrailVisualObject != null)
        {
            antTrailVisualObject.SetActive(false);
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.VRClientId.OnValueChanged -= OnRoleAssignedCheck;
            GameManager.Instance.ARClientId.OnValueChanged -= OnRoleAssignedCheck;
        }
        base.OnNetworkDespawn();
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
    private void UpdateServerPositionServerRpc(
        Vector3 position,
        Quaternion rotation,
        ServerRpcParams rpcParams = default
    )
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
        if (antTrailController != null)
        {
            antTrailController.StopTrail();
        }
        if (antTrailVisualObject != null)
        {
            antTrailVisualObject.SetActive(false);
        }
        base.OnDestroy();
    }
}