using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Management;
using System;

public class PlatformRoleManager : MonoBehaviour
{
    public static PlatformRoleManager Instance { get; private set; }
    public event Action OnPlatformReady;
    public bool IsPlatformReady { get; private set; } = false;

    [Header("XR Setups")]
    [SerializeField] private GameObject m_arRigGameObject;
    [SerializeField] private GameObject m_arSessionGameObject;
    [SerializeField] private GameObject m_vrRigGameObject;

    [Header("Camera Settings")]
    [SerializeField] private Camera m_arCamera;
    [SerializeField] private Camera m_vrCamera;

    private const string MainCameraTag = "MainCamera";
    private bool m_isInitializing = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad(gameObject);

        if (m_arRigGameObject == null || m_vrRigGameObject == null || m_arCamera == null || m_vrCamera == null)
        {
            Debug.LogError("PlatformRoleManager: Rigs or Cameras not assigned!", this);
            return;
        }

        m_arRigGameObject.SetActive(false);
        m_arSessionGameObject.SetActive(false);
        m_vrRigGameObject.SetActive(false);
        m_arCamera.enabled = false;
        m_vrCamera.enabled = false;

        IsPlatformReady = false;
    }

    public void SetupForRole(bool isVrHost)
    {
        if (m_isInitializing || IsPlatformReady)
        {
            Debug.LogWarning("PlatformRoleManager: Already initializing, ignoring new request.");
            return;
        }
        m_isInitializing = true;

        StartCoroutine(SwitchToXR(isVrHost));
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private IEnumerator SwitchToXR(bool isVrHost)
    {
        IsPlatformReady = false;

        Debug.Log("SwitchToXR: Starting...");

        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null && XRGeneralSettings.Instance.Manager.isInitializationComplete)
        {
            Debug.Log("SwitchToXR: Stopping existing XR Subsystems...");
            XRGeneralSettings.Instance.Manager.StopSubsystems();
            yield return null;

            Debug.Log("SwitchToXR: Deinitializing existing XR Loader...");
            XRGeneralSettings.Instance.Manager.DeinitializeLoader();
            yield return null;
            Debug.Log("SwitchToXR: Existing XR should be stopped.");
        } 
        else 
        {
            Debug.Log("SwitchToXR: No active XR Loader found or XR Manager unavailable.");
        }

        string targetSystem = isVrHost ? "VR (OpenXR)" : "AR (iOS)";
        Debug.Log($"SwitchToXR: Attempting to initialize {targetSystem} loader...");

        yield return XRGeneralSettings.Instance.Manager.InitializeLoader();

        if (XRGeneralSettings.Instance.Manager.activeLoader == null)
        {
            Debug.LogError($"SwitchToXR: Failed to initialize {targetSystem} loader! Check XR Plug-in Management settings for this build target.");
            m_isInitializing = false;
            yield break;
        }

        Debug.Log($"SwitchToXR: Successfully initialized {targetSystem} loader ({XRGeneralSettings.Instance.Manager.activeLoader.name}).");

        Debug.Log("SwitchToXR: Starting XR Subsystems...");
        XRGeneralSettings.Instance.Manager.StartSubsystems();

        yield return null;

        Debug.Log("SwitchToXR: Subsystems should be started.");

        Debug.Log($"SwitchToXR: Activating {(isVrHost ? "VR" : "AR")} Rig and Camera...");
        if (isVrHost)
        {
            if(m_arRigGameObject != null) m_arRigGameObject.SetActive(false);
            if(m_vrRigGameObject != null) m_vrRigGameObject.SetActive(true);
            if(m_arCamera != null) m_arCamera.enabled = false;
            if(m_vrCamera != null) m_vrCamera.enabled = true;
            if(m_vrCamera != null && m_vrCamera.tag != MainCameraTag) m_vrCamera.tag = MainCameraTag;
            if(m_arCamera != null && m_arCamera.CompareTag(MainCameraTag)) m_arCamera.tag = "Untagged";
            Debug.Log("SwitchToXR: VR Rig activated.");
        }
        else 
        {
            if(m_vrRigGameObject != null) m_vrRigGameObject.SetActive(false);
            if(m_arRigGameObject != null) m_arRigGameObject.SetActive(true);
            if(m_vrCamera != null) m_vrCamera.enabled = false;
            if(m_arCamera != null) m_arCamera.enabled = true;
            if(m_arCamera != null && m_arCamera.tag != MainCameraTag) m_arCamera.tag = MainCameraTag;
            if(m_vrCamera != null && m_vrCamera.CompareTag(MainCameraTag)) m_vrCamera.tag = "Untagged";
            Debug.Log("SwitchToXR: AR Rig activated.");
        }

        m_arSessionGameObject.SetActive(true);

        m_isInitializing = false;
        IsPlatformReady = true;
        Debug.Log("SwitchToXR: Platform is ready. Invoking OnPlatformReady event.");
        OnPlatformReady?.Invoke();

        Debug.Log("SwitchToXR: Coroutine complete.");
    }

    void OnApplicationQuit()
    {
        Debug.Log("OnApplicationQuit: Stopping XR Subsystems and Deinitializing Loader.");
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null && XRGeneralSettings.Instance.Manager.isInitializationComplete)
        {
            XRGeneralSettings.Instance.Manager.StopSubsystems();
            XRGeneralSettings.Instance.Manager.DeinitializeLoader();
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
