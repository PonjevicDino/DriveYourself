using UnityEngine;
using UnityEngine.Audio;

public class SoundHandler : MonoBehaviour
{
    [Header("Target Vehicle")]
    public RCC_CarControllerV4 carController;

    [Header("Audio Routing")]
    public AudioMixerGroup outputMixer;

    [Header("Audio Clips")]
    public AudioClip windClip;
    public AudioClip brakeClip;
    public AudioClip bumpClip;
    public AudioClip[] skidClips;

    [Header("Tuning Thresholds")]
    [Range(0f, 1f)] public float strongBrakeThreshold = 0.1f;
    public float suddenMovementThreshold = 1.0f; 
    
    [Header("Skid Audio Settings")]
    [Range(0f, 1f)] public float brakeVolume = 0.5f;
    
    [Header("Skid Audio Settings")]
    [Tooltip("Cap the maximum volume of the tire skids.")]
    [Range(0f, 1f)] public float maxSkidVolume = 0.5f;
    public bool disableSkidSounds = false;
    
    [Header("Wind Audio Settings")]
    [Range(0f, 1f)] public float maxWindVolume = 0.1f;
    public bool disableWindSounds = false;

    [Header("EV Sound Generator Settings")]
    public float baseFrequency = 50f;
    public float maxFrequency = 1400f;
    [Range(0f, 1f)] public float maxHumVolume = 0.5f;

    [Header("Debug")]
    public bool debugAudioValues = false;

    // Audio Sources
    private AudioSource windSource;
    private AudioSource brakeSource;
    private AudioSource bumpSource;
    private AudioSource[] skidSources;

    // Movement Tracking (Bumps)
    private float lastSteer;
    private float lastPedals;
    private float lastBumpTime;

    // EV Synthesis Variables
    private double phase;
    private double phase3rd;
    private double phaseSlot;
    private double phaseInverter;
    private float currentFrequency;
    private float currentVolume;
    private float currentThrottle;
    private double sampleRate;

    private void Awake()
    {
        // Forcibly unpause and max out the global Unity audio pipeline
        AudioListener.pause = false;
        AudioListener.volume = 1f;
    }

    private void Start()
    {
        if (carController == null)
        {
            Debug.LogError("[SoundHandler] Please assign the RCC_CarControllerV4 in the Inspector!");
            enabled = false;
            return;
        }

        sampleRate = AudioSettings.outputSampleRate;

        // 1. Wind Setup
        windSource = CreateAudioSource("Wind_Audio", windClip, true, 0.2f);

        // 2. Brake Setup
        brakeSource = CreateAudioSource("Brake_Audio", brakeClip, true, 0.8f);
        brakeSource.transform.SetParent(carController.transform);
        brakeSource.transform.localPosition = Vector3.zero;

        // 3. Bump Setup (One-shot, not looped)
        bumpSource = CreateAudioSource("Bump_Audio", bumpClip, false, 0.9f);
        bumpSource.transform.SetParent(carController.transform);
        bumpSource.transform.localPosition = Vector3.zero;

        // 4. Per-Wheel Skid Setup
        if (carController.AllWheelColliders != null)
        {
            int wheelCount = carController.AllWheelColliders.Length;
            skidSources = new AudioSource[wheelCount];

            for (int i = 0; i < wheelCount; i++)
            {
                AudioClip clipToUse = (skidClips != null && skidClips.Length > 0)
                    ? skidClips[i % skidClips.Length]
                    : null;

                skidSources[i] = CreateAudioSource($"Skid_Audio_Wheel_{i}", clipToUse, true, 1.0f);
                skidSources[i].transform.SetParent(carController.AllWheelColliders[i].transform);
                skidSources[i].transform.localPosition = Vector3.zero;
            }
        }

        // 5. EV Generator Setup
        AudioSource evSource = GetComponent<AudioSource>();
        if (evSource.clip == null)
        {
            evSource.clip = AudioClip.Create("EVDummyClip", (int)sampleRate, 1, (int)sampleRate, false);
            evSource.loop = true;
        }
        evSource.outputAudioMixerGroup = outputMixer;
        evSource.spatialBlend = 0.0f;
        evSource.Play();
    }

    private AudioSource CreateAudioSource(string name, AudioClip clip, bool loop, float spatialBlend)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.spatialBlend = spatialBlend;
        source.minDistance = 5f;
        source.maxDistance = 150f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.volume = 0f;
        source.outputAudioMixerGroup = outputMixer;

        if (clip != null && loop) 
        {
            source.Play();
        }

