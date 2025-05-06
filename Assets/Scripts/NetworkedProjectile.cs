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

    //private static readonly int ImpactEventID = Shader.PropertyToID("Impact");
    private const string ImpactEventName = "Impact";

    private Rigidbody rb;
    private VisualEffect vfx;
    private Collider col;
    private float spawnTime;
    private float initialSpeed;
    private bool isDestroying = false;
    private ulong ownerClientId;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        vfx = GetComponent<VisualEffect>();
        col = GetComponent<Collider>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        col.isTrigger = false;
    }

    public override void OnNetworkSpawn()
    {
        spawnTime = Time.time;
        if (!IsServer)
        {
            rb.isKinematic = true;
            col.enabled = false;
        }
    }

    public void Initialize(Vector3 position, Vector3 direction, float speed, ulong ownerId)
    {
        if (!IsServer)
            return;

        transform.position = position;
        transform.rotation = Quaternion.LookRotation(direction);
        this.initialSpeed = speed;
        this.ownerClientId = ownerId;

        rb.linearVelocity = direction.normalized * initialSpeed;
        Debug.Log(
            $"[Server] Projectile {NetworkObject.NetworkObjectId} initialized. Pos: {position}, Dir: {direction}, Speed: {speed}, Vel: {rb.linearVelocity}"
        );
    }

    void Update()
    {
        if (!IsServer)
            return;

        if (Time.time > spawnTime + lifetime && !isDestroying)
        {
            Debug.Log($"[Server] Projectile {NetworkObject.NetworkObjectId} lifetime expired.");
            DestroySelf();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // DelayedOnCollision(2f);

        if (isDestroying || !IsServer)
            return;

        if (((1 << collision.gameObject.layer) & hitLayerMask) == 0)
        {
            // DestroySelf();
            return;
        }

        Debug.Log(
            $"[Server] Projectile {NetworkObject.NetworkObjectId} collided with: {collision.gameObject.name} on layer {LayerMask.LayerToName(collision.gameObject.layer)}"
        );
        HandleCollision(collision);
    }

    private System.Collections.IEnumerator DelayedOnCollision(float delay)
    {
        yield return new WaitForSeconds(delay);
    }

    private void HandleCollision(Collision collision)
    {
        if (isDestroying || !IsServer)
            return;

        Debug.Log(
            $"[Server] Projectile {NetworkObject.NetworkObjectId} handling collision with: {collision.collider.name} on layer {LayerMask.LayerToName(collision.collider.gameObject.layer)}"
        );

        NetworkObject targetNetworkObject =
            collision.collider.GetComponentInParent<NetworkObject>();

        int vrPlayerLayer = LayerMask.NameToLayer("VRPlayer");
        bool hitVRPlayer = (collision.gameObject.layer == vrPlayerLayer);

        if (hitVRPlayer)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ReportProjectileHitServerRpc(
                    targetNetworkObject.OwnerClientId
                );
            }
            else
            {
                Debug.LogError(
                    "[Server] GameManager.Instance is null, cannot report projectile hit."
                );
            }
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
            vfx.SendEvent(ImpactEventName);
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