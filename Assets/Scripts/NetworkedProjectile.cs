using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(VisualEffect))]
public class NetworkedProjectile : NetworkBehaviour
{
    [SerializeField]
    private float lifetime = 3.0f;

    [SerializeField]
    private LayerMask hitLayerMask;

    private static readonly int ImpactEventID = Shader.PropertyToID("Impact");

    private Rigidbody rb;
    private VisualEffect vfx;
    private Collider col;
    private float spawnTime;
    private float initialSpeed;
    private bool isDestroying = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        vfx = GetComponent<VisualEffect>();
        col = GetComponent<Collider>();

        rb.isKinematic = true;
        rb.useGravity = false;
        col.isTrigger = true;
    }

    public override void OnNetworkSpawn()
    {
        spawnTime = Time.time;
    }

    public void Initialize(Vector3 direction, float initialSpeed)
    {
        transform.forward = direction.normalized;
        this.initialSpeed = initialSpeed;
    }

    void Update()
    {
        if (!IsServer)
            return;

        float stepDistance = initialSpeed * Time.deltaTime;
        Vector3 movement = transform.forward * stepDistance;
        Vector3 nextPosition = transform.position + movement;

        RaycastHit hit;
        Vector3 rayOrigin = transform.position + transform.forward * 0.01f;

        if (
            Physics.Raycast(
                rayOrigin,
                transform.forward,
                out hit,
                stepDistance + 0.01f,
                hitLayerMask
            )
        )
        {
            HandleHit(hit);
        }
        else
        {
            transform.position = nextPosition;
        }

        if (Time.time > spawnTime + lifetime)
        {
            DestroySelf();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isDestroying || !IsServer || ((1 << other.gameObject.layer) & hitLayerMask) == 0)
            return;

        Debug.Log(
            $"[Server] Projectile triggered: {other.name} on layer {LayerMask.LayerToName(other.gameObject.layer)}"
        );

        NetworkObject targetNetworkObject = other.GetComponentInParent<NetworkObject>();
        if (
            targetNetworkObject == null
            || (
                GameManager.Instance != null
                && targetNetworkObject.OwnerClientId != GameManager.Instance.VRClientId.Value
            )
        )
        {
            Debug.Log($"[Server] Projectile hit non-VR object {other.name}. Destroying self.");
            DestroySelf();
        }
    }

    private void HandleHit(RaycastHit hit)
    {
        if (isDestroying || !IsServer)
            return;

        Debug.Log(
            $"[Server] Projectile raycast hit: {hit.collider.name} on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}"
        );

        NetworkObject targetNetworkObject = hit.collider.GetComponentInParent<NetworkObject>();
        bool hitVRPlayer = false;
        if (
            targetNetworkObject != null
            && GameManager.Instance != null
            && targetNetworkObject.OwnerClientId == GameManager.Instance.VRClientId.Value
        )
        {
            hitVRPlayer = true;
            Debug.Log(
                $"[Server] Projectile raycast hit VR Player (Client {targetNetworkObject.OwnerClientId})!"
            );
        }
        else if (targetNetworkObject != null)
        {
            Debug.Log(
                $"[Server] Projectile raycast hit NetworkObject {targetNetworkObject.name} owned by {targetNetworkObject.OwnerClientId}, but it's not the VR player ({GameManager.Instance?.VRClientId.Value})."
            );
        }
        else
        {
            Debug.Log(
                $"[Server] Projectile raycast hit {hit.collider.name}, but it has no NetworkObject."
            );
        }

        if (hitVRPlayer)
        {
            GameManager.Instance.ReportProjectileHitServerRpc(targetNetworkObject.OwnerClientId);
        }

        DestroySelf();
    }

    private void DestroySelf()
    {
        if (!IsServer || isDestroying)
            return;
        isDestroying = true;

        Debug.Log($"[Server] DestroySelf called for {NetworkObject.NetworkObjectId}");

        if (vfx != null)
        {
            TriggerImpactVFXClientRpc();
        }

        if (col != null)
        {
            col.enabled = false;
            Debug.Log($"[Server] Disabled collider for {NetworkObject.NetworkObjectId}");
        }

        StartCoroutine(DelayedDespawn(0.5f));
    }

    [ClientRpc]
    private void TriggerImpactVFXClientRpc()
    {
        if (vfx != null)
        {
            Debug.Log(
                $"[{NetworkManager.Singleton?.LocalClientId ?? 0}] Triggering Impact VFX for {gameObject.name}"
            );
            vfx.SendEvent(ImpactEventID);
        }
        else
        {
            Debug.LogWarning(
                $"[{NetworkManager.Singleton?.LocalClientId ?? 0}] TriggerImpactVFXClientRpc called but vfx is null for {gameObject.name}!"
            );
        }
    }

    private System.Collections.IEnumerator DelayedDespawn(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            Debug.Log($"[Server] Delayed Despawn executing for {NetworkObject.NetworkObjectId}");
            NetworkObject.Despawn(true);
        }
        else if (!IsServer) { }
        else
        {
            Debug.LogWarning(
                $"[Server] Delayed Despawn: NetworkObject invalid or not spawned for {NetworkObject?.NetworkObjectId ?? 0}."
            );
        }
    }
}