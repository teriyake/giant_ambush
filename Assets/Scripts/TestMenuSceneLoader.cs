using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TestMenuSceneLoader : MonoBehaviour
{
    [SerializeField] private Button m_MeshingBtn;
    [SerializeField] private Button m_MultiplayerBtn;

    void Awake()
    {
        m_MeshingBtn.onClick.AddListener(() => SceneManager.LoadScene(1));
        m_MultiplayerBtn.onClick.AddListener(() => SceneManager.LoadScene(2));
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
