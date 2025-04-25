using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

public class PlayerFeedback : NetworkBehaviour
{
    [Header("Feedback Effects")]
    [SerializeField]
    private ParticleSystem captureSuccessParticles;

    [SerializeField]
    private AudioClip captureSuccessSound;

    [SerializeField]
    private ParticleSystem capturedParticles;

    [SerializeField]
    private AudioClip capturedSound;

    [SerializeField]
    private ParticleSystem captureFailParticles;

    [SerializeField]
    private AudioClip captureFailSound;

    [SerializeField]
    private VisualEffect captureAttemptVFX;

    [SerializeField]
    private AudioClip captureAttemptSound;

    [Header("References")]
    [SerializeField]
    private AudioSource audioSource;

    void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.LogWarning(
                    "PlayerFeedback: No AudioSource found or assigned. Added one.",
                    this
                );
            }
        }
    }

    public void PlayCaptureSuccessEffect()
    {
        Debug.Log($"PlayerFeedback ({NetworkObjectId}): Playing Capture Success Effect");
        if (captureSuccessParticles != null)
        {
            captureSuccessParticles.Play();
        }
        if (audioSource != null && captureSuccessSound != null)
        {
            audioSource.PlayOneShot(captureSuccessSound);
        }
    }

    public void PlayCapturedEffect()
    {
        Debug.Log($"PlayerFeedback ({NetworkObjectId}): Playing Captured Effect");
        if (capturedParticles != null)
        {
            capturedParticles.Play();
        }
        if (audioSource != null && capturedSound != null)
        {
            audioSource.PlayOneShot(capturedSound);
        }
    }

    public void PlayCaptureFailEffect()
    {
        Debug.Log($"PlayerFeedback ({NetworkObjectId}): Playing Capture Fail Effect");
        if (captureFailParticles != null)
        {
            captureFailParticles.Play();
        }
        if (audioSource != null && captureFailSound != null)
        {
            audioSource.PlayOneShot(captureFailSound);
        }
    }

    public void PlayCaptureAttemptEffect(Vector3 position)
    {
        Debug.Log(
            $"PlayerFeedback ({NetworkObjectId}): Playing Capture Attempt Effect at {position}"
        );
        if (captureAttemptVFX != null)
        {
            captureAttemptVFX.transform.position = position;
            captureAttemptVFX.Play();
        }
        if (audioSource != null && captureAttemptSound != null)
        {
            AudioSource.PlayClipAtPoint(captureAttemptSound, position);
        }
    }
}