using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(NetworkObject))]
public class ARWindController : NetworkBehaviour
{
    [Header("Microphone Settings")]
    [Tooltip("Index of the microphone device to use.")]
    [SerializeField]
    private int microphoneDeviceIndex = 0;

    [Tooltip("Number of samples to average for volume detection.")]
    [SerializeField]
    private int sampleWindow = 128;

    [Tooltip("Minimum volume (0-1) to trigger wind.")]
    [SerializeField]
    private float volumeThreshold = 0.05f;

    [Tooltip("Volume level that maps to maximum wind strength.")]
    [SerializeField]
    private float maxVolumeMap = 0.5f;
    [SerializeField]
    private float maxVolumeMapVFX = 0.5f;

    [Tooltip("Overall multiplier for wind strength.")]
    [SerializeField]
    private float windStrengthMultiplier = 15f;

    [Header("Wind Effect Settings")]
    [Tooltip("Time between consecutive wind gusts.")]
    [SerializeField]
    private float windCooldown = 0.3f;

    [Tooltip("Offset from camera to spawn wind origin.")]
    [SerializeField]
    private float windOriginOffset = 0.5f;

    [SerializeField]
    public GameObject windVFXPrefab;

    [SerializeField]
    private float vfxBaseSpeed = 5f;

    [SerializeField]
    private float vfxSpeedVolumeMultiplier = 10f;

    [SerializeField]
    private int vfxBaseParticleCount = 50;

    [SerializeField]
    private int vfxParticleCountVolumeMultiplier = 150;

    private static readonly string WindSpeedName = "WindSpeed";
    private static readonly string ParticleCountName = "ParticleCount";
    private static readonly string PlayWindEventName = "OnBlowWind";
    private VisualEffect activeWindVFX;

    private AudioClip micClip;
    private string microphoneName;
    private float[] samples;
    private float lastWindTime;

