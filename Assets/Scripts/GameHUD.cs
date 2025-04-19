using TMPro;
using Unity.Netcode;
using UnityEngine;

public class GameHUD : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI timerText;

    [SerializeField]
    private TextMeshProUGUI statusText;

    [SerializeField]
    private TextMeshProUGUI winnerText;

    public static GameHUD Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                $"Duplicate GameHUD instance found on {gameObject.name}. Destroying duplicate."
            );
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log($"GameHUD Instance assigned to {gameObject.name}");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Debug.Log($"GameHUD Instance nulled (was {gameObject.name})");
        }
    }

    void Start()
    {
        if (timerText != null)
            timerText.text = "00:00";
        if (statusText != null)
            statusText.text = "Waiting...";
        if (winnerText != null)
            winnerText.gameObject.SetActive(false);
    }

    public void UpdateTimer(float timeRemaining)
    {
        if (timerText == null)
            return;

        if (timeRemaining < 0)
            timeRemaining = 0;

        int minutes = Mathf.FloorToInt(timeRemaining / 60F);
        int seconds = Mathf.FloorToInt(timeRemaining % 60F);
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void UpdatePhase(GamePhase phase)
    {
        if (statusText == null)
            return;
            
        switch (phase)
        {
            case GamePhase.WaitingForPlayers:
                statusText.text = "Waiting for players...";
                break;
            case GamePhase.Setup:
                if (
                    NetworkManager.Singleton != null
                    && NetworkManager.Singleton.IsConnectedClient
                    && RoleManager.IsClientAnt(NetworkManager.Singleton.LocalClientId)
                )
                {
                    statusText.text = "Scan your play area!";
                }
                else
                {
                    statusText.text = "Waiting for Ant to scan...";
                }
                break;
            case GamePhase.LevelReady:
                statusText.text = "Level Ready!";
                break;
            case GamePhase.Countdown:
                statusText.text = "Get Ready!";
                break;
            case GamePhase.Playing:
                if (
                    NetworkManager.Singleton != null
                    && NetworkManager.Singleton.IsConnectedClient
                    && RoleManager.IsClientAnt(NetworkManager.Singleton.LocalClientId)
                )
                {
                    statusText.text = "Evade the Giant!";
                }
                else if (
                    NetworkManager.Singleton != null
                    && NetworkManager.Singleton.IsConnectedClient
                    && RoleManager.IsClientVR(NetworkManager.Singleton.LocalClientId)
                )
                {
                    statusText.text = "Catch the Ant!";
                }
                else
                {
                    statusText.text = "Game in Progress";
                }
                break;
            case GamePhase.GameOver:
                statusText.text = "Game Over!";
                break;
            default:
                statusText.text = "";
                break;
        }
    }

    public void UpdateWinnerText(ulong winnerClientId)
    {
        if (winnerText == null)
            return;

        if (winnerClientId == ulong.MaxValue)
        {
            winnerText.gameObject.SetActive(false);
            return;
        }

        winnerText.gameObject.SetActive(true);
        if (winnerClientId == RoleManager.VRClientId)
        {
            winnerText.text = "Giant Wins!";
        }
        else if (winnerClientId == RoleManager.AntClientId)
        {
            winnerText.text = "Ant Wins!";
        }
        else
        {
            winnerText.text = "Game Over!";
        }
    }
}