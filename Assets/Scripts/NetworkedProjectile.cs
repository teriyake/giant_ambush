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
        transform.position += transform.forward * initialSpeed * Time.deltaTime;

        if (IsServer && Time.time > spawnTime + lifetime)
        {
            DestroySelf();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & hitLayerMask) != 0)
            return;

        Debug.Log(
            $"Projectile hit: {other.name} on layer {LayerMask.LayerToName(other.gameObject.layer)}"
        );
        NetworkObject targetNetworkObject = other.GetComponentInParent<NetworkObject>();

        if (targetNetworkObject != null)
        {
            if (
                GameManager.Instance != null
                && targetNetworkObject.OwnerClientId == GameManager.Instance.VRClientId.Value
            )
            {
                Debug.Log(
                    $"Projectile hit VR Player (Client {targetNetworkObject.OwnerClientId})!"
                );
                if (IsServer)
                {
                    GameManager.Instance.ReportProjectileHitServerRpc(
                        targetNetworkObject.OwnerClientId
                    );
                }

                DestroySelf();
            }
            else
            {
                Debug.Log(
                    $"Projectile hit NetworkObject {targetNetworkObject.name} owned by {targetNetworkObject.OwnerClientId}, but it's not the VR player ({GameManager.Instance?.VRClientId.Value})."
                );
                DestroySelf();
            }
        }
        else
        {
            Debug.Log($"Projectile hit {other.name}, but it has no NetworkObject.");
            DestroySelf();
        }
    }

    private void DestroySelf()
    {
        if (!IsServer)
            return;

        if (vfx != null)
        {
            vfx.SendEvent(ImpactEventID);
            TriggerImpactVFXClientRpc();
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            // Destroy(gameObject);
        }
    }

    [ClientRpc]
    private void TriggerImpactVFXClientRpc()
    {
        if (!IsServer && vfx != null)
        {
            vfx.SendEvent(ImpactEventID);
        }
    }

    private System.Collections.IEnumerator DelayedDespawn(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}