    private Camera arCamera;
    private bool isInitialized = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady += TryInitialize;
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ARClientId.OnValueChanged += OnRoleAssigned;
        }

        TryInitialize();
    }

    private void OnRoleAssigned(ulong prev, ulong current)
    {
        TryInitialize();
    }

    void TryInitialize()
    {
        if (isInitialized || !IsOwner)
            return;

        bool platformReady =
            PlatformRoleManager.Instance != null && PlatformRoleManager.Instance.IsPlatformReady;
        bool gameManagerReady = GameManager.Instance != null;
        bool roleAssigned = gameManagerReady && RoleManager.IsClientAR(OwnerClientId);

        if (platformReady && gameManagerReady && roleAssigned)
        {
            InitializeARWind();
            isInitialized = true;

            if (PlatformRoleManager.Instance != null)
                PlatformRoleManager.Instance.OnPlatformReady -= TryInitialize;
            if (GameManager.Instance != null)
                GameManager.Instance.ARClientId.OnValueChanged -= OnRoleAssigned;
        }
    }

    void InitializeARWind()
    {
        arCamera = Camera.main;
        if (arCamera == null)
        {
            Debug.LogError(
                "ARWindController: AR Camera (MainCamera) not found! Disabling wind.",
                this
            );
            enabled = false;
            return;
        }

        samples = new float[sampleWindow];
        InitializeMicrophone();

        if (windVFXPrefab != null)
        {
            GameObject vfxInstance = Instantiate(windVFXPrefab, arCamera.transform);
            vfxInstance.transform.localPosition = Vector3.forward * windOriginOffset;
            vfxInstance.transform.localRotation = Quaternion.identity;
            activeWindVFX = vfxInstance.GetComponent<VisualEffect>();
            if (activeWindVFX == null)
            {
                Debug.LogError(
                    "ARWindController: Instantiated windVFXPrefab is missing VisualEffect component!",
                    this
                );
            }
        }
        else
        {
            Debug.LogWarning("ARWindController: WindVFXPrefab not assigned.", this);
        }

        Debug.Log("ARWindController initialized for AR player.", this);
    }

    void InitializeMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("ARWindController: No microphone devices found! Disabling wind.", this);
            enabled = false;
            return;
        }

        microphoneName = Microphone.devices[microphoneDeviceIndex];
        micClip = Microphone.Start(microphoneName, true, 1, AudioSettings.outputSampleRate);

        if (micClip == null)
        {
            Debug.LogError(
                $"ARWindController: Failed to start microphone '{microphoneName}'. Disabling wind.",
                this
            );
            enabled = false;
            return;
        }
        Debug.Log($"ARWindController: Started microphone '{microphoneName}'.", this);
    }

    void Update()
    {
        if (!isInitialized || !IsOwner || !enabled || micClip == null || arCamera == null)
            return;
        if (
            GameManager.Instance == null
            || GameManager.Instance.CurrentPhase.Value != GamePhase.Playing
        )
            return;

        float volume = GetMicrophoneVolume();

        if (volume > volumeThreshold && Time.time > lastWindTime + windCooldown)
        {
            lastWindTime = Time.time;

            float strength = Mathf.InverseLerp(0, maxVolumeMap, volume - volumeThreshold);
            strength = Mathf.Clamp01(strength) * windStrengthMultiplier;

            Vector3 windOrigin =
                arCamera.transform.position + arCamera.transform.forward * windOriginOffset;
            Vector3 windDirection = arCamera.transform.forward;

            //Debug.Log($"ARWindController: Blowing wind! Volume: {volume:F3}, Strength: {strength:F2}", this);

            if (activeWindVFX != null)
            {
                float vfxVolumeNormalized = Mathf.InverseLerp(
                    volumeThreshold,
                    maxVolumeMapVFX,
                    volume
                );
                vfxVolumeNormalized = Mathf.Clamp01(vfxVolumeNormalized);

                float currentVFXSpeed =
                    vfxBaseSpeed + (vfxSpeedVolumeMultiplier * vfxVolumeNormalized);
                int currentVFXCount = (int)(
                    vfxBaseParticleCount + (vfxParticleCountVolumeMultiplier * vfxVolumeNormalized)
                );

                activeWindVFX.SetFloat(WindSpeedName, currentVFXSpeed);
                activeWindVFX.SetFloat(ParticleCountName, (float)currentVFXCount);

                activeWindVFX.SendEvent(PlayWindEventName);
            }

            BlowWindServerRpc(windOrigin, windDirection, strength);
        }
    }

    float GetMicrophoneVolume()
    {
        if (micClip == null || string.IsNullOrEmpty(microphoneName))
            return 0;

        int micPosition = Microphone.GetPosition(microphoneName) - (sampleWindow + 1);
        if (micPosition < 0)
            return 0;

        micClip.GetData(samples, micPosition);

        float sum = 0;
        for (int i = 0; i < sampleWindow; i++)
        {
            sum += Mathf.Abs(samples[i]);
        }
        return sum / sampleWindow;
    }

    [ServerRpc]
    void BlowWindServerRpc(Vector3 origin, Vector3 direction, float strength)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.HandleWindEffect(origin, direction, strength, OwnerClientId);
        }
    }

    public override void OnDestroy()
    {
        if (IsOwner)
        {
            if (PlatformRoleManager.Instance != null)
                PlatformRoleManager.Instance.OnPlatformReady -= TryInitialize;
            if (GameManager.Instance != null)
                GameManager.Instance.ARClientId.OnValueChanged -= OnRoleAssigned;

            if (!string.IsNullOrEmpty(microphoneName) && Microphone.IsRecording(microphoneName))
            {
                Microphone.End(microphoneName);
                Debug.Log($"ARWindController: Stopped microphone '{microphoneName}'.", this);
            }

            if (activeWindVFX != null)
            {
                Destroy(activeWindVFX.gameObject);
            }
        }
        base.OnDestroy();
    }
}