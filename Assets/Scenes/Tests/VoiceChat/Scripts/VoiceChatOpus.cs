using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityOpus;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(AudioSource))]
public class VoiceChatOpus : NetworkBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private SamplingFrequency sampleRate = SamplingFrequency.Frequency_16000;
    [SerializeField] private int audioClipLengthSeconds = 1;
    [SerializeField] private float transmitInterval = 0.1f;
    [SerializeField] [Range(6000, 32000)] private int opusBitrate = 24000;

    private AudioSource audioSource;
    private AudioClip recordingClip;
    private string microphoneDeviceName;

    private Encoder opusEncoder;
    private Decoder opusDecoder;
    private float[] decodeBuffer;
    private int decodeBufferSizeSamples;

    private float[] captureBuffer;
    private int lastSamplePosition = 0;
    private float transmitTimer = 0f;

    private byte[] encodedOutputBuffer = new byte[4096];

    [Header("Silence Detection")]
    [SerializeField] private bool useSilenceDetection = true;
    [SerializeField] private float silenceThreshold = 0.01f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;
        audioSource.loop = false;
        audioSource.playOnAwake = false;

        try
        {
            opusDecoder = new Decoder(sampleRate, NumChannels.Mono);
            decodeBufferSizeSamples = (int)(GetSampleRateInt(sampleRate) * 0.12f);
            decodeBuffer = new float[decodeBufferSizeSamples];
            Debug.Log($"SimpleVoiceChat: Opus Decoder initialized (SampleRate: {sampleRate}, Channels: 1). Decode buffer size: {decodeBufferSizeSamples} samples.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleVoiceChat: Failed to initialize Opus Decoder! Error: {e.Message}", this);
            enabled = false;
        }

        int captureBufferSize = (int)(GetSampleRateInt(sampleRate) * transmitInterval * 1.1f);
        captureBuffer = new float[captureBufferSize];
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("SimpleVoiceChat: No microphone devices found!", this);
                enabled = false;
                return;
            }
            microphoneDeviceName = Microphone.devices[0];
            Debug.Log($"SimpleVoiceChat: Using microphone device: {microphoneDeviceName}");

            recordingClip = Microphone.Start(microphoneDeviceName, true, audioClipLengthSeconds, GetSampleRateInt(sampleRate));
            if (recordingClip == null)
            {
                Debug.LogError("SimpleVoiceChat: Microphone.Start failed!", this);
                enabled = false;
                return;
            }
            Debug.Log("SimpleVoiceChat: Microphone recording started.");

            try
            {
                opusEncoder = new Encoder(sampleRate, NumChannels.Mono, OpusApplication.VoIP);
                opusEncoder.Bitrate = opusBitrate;
                Debug.Log($"SimpleVoiceChat: Opus Encoder initialized (Bitrate: {opusEncoder.Bitrate}).");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SimpleVoiceChat: Failed to initialize Opus Encoder! Error: {e.Message}", this);
                if (recordingClip != null && Microphone.IsRecording(microphoneDeviceName)) Microphone.End(microphoneDeviceName);
                if (recordingClip != null) Destroy(recordingClip);
                recordingClip = null;
            }
        }
        else
        {
            enabled = false;
        }
    }

    void Update()
    {
        if (!IsOwner || recordingClip == null || opusEncoder == null || !Microphone.IsRecording(microphoneDeviceName))
        {
            if (IsOwner && recordingClip != null && !Microphone.IsRecording(microphoneDeviceName)) 
            {
                Debug.LogWarning("SimpleVoiceChat: Microphone stopped recording. Please check permissions or device.");
            }
            if (IsOwner && opusEncoder == null && recordingClip != null) 
            {
                Debug.LogError("SimpleVoiceChat: Opus Encoder is not initialized. Cannot send audio.");
            }
            return;
        }

        transmitTimer += Time.deltaTime;
        if (transmitTimer >= transmitInterval)
        {
            transmitTimer = 0f;
            TransmitAudio();
        }
    }

    private void TransmitAudio()
    {
        int currentPosition = Microphone.GetPosition(microphoneDeviceName);
        int samplesAvailable;

        if (currentPosition < lastSamplePosition)
        {
            samplesAvailable = (recordingClip.samples - lastSamplePosition) + currentPosition;
        }
        else
        {
            samplesAvailable = currentPosition - lastSamplePosition;
        }

        if (samplesAvailable <= 0)
        {
            lastSamplePosition = currentPosition; 
            return;
        }

        float[] samplesToEncode = new float[samplesAvailable];
        recordingClip.GetData(samplesToEncode, lastSamplePosition);

        lastSamplePosition = currentPosition;

        if (useSilenceDetection && IsSilent(samplesToEncode, samplesToEncode.Length, silenceThreshold)) 
        {
            return; 
        }

        try
        {
            int encodedLength = opusEncoder.Encode(samplesToEncode, encodedOutputBuffer);

            if (encodedLength > 0)
            {
                byte[] dataToSend = new byte[encodedLength];
                System.Array.Copy(encodedOutputBuffer, 0, dataToSend, 0, encodedLength);

                SendVoiceDataServerRpc(dataToSend);
            }
            else 
            {
                // Debug.LogWarning($"Opus Encoder returned {encodedLength} bytes.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleVoiceChat: Opus encoding failed! Error: {e.Message}", this);
        }
    }

    private bool IsSilent(float[] buffer, int length, float threshold)
    {
        if (length == 0) return true;
        float sum = 0;
        for (int i = 0; i < length; i++)
        {
            sum += Mathf.Abs(buffer[i]);
        }
        return (sum / length) < threshold;
    }

    private int GetSampleRateInt(SamplingFrequency sampleRate)
    {
        switch (sampleRate)
        {
            case SamplingFrequency.Frequency_8000:
            {
                return 8000;
            }
            case SamplingFrequency.Frequency_12000:
            {
                return 12000;
            }
            case SamplingFrequency.Frequency_16000:
            {
                return 16000;
            }
            case SamplingFrequency.Frequency_24000:
            {
                return 24000;
            }
            case SamplingFrequency.Frequency_48000:
            {
                return 48000;
            }
            default: return 0;
        }
    }

    [ServerRpc]
    private void SendVoiceDataServerRpc(byte[] compressedVoiceData, ServerRpcParams rpcParams = default)
    {
        ReceiveVoiceDataClientRpc(compressedVoiceData, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveVoiceDataClientRpc(byte[] compressedVoiceData, ulong sendingClientId)
    {
        if (sendingClientId == NetworkManager.Singleton.LocalClientId || opusDecoder == null)
        {
            return;
        }

        try
        {
            int samplesDecoded = opusDecoder.Decode(compressedVoiceData, compressedVoiceData.Length, decodeBuffer);

            if (samplesDecoded > 0)
            {
                AudioClip playbackChunk = AudioClip.Create("ReceivedChunk", samplesDecoded, 1, GetSampleRateInt(sampleRate), false);
                playbackChunk.SetData(decodeBuffer, 0);

                if (audioSource != null)
                {
                    audioSource.PlayOneShot(playbackChunk);
                }
                else 
                {
                    Debug.LogWarning("SimpleVoiceChat: AudioSource is null on receiving client, cannot play audio.");
                    Destroy(playbackChunk);
                }
            }
            else 
            {
                Debug.LogWarning($"SimpleVoiceChat: Opus decode returned 0 or negative samples ({samplesDecoded}) from client {sendingClientId}. Packet size: {compressedVoiceData.Length} bytes.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleVoiceChat: Opus decoding failed for data from {sendingClientId}! Error: {e.Message}. Packet size: {compressedVoiceData.Length} bytes.", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        CleanupResources();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        CleanupResources();
        base.OnDestroy();
    }

    private void CleanupResources()
    {
        if (IsOwner && microphoneDeviceName != null && Microphone.IsRecording(microphoneDeviceName))
        {
            Debug.Log("SimpleVoiceChat: Stopping microphone recording.");
            Microphone.End(microphoneDeviceName);
        }
        if (recordingClip != null) 
        {
            Destroy(recordingClip);
            recordingClip = null;
        }

        if (opusEncoder != null)
        {
            opusEncoder.Dispose();
            opusEncoder = null;
            Debug.Log("SimpleVoiceChat: Opus Encoder disposed.");
        }
        if (opusDecoder != null)
        {
            opusDecoder.Dispose();
            opusDecoder = null;
            Debug.Log("SimpleVoiceChat: Opus Decoder disposed.");
        }
    }
}
