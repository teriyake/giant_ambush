using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(VisualEffect))]
public class AntTrailController : MonoBehaviour
{
    public Transform antPlayerTransform;
    public float speedThreshold = 0.01f;
    public int maxSpawnRate = 400;

    private VisualEffect trailVFX;
    private bool isInitialized = false;
    private Vector3 previousPosition;

    private static readonly int AntPositionID = Shader.PropertyToID("AntPosition");
    private static readonly int AntForwardID = Shader.PropertyToID("AntForward");
    private static readonly int SpawnRateID = Shader.PropertyToID("SpawnRate");

    void Awake()
    {
        trailVFX = GetComponent<VisualEffect>();

        if (trailVFX == null)
        {
            Debug.LogError(
                "AntTrailController: VisualEffect component not found on this GameObject!",
                this
            );
            enabled = false;
            return;
        }
        if (trailVFX.visualEffectAsset == null)
        {
            Debug.LogError(
                "AntTrailController: No Visual Effect Asset assigned to the Visual Effect component!",
                this
            );
            enabled = false;
            return;
        }
    }

    public void InitializeTrail(Transform targetTransform)
    {
        if (targetTransform == null)
        {
            Debug.LogError(
                "AntTrailController: InitializeTrail called with a null target Transform!",
                this
            );
            enabled = false;
            return;
        }
        antPlayerTransform = targetTransform;
        isInitialized = true;
        Debug.Log($"AntTrailController initialized to follow {antPlayerTransform.name}", this);

        gameObject.SetActive(true);
        trailVFX.Play();
    }

    void Update()
    {
        if (isInitialized && antPlayerTransform != null && trailVFX != null && trailVFX.enabled)
        {
            trailVFX.SetVector3(AntPositionID, antPlayerTransform.position);
            trailVFX.SetVector3(AntForwardID, antPlayerTransform.forward);
        }
        else if (isInitialized && antPlayerTransform == null)
        {
            StopTrail();
            isInitialized = false;
        }
        Vector3 currentPosition = antPlayerTransform.position;

        trailVFX.SetVector3(AntPositionID, currentPosition);

        if (Time.deltaTime > 0)
        {
            Vector3 deltaPosition = currentPosition - previousPosition;
            float speed = deltaPosition.magnitude / Time.deltaTime;

            if (speed < speedThreshold)
            {
                trailVFX.SetInt(SpawnRateID, 0);
            }
            else
            {
                trailVFX.SetInt(SpawnRateID, maxSpawnRate);
            }
        }

        previousPosition = currentPosition;
    }

    public void StartTrail()
    {
        if (trailVFX != null && isInitialized)
        {
            trailVFX.Play();
        }
    }

    public void StopTrail()
    {
        if (trailVFX != null && isInitialized)
        {
            trailVFX.Stop();
        }
    }
}