        return source;
    }

    private void Update()
    {
        if (!carController.engineRunning)
        {
            currentVolume = 0f;
            windSource.volume = 0f;
            brakeSource.volume = 0f;
            if (skidSources != null)
            {
                foreach (var skid in skidSources) if (skid != null) skid.volume = 0f;
            }
            return;
        }
        
        HandleWind();
        HandleBrakes();
        HandleBumps();
        HandleSkids();
        UpdateEVSynthesisParameters();

        if (debugAudioValues)
        {
            Debug.Log($"ESP Active: {carController.ESPAct} | Skid 0 Vol: {(skidSources.Length > 0 ? skidSources[0].volume : 0):F2}");
        }
    }

    private void HandleWind()
    {
        float speedRatio = Mathf.Clamp01(carController.speed / 160f);
        windSource.volume = Mathf.Lerp(windSource.volume, speedRatio * 0.7f * maxWindVolume, Time.deltaTime * 4f);
        if (disableWindSounds)
        {
            windSource.volume = 0.0f;
        }
        windSource.pitch = Mathf.Lerp(0.85f, 1.25f, speedRatio);
    }

    private void HandleBrakes()
    {
        if (carController.brakeInput > strongBrakeThreshold && carController.speed > 3f)
        {
            float brakeIntensity = Mathf.Clamp01(carController.brakeInput);
            brakeSource.volume = Mathf.Lerp(brakeSource.volume, brakeIntensity * brakeVolume, Time.deltaTime * 8f);
            brakeSource.pitch = Mathf.Lerp(0.9f, 1.1f, carController.speed / 50f);
        }
        else
        {
            brakeSource.volume = Mathf.Lerp(brakeSource.volume, 0f, Time.deltaTime * 12f);
        }
    }

    private void HandleBumps()
    {
        if (bumpClip == null) return;

        float currentSteer = carController.steerInput;
        float currentPedals = carController.throttleInput - carController.brakeInput;

        float steerSpeed = Mathf.Abs(currentSteer - lastSteer) / Time.deltaTime;
        float pedalSpeed = Mathf.Abs(currentPedals - lastPedals) / Time.deltaTime;

        if ((steerSpeed > suddenMovementThreshold || pedalSpeed > suddenMovementThreshold) && Time.time > lastBumpTime + 0.25f)
        {
            float intensity = Mathf.Clamp01((steerSpeed + pedalSpeed) * 0.15f);
            bumpSource.PlayOneShot(bumpClip, intensity);
            lastBumpTime = Time.time;
        }

        lastSteer = currentSteer;
        lastPedals = currentPedals;
    }

    private void HandleSkids()
    {
        if (skidSources == null) return;

        for (int i = 0; i < carController.AllWheelColliders.Length; i++)
        {
            if (i >= skidSources.Length || skidSources[i] == null) continue;

            RCC_WheelCollider wc = carController.AllWheelColliders[i];
            
            if (carController.ESPAct || carController.ABSAct || wc.totalSlip > 0.25f) 
            {
                float targetVol = Mathf.Clamp01(wc.totalSlip * 2.0f) * maxSkidVolume;

                if (carController.ABSAct)
                {
                    float absPulse = Mathf.Sin(Time.time * 40f) > 0f ? 1.0f : 0.3f;
                    targetVol *= absPulse;
                }
                else if (carController.ESPAct)
                {
                    float espChirp = Mathf.PerlinNoise(Time.time * 28f, i) > 0.35f ? 1.0f : 0.25f;
                    targetVol *= espChirp;
                }

                skidSources[i].volume = Mathf.Lerp(skidSources[i].volume, targetVol, Time.deltaTime * 15f);
                skidSources[i].pitch = Mathf.Lerp(0.85f, 1.15f, wc.totalSlip);
            }
            else
            {
                skidSources[i].volume = Mathf.Lerp(skidSources[i].volume, 0f, Time.deltaTime * 15f);
            }

            if (disableSkidSounds)
            {
                skidSources[i].volume = 0f;
            }
        }
    }

    private void UpdateEVSynthesisParameters()
    {
        float speedRatio = Mathf.Clamp01(carController.speed / Mathf.Max(carController.maxspeed, 1f));
        currentThrottle = carController.throttleInput;

        float targetFreq = Mathf.Lerp(baseFrequency, maxFrequency, speedRatio) + (currentThrottle * 60f);
        float targetVol = Mathf.Lerp(0.00f, maxHumVolume, (speedRatio * 0.6f) + (currentThrottle * 0.4f));

        if (carController.GetComponent<Rigidbody>().isKinematic)
        {
            targetVol = 0.0f;
        }

        currentFrequency = Mathf.Lerp(currentFrequency, targetFreq, Time.deltaTime * 8f);
        currentVolume = Mathf.Lerp(currentVolume, targetVol, Time.deltaTime * 8f);
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (carController == null || !carController.engineRunning || sampleRate <= 0) return;

        for (int i = 0; i < data.Length; i += channels)
        {
            double freqFundamental = currentFrequency;
            double freq3rd = currentFrequency * 3.0;
            double freqSlot = currentFrequency * 8.0;
            double freqInverter = System.Math.Min(2200.0 + (currentFrequency * 4.0), 7500.0);

            phase += 2.0 * System.Math.PI * freqFundamental / sampleRate;
            phase3rd += 2.0 * System.Math.PI * freq3rd / sampleRate;
            phaseSlot += 2.0 * System.Math.PI * freqSlot / sampleRate;
            phaseInverter += 2.0 * System.Math.PI * freqInverter / sampleRate;

            if (phase > 2.0 * System.Math.PI) phase -= 2.0 * System.Math.PI;
            if (phase3rd > 2.0 * System.Math.PI) phase3rd -= 2.0 * System.Math.PI;
            if (phaseSlot > 2.0 * System.Math.PI) phaseSlot -= 2.0 * System.Math.PI;
            if (phaseInverter > 2.0 * System.Math.PI) phaseInverter -= 2.0 * System.Math.PI;

            float torqueMod = (float)System.Math.Sin(phase * 2.0) * currentThrottle * 0.35f;

            float fundamental = (float)System.Math.Sin(phase + torqueMod);
            float harmonic3rd = (float)System.Math.Sin(phase3rd) * 0.18f;
            float slotWhine = (float)System.Math.Sin(phaseSlot) * 0.12f * (0.4f + currentThrottle * 0.6f);
            float inverterWhine = (float)System.Math.Sin(phaseInverter) * 0.06f;
            float subBass = (float)System.Math.Sin(phase * 0.5) * currentThrottle * 0.25f;

            float sample = (fundamental + harmonic3rd + slotWhine + inverterWhine + subBass) * currentVolume;

            for (int c = 0; c < channels; c++)
            {
                data[i + c] += sample;
            }
        }
    }
}