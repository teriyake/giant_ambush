using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARAnchorFollower : MonoBehaviour
{
    private ARAnchor m_anchorToFollow;
    private GameObject m_objectToFollow;

    private bool m_isInitialized = false;

    public void Initialize(ARAnchor anchor)
    {
        if (anchor == null)
        {
            Debug.LogError("ARAnchorFollower: Initialize called with a null anchor!", this);
            enabled = false; 
            return;
        }

        m_anchorToFollow = anchor;
        m_objectToFollow = anchor.gameObject;
        m_isInitialized = true;
        Debug.Log($"ARAnchorFollower initialized for {gameObject.name} to follow anchor {anchor.trackableId}", this);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!m_isInitialized || m_anchorToFollow == null || m_objectToFollow == null)
        {
            if(m_isInitialized) {
                Debug.LogWarning($"ARAnchorFollower on {gameObject.name}: Anchor reference lost. Stopping updates.", this);
                enabled = false;
            }
            return;
        }

        if (m_anchorToFollow.trackingState == TrackingState.Tracking)
        {
            transform.position = m_objectToFollow.transform.position;
            transform.rotation = m_objectToFollow.transform.rotation;
        }
    }

    void OnDestroy()
    {
        if (m_anchorToFollow != null)
        {
            Debug.Log($"ARAnchorFollower on {gameObject.name} is being destroyed. Removing associated ARAnchor {m_anchorToFollow.trackableId}.", this);
            Destroy(m_anchorToFollow.gameObject);
            m_anchorToFollow = null;
            m_objectToFollow = null;
        }
        m_isInitialized = false;
    }
}
