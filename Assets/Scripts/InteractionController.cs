using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class InteractionController : NetworkBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField]
    private LayerMask m_interactableLayer;

    [SerializeField]
    private float m_maxInteractionDistance = 50f;

    [SerializeField]
    private Camera m_playerCamera;

    [Header("AR Attack Settings")]
    [SerializeField]
    private float m_minSwipeDistancePixels = 50f;

    [SerializeField]
    private float minProjectileSpeed = 8f;

    [SerializeField]
    private float maxProjectileSpeed = 25f;

    [SerializeField]
    private float minExpectedSwipeSpeed = 200f;

    [SerializeField]
    private float maxExpectedSwipeSpeed = 10000f;

    [SerializeField]
    private float m_attackCooldown = 1.0f;

    [SerializeField]
    private float m_aimRaycastDistance = 10f;

    private bool m_canInteract = false;
    private Vector2 m_swipeStartPosition;
    private bool m_isSwiping = false;
    private float m_swipeStartTime;
    private float m_lastAttackTime = -10f;

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
            Debug.Log(
                $"InteractionController (Owner: {OwnerClientId}): Platform already ready. Initializing interaction camera."
            );
            InitializeInteractionCamera();
        }
        else if (PlatformRoleManager.Instance != null)
        {
            Debug.Log(
                $"InteractionController (Owner: {OwnerClientId}): Platform not ready yet. Subscribing to OnPlatformReady event."
            );
            PlatformRoleManager.Instance.OnPlatformReady += InitializeInteractionCamera;
        }
        else
        {
            Debug.LogError(
                $"InteractionController (Owner: {OwnerClientId}): PlatformRoleManager Instance not found on spawn!",
                this
            );
            m_canInteract = false;
        }
    }

    private void InitializeInteractionCamera()
    {
        if (!IsOwner || m_canInteract)
            return;

        Debug.Log(
            $"InteractionController (Owner: {OwnerClientId}): InitializeInteractionCamera called."
        );

        if (m_playerCamera == null)
        {
            m_playerCamera = Camera.main;
            if (m_playerCamera != null)
            {
                Debug.Log(
                    $"InteractionController (Owner: {OwnerClientId}): Found Camera.main: {m_playerCamera.name}",
                    m_playerCamera.gameObject
                );
            }
        }
        else
        {
            Debug.Log(
                $"InteractionController (Owner: {OwnerClientId}): Using pre-assigned camera: {m_playerCamera.name}"
            );
        }

        if (m_playerCamera == null)
        {
            Debug.LogError(
                $"InteractionController (Owner: {OwnerClientId}): Player Camera not found AFTER platform ready! Interaction will not work.",
                this
            );
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
            Debug.Log(
                $"InteractionController (Owner: {OwnerClientId}): Unsubscribed from OnPlatformReady."
            );
        }
        base.OnNetworkDespawn();
    }

    // Start is called before the first frame update
    void Start() { }

    // Update is called once per frame
    void Update()
    {
        if (
            !m_canInteract
            || m_playerCamera == null
            || GameManager.Instance == null
            || PlatformRoleManager.Instance == null
            || !IsOwner
        )
            return;

        if (
            GameManager.Instance.CurrentPhase.Value != GamePhase.Playing
            || !RoleManager.IsClientAR(NetworkManager.Singleton.LocalClientId)
        )
            return;

        Pointer currentPointer = Pointer.current;
        if (currentPointer == null)
            return;

        if (currentPointer.press.wasPressedThisFrame)
        {
            m_swipeStartPosition = currentPointer.position.ReadValue();
            m_swipeStartTime = Time.time;
            m_isSwiping = true;
            // TODO: show swipe start feedback?
        }

        if (currentPointer.press.wasReleasedThisFrame && m_isSwiping)
        {
            Vector2 swipeEndPosition = currentPointer.position.ReadValue();
            float swipeEndTime = Time.time;
            m_isSwiping = false;

            Vector2 swipeVector = swipeEndPosition - m_swipeStartPosition;
            float swipeDistance = swipeVector.magnitude;
            float swipeDuration = swipeEndTime - m_swipeStartTime;
            if (swipeDuration < 0.01f)
                swipeDuration = 0.01f;

            float swipeSpeedPixelsPerSec = swipeDistance / swipeDuration;

            if (
                swipeDistance >= m_minSwipeDistancePixels
                && Time.time >= m_lastAttackTime + m_attackCooldown
            )
            {
                m_lastAttackTime = Time.time;
                float normalizedSwipeSpeed = Mathf.InverseLerp(
                    minExpectedSwipeSpeed,
                    maxExpectedSwipeSpeed,
                    swipeSpeedPixelsPerSec
                );
                float calculatedProjectileSpeed = Mathf.Lerp(
                    minProjectileSpeed,
                    maxProjectileSpeed,
                    normalizedSwipeSpeed
                );

                Debug.Log(
                    $"Swipe: Dist={swipeDistance:F1}px, Dur={swipeDuration:F2}s, Speed={swipeSpeedPixelsPerSec:F1}px/s -> Projectile Speed: {calculatedProjectileSpeed:F1}m/s"
                );

                PerformSwipeAttack(
                    m_swipeStartPosition,
                    swipeEndPosition,
                    calculatedProjectileSpeed
                );
                // TODO: handle tap if swipe distance is too small?
            }
        }

        /*
         * tap to capture logic
        Vector2 screenPosition = currentPointer.position.ReadValue();

        Ray ray = m_playerCamera.ScreenPointToRay(screenPosition);
        Debug.DrawRay(ray.origin, ray.direction * m_maxInteractionDistance, Color.yellow, 1.0f);

        if (Physics.Raycast(ray, out RaycastHit hit, m_maxInteractionDistance, m_interactableLayer))
        {
            NetworkObject hitNetworkObject = hit.collider.GetComponent<NetworkObject>();
            Debug.Log(
                $"InteractionController Update: Raycast hit object '{hit.collider.gameObject.name}'. Has NetworkObject: {hitNetworkObject != null}"
            );
            if (hitNetworkObject != null)
            {
                Debug.Log(
                    $"InteractionController Update: Hit NetworkObject OwnerClientId={hitNetworkObject.OwnerClientId}, GameManager.Instance.VRClientId.Value={GameManager.Instance.VRClientId.Value}"
                );
                if (
                    GameManager.Instance.VRClientId.Value != ulong.MaxValue
                    && hitNetworkObject.OwnerClientId == GameManager.Instance.VRClientId.Value
                )
                {
                    Debug.Log(
                        $"InteractionController (AR): Hit VR Player object {hitNetworkObject.NetworkObjectId}. Requesting capture attempt."
                    );
                    GameManager.Instance.RequestCaptureAttemptServerRpc(
                        hitNetworkObject.NetworkObjectId
                    );
                }
                else
                {
                    Debug.Log(
                        $"InteractionController (AR): Hit NetworkObject OwnerClientId ({hitNetworkObject.OwnerClientId}) does not match GameManager.Instance.VRClientId.Value ({GameManager.Instance.VRClientId.Value})."
                    );
                }
            }
            else
            {
                Debug.Log(
                    $"InteractionController (AR): Hit object '{hit.collider.gameObject.name}' does not have a NetworkObject component."
                );
            }
        }
        else
        {
            Debug.Log("InteractionController (AR): Tap/click did not hit any interactable object.");
        }
        */
    }

    private void PerformSwipeAttack(
        Vector2 screenStartPos,
        Vector2 screenEndPos,
        float projectileSpeed
    )
    {
        Ray aimRay = m_playerCamera.ScreenPointToRay(screenStartPos);
        Vector3 targetPoint;

        if (
            Physics.Raycast(
                aimRay,
                out RaycastHit hit,
                m_aimRaycastDistance
                // ~LayerMask.GetMask("Everything")
            )
        )
        {
            targetPoint = hit.point;
            Debug.DrawLine(aimRay.origin, targetPoint, Color.green, 2.0f);
        }
        else
        {
            targetPoint = aimRay.GetPoint(m_aimRaycastDistance);
            Debug.DrawLine(aimRay.origin, targetPoint, Color.yellow, 2.0f);
        }

        Vector3 attackOrigin = aimRay.origin + aimRay.direction * 0.2f;

        Vector3 attackDirection = (targetPoint - attackOrigin).normalized;

        Debug.Log($"Swipe Attack: Origin={attackOrigin}, Direction={attackDirection}");
        Debug.DrawRay(attackOrigin, attackDirection * 3f, Color.red, 2.0f);

        GameManager.Instance.RequestAttackServerRpc(attackOrigin, attackDirection, projectileSpeed);

        // TODO: handles VFX for AR
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