using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    [Header("HUD Canvases")]
    [SerializeField]
    private GameObject arHudCanvasGO;

    [SerializeField]
    private GameObject vrHudCanvasGO;

    [Header("HUD Script References")]
    [SerializeField]
    private GameHUD arHudScript;

    [SerializeField]
    private GameHUD vrHudScript;

    private GameHUD activeHud;
    private bool hasInitialized = false;

    void Start()
    {
        if (PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady += HandlePlatformReady;
            Debug.Log("UIManager: Subscribed to PlatformRoleManager.OnPlatformReady");
        }
        else
        {
            Debug.LogError(
                "UIManager: PlatformRoleManager.Instance is null! Cannot subscribe to OnPlatformReady."
            );
        }
        StartCoroutine(WaitForGameManagerAndSubscribeRoles());
    }

    void HandlePlatformReady()
    {
        Debug.Log("UIManager: Received PlatformRoleManager.OnPlatformReady event.");
        TryInitializeUI();
    }

    IEnumerator WaitForGameManagerAndSubscribeRoles()
    {
        Debug.Log("UIManager: Coroutine waiting for GameManager Instance...");
        while (GameManager.Instance == null)
        {
            yield return null;
        }
        Debug.Log("UIManager: GameManager Instance found. Subscribing to Role changes.");

        GameManager.Instance.VRClientId.OnValueChanged += OnRoleChanged;
        GameManager.Instance.ARClientId.OnValueChanged += OnRoleChanged;

        TryInitializeUI();
    }

    void OnRoleChanged(ulong prev, ulong current)
    {
        Debug.Log(
            $"UIManager: Role changed detected via NetworkVariable (Value: {current}). Attempting UI Initialization check."
        );
        TryInitializeUI();
    }

    void TryInitializeUI()
    {
        if (hasInitialized)
            return;

        bool platformReady =
            PlatformRoleManager.Instance != null && PlatformRoleManager.Instance.IsPlatformReady;
        bool gameManagerReady = GameManager.Instance != null;
        bool rolesAssigned = false;

        if (gameManagerReady)
        {
            ulong arId = RoleManager.GetARClientId();
            ulong vrId = RoleManager.GetVRClientId();
            rolesAssigned = arId != ulong.MaxValue && vrId != ulong.MaxValue;
        }
        Debug.Log(
            $"UIManager: TryInitializeUI Check - PlatformReady: {platformReady}, GameManagerReady: {gameManagerReady}, RolesAssigned: {rolesAssigned}"
        );

        if (platformReady && gameManagerReady && rolesAssigned)
        {
            Debug.Log("UIManager: Platform is ready, calling InitializeLocalPlayerUI.");
            InitializeLocalPlayerUI();
            hasInitialized = true;
            PlatformRoleManager.Instance.OnPlatformReady -= HandlePlatformReady;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.VRClientId.OnValueChanged -= OnRoleChanged;
                GameManager.Instance.ARClientId.OnValueChanged -= OnRoleChanged;
            }
        }
        else
        {
            Debug.Log("UIManager: Platform not ready yet, delaying UI initialization.");
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (PlatformRoleManager.Instance != null)
        {
            PlatformRoleManager.Instance.OnPlatformReady -= TryInitializeUI;
            Debug.Log("UIManager: Unsubscribed from PlatformRoleManager.OnPlatformReady");
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.VRClientId.OnValueChanged -= OnRoleChanged;
            GameManager.Instance.ARClientId.OnValueChanged -= OnRoleChanged;
        }
    }

    public void InitializeLocalPlayerUI()
    {
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("UIManager: Not a client/host, disabling all HUDs.");
            arHudCanvasGO.SetActive(false);
            vrHudCanvasGO.SetActive(false);
            activeHud = null;
            return;
        }

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        bool isAR = RoleManager.IsClientAR(localClientId);

        Debug.Log($"UIManager: Initializing UI for local client {localClientId}. IsAR: {isAR}");

        if (isAR)
        {
            arHudCanvasGO.SetActive(true);
            vrHudCanvasGO.SetActive(false);
            activeHud = arHudScript;
            Debug.Log("UIManager: AR HUD Activated.");
        }
        else
        {
            arHudCanvasGO.SetActive(false);
            vrHudCanvasGO.SetActive(true);
            activeHud = vrHudScript;
            Debug.Log("UIManager: VR HUD Activated.");

            Canvas vrCanvas = vrHudCanvasGO.GetComponent<Canvas>();
            if (vrCanvas && vrCanvas.worldCamera == null)
            {
                Camera vrCam = FindObjectOfType<Camera>();
                if (vrCam != null)
                {
                    vrCanvas.worldCamera = vrCam;
                    Debug.Log($"UIManager: Set Event Camera for VR Canvas to {vrCam.name}");
                }
                else
                {
                    Debug.LogError(
                        "UIManager: Could not find VR Camera to assign to World Space Canvas!"
                    );
                }
            }
        }

        if (activeHud == null)
        {
            Debug.LogError("UIManager: Failed to assign an active HUD!");
        }
    }

    public void UpdateTimer(float timeRemaining)
    {
        if (activeHud != null)
        {
            activeHud.UpdateTimer(timeRemaining);
        }
    }

    public void UpdatePhase(GamePhase phase)
    {
        if (activeHud != null)
        {
            activeHud.UpdatePhase(phase);
        }
    }

    public void UpdateWinnerText(ulong winnerClientId)
    {
        if (activeHud != null)
        {
            activeHud.UpdateWinnerText(winnerClientId);
        }
    }
}