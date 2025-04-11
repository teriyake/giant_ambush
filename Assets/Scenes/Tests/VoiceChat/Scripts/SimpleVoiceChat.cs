using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(AudioSource))]
public class SimpleVoiceChat : NetworkBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private int recordClipLengthSeconds = 1;
    [SerializeField] private float transmitInterval = 0.1f;
    [SerializeField] private int playbackBufferSizeSeconds = 2;

    private AudioSource audioSource;

    private AudioClip recordingClip;
    private string microphoneDeviceName;
    private int lastSamplePosition = 0;
    private float transmitTimer = 0f;
    private float[] recordingBuffer;

    private AudioClip playbackClip;
    private int playbackWritePosition = 0;
    private Queue<float[]> receivedAudioQueue = new Queue<float[]>();
    private int playbackSampleRate = 0; 
    private object queueLock = new object();

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f; 
        audioSource.loop = false;
        audioSource.playOnAwake = false;

        int samplesPerInterval = (int)(sampleRate * transmitInterval);
        recordingBuffer = new float[samplesPerInterval];
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            StartMicrophone();
        }
        else
        {
            enabled = true;
        }
    }

    void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("SimpleVoiceChat: No microphone devices found!");
            enabled = false;
            return;
        }

        microphoneDeviceName = Microphone.devices[0]; 
        Debug.Log($"SimpleVoiceChat: Using microphone device: {microphoneDeviceName}");

        recordingClip = Microphone.Start(microphoneDeviceName, true, recordClipLengthSeconds, sampleRate);
        lastSamplePosition = 0; 
        transmitTimer = 0f;

        if (recordingClip == null)
        {
            Debug.LogError("SimpleVoiceChat: Microphone.Start failed!");
            enabled = false;
            return;
        }

        while (!(Microphone.GetPosition(microphoneDeviceName) > 0)) { }
        Debug.Log("SimpleVoiceChat: Microphone recording started.");
    }

    void Update()
    {
        if (IsOwner)
        {
            HandleRecording();
        }
        else
        {
            HandlePlayback();
        }
    }

    void HandleRecording()
    {
        if (recordingClip == null || !Microphone.IsRecording(microphoneDeviceName))
        {
            Debug.LogWarning("SimpleVoiceChat: Microphone stopped recording or not initialized. Attempting restart...");
            CleanupRecording();
            StartMicrophone(); 
            if (recordingClip == null)
            {
                enabled = false;
                return;
            }
        }

        transmitTimer += Time.deltaTime;
        if (transmitTimer >= transmitInterval)
        {
            transmitTimer -= transmitInterval;
            TransmitAudio();
        }
    }

    private void TransmitAudio()
    {
        int currentPosition = Microphone.GetPosition(microphoneDeviceName);
        int samplesAvailable;

        if (currentPosition >= lastSamplePosition)
        {
            samplesAvailable = currentPosition - lastSamplePosition;
        }
        else
        {
            samplesAvailable = (recordingClip.samples - lastSamplePosition) + currentPosition;
        }

        if (samplesAvailable > 0)
        {
            int samplesToRead = Mathf.Min(samplesAvailable, recordingBuffer.Length);

            float[] captureBuffer = new float[samplesToRead];
            recordingClip.GetData(captureBuffer, lastSamplePosition);

            SendVoiceDataServerRpc(FloatToByte(captureBuffer), sampleRate);

            lastSamplePosition = (lastSamplePosition + samplesToRead) % recordingClip.samples;
        }
    }

    [ServerRpc]
    private void SendVoiceDataServerRpc(byte[] voiceData, int rate, ServerRpcParams rpcParams = default)
    {
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds
                                      .Where(id => id != rpcParams.Receive.SenderClientId)
                                      .ToArray()
            }
        };

        ReceiveVoiceDataClientRpc(voiceData, rate, rpcParams.Receive.SenderClientId, clientRpcParams);
    }

    [ClientRpc]
    private void ReceiveVoiceDataClientRpc(byte[] voiceData, int rate, ulong sendingClientId, ClientRpcParams clientRpcParams = default)
    {
        float[] receivedSamples = ByteToFloat(voiceData);

        if (receivedSamples.Length > 0)
        {
            if (playbackClip == null || playbackSampleRate != rate)
            {
                InitializePlayback(rate);
            }

            lock (queueLock)
            {
                receivedAudioQueue.Enqueue(receivedSamples);
            }
        }
    }

    void InitializePlayback(int rate)
    {
        if (playbackClip != null) 
        {
            if (audioSource.isPlaying) audioSource.Stop();
            Destroy(playbackClip);
        }

        playbackSampleRate = rate;
        int bufferSamples = playbackSampleRate * playbackBufferSizeSeconds;

        playbackClip = AudioClip.Create("PlaybackClip", bufferSamples, 1, playbackSampleRate, true, OnAudioRead); 
        playbackClip = AudioClip.Create("PlaybackClip", bufferSamples, 1, playbackSampleRate, false); 

        audioSource.clip = playbackClip;
        audioSource.loop = true;
        playbackWritePosition = 0;

        lock(queueLock)
        {
            receivedAudioQueue.Clear();
        }

        Debug.Log($"SimpleVoiceChat: Initialized playback buffer (Rate: {playbackSampleRate}Hz, Size: {playbackBufferSizeSeconds}s)");

        if (!audioSource.isPlaying) 
        {
            audioSource.Play();
            Debug.Log("SimpleVoiceChat: AudioSource playback started.");
        }
    }

    void OnAudioRead(float[] data) { }

    void HandlePlayback()
    {
        if (playbackClip == null || !audioSource.isPlaying)
        {
            return;
        }

        int playbackReadPosition = audioSource.timeSamples; 
        int samplesToWrite = 0;
        List<float[]> chunksToWrite = new List<float[]>();

        lock (queueLock)
        {
            while (receivedAudioQueue.Count > 0)
            {
                float[] chunk = receivedAudioQueue.Peek();

                int nextWritePosition = (playbackWritePosition + chunk.Length) % playbackClip.samples;

                bool writePosAhead = playbackWritePosition >= playbackReadPosition;
                bool nextWritePosAhead = nextWritePosition >= playbackReadPosition;

                bool wouldOvertakeWrap = writePosAhead && !nextWritePosAhead;
                bool wouldOvertakeNoWrap = writePosAhead && nextWritePosAhead && nextWritePosition < playbackWritePosition;
                // (playbackWritePosition - playbackReadPosition + playbackClip.samples) % playbackClip.samples < minimumBufferSamples

                int bufferedSamples = (playbackWritePosition - playbackReadPosition + playbackClip.samples) % playbackClip.samples;
                int desiredMinBufferSamples = playbackSampleRate / 4;

                if (bufferedSamples < playbackClip.samples - chunk.Length - desiredMinBufferSamples || bufferedSamples < desiredMinBufferSamples/2 )
                {
                    chunksToWrite.Add(receivedAudioQueue.Dequeue());
                    samplesToWrite += chunk.Length;
                    playbackWritePosition = nextWritePosition; 
                }
                else
                {
                    // Debug.LogWarning("Playback buffer full or write head too close to read head. Waiting.");
                    break; 
                }
            }
        }

        if (chunksToWrite.Count > 0)
        {
            float[] combinedSamples = new float[samplesToWrite];
            int combinedOffset = 0;
            foreach(var chunk in chunksToWrite)
            {
                System.Buffer.BlockCopy(chunk, 0, combinedSamples, combinedOffset * sizeof(float), chunk.Length * sizeof(float));
                combinedOffset += chunk.Length;
            }

            int writeStartPosition = (playbackWritePosition - samplesToWrite + playbackClip.samples) % playbackClip.samples;

            playbackClip.SetData(combinedSamples, writeStartPosition);
            // Debug.Log($"Wrote {samplesToWrite} samples at pos {writeStartPosition}. ReadPos: {playbackReadPosition}, New WritePos: {playbackWritePosition}");
        }

        const int maxQueueSize = 20;
        lock(queueLock)
        {
            while (receivedAudioQueue.Count > maxQueueSize)
            {
                receivedAudioQueue.Dequeue();
                Debug.LogWarning("Voice chat queue too large, dropping oldest packet.");
            }
        }
    }

    private byte[] FloatToByte(float[] floatArray)
    {
        byte[] byteArray = new byte[floatArray.Length * sizeof(float)];
        System.Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    private float[] ByteToFloat(byte[] byteArray)
    {
        if (byteArray == null || byteArray.Length % sizeof(float) != 0)
        {
            Debug.LogError("ByteToFloat: Invalid byte array length.");
            return new float[0];
        }
        float[] floatArray = new float[byteArray.Length / sizeof(float)];
        System.Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);
        return floatArray;
    }

    void CleanupRecording()
    {
        if (microphoneDeviceName != null && Microphone.IsRecording(microphoneDeviceName))
        {
            Microphone.End(microphoneDeviceName);
        }
        if (recordingClip != null)
        {
            Destroy(recordingClip);
            recordingClip = null;
        }
        microphoneDeviceName = null;
    }

    void CleanupPlayback()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        if (playbackClip != null)
        {
            Destroy(playbackClip);
            playbackClip = null;
        }
        lock(queueLock)
        {
            receivedAudioQueue.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            CleanupRecording();
        }
        else
        {
            CleanupPlayback();
        }
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (IsOwner)
        {
            CleanupRecording();
        }
        else
        {
            CleanupPlayback();
        }
        base.OnDestroy();
    }
}
