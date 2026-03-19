using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// EnvironmentManagerYueFei
///
/// Controls the battlefield environment for the Yue Fei scene.
/// Assign all references in the Inspector — nothing is required,
/// so you can wire up only what your scene currently has and add
/// the rest later without breaking anything.
///
/// States:
///   Normal   — Golden dawn, calm battlefield
///   Sad      — Grey rain, mourning fallen soldiers
///   Angry    — Red sky, fire and smoke, battle fury
///   Spiritual — Ethereal glow, petals, Man Jiang Hong moment
///
/// Called by AIBridgeYueFei.OnAIResponseReceived(emotion)
/// </summary>
public class EnvironmentManagerYueFei : MonoBehaviour
{
    // ── Lighting ──────────────────────────────────────────────────────────
    [Header("Lighting")]
    public Light directionalLight;
    public float transitionDuration = 2.0f;

    // ── Particle Systems ──────────────────────────────────────────────────
    [Header("Particles — assign in Inspector")]
    public ParticleSystem rainParticles;        // Sad
    public ParticleSystem fireEmbersParticles;  // Angry
    public ParticleSystem smokeParticles;       // Angry
    public ParticleSystem dustParticles;        // Normal (light wind)
    public ParticleSystem petalParticles;       // Spiritual
    public ParticleSystem lightRaysParticles;   // Spiritual

    // ── Audio ─────────────────────────────────────────────────────────────
    [Header("Audio Sources — assign in Inspector")]
    public AudioSource ambienceSource;     // loops battlefield ambience
    public AudioSource musicSource;        // loops background music

    [Header("Audio Clips")]
    public AudioClip normalAmbience;       // distant army, wind
    public AudioClip sadAmbience;          // rain, distant weeping erhu
    public AudioClip angryAmbience;        // war drums, battle cries
    public AudioClip spiritualAmbience;    // epic orchestral swell / Man Jiang Hong theme

    // ── Fog ───────────────────────────────────────────────────────────────
    [Header("Fog")]
    public bool controlFog = true;

    // ── Skybox Materials ──────────────────────────────────────────────────
    [Header("Skybox Materials (optional)")]
    public Material skyboxNormal;
    public Material skyboxSad;
    public Material skyboxAngry;
    public Material skyboxSpiritual;

    // ── State tracking ────────────────────────────────────────────────────
    private string currentState = "Normal";
    private Coroutine transitionCoroutine;

    // ─────────────────────────────────────────────────────────────────────
    //  Colour definitions per state
    // ─────────────────────────────────────────────────────────────────────

    // Directional light colour
    private static readonly Color lightNormal = new Color(1.00f, 0.85f, 0.60f); // warm golden dawn
    private static readonly Color lightSad = new Color(0.45f, 0.50f, 0.65f); // cold grey-blue
    private static readonly Color lightAngry = new Color(1.00f, 0.25f, 0.10f); // harsh red
    private static readonly Color lightSpiritual = new Color(1.00f, 0.95f, 0.80f); // soft white-gold

    // Light intensity
    private const float intensityNormal = 1.2f;
    private const float intensitySad = 0.5f;
    private const float intensityAngry = 1.8f;
    private const float intensitySpiritual = 0.9f;

    // Ambient light colour
    private static readonly Color ambientNormal = new Color(0.35f, 0.30f, 0.20f);
    private static readonly Color ambientSad = new Color(0.10f, 0.12f, 0.18f);
    private static readonly Color ambientAngry = new Color(0.25f, 0.08f, 0.05f);
    private static readonly Color ambientSpiritual = new Color(0.40f, 0.38f, 0.30f);

    // Fog colour and density
    private static readonly Color fogNormal = new Color(0.70f, 0.65f, 0.50f);
    private static readonly Color fogSad = new Color(0.20f, 0.22f, 0.30f);
    private static readonly Color fogAngry = new Color(0.30f, 0.08f, 0.05f);
    private static readonly Color fogSpiritual = new Color(0.90f, 0.88f, 0.80f);

    private const float fogDensityNormal = 0.008f;
    private const float fogDensitySad = 0.030f;
    private const float fogDensityAngry = 0.020f;
    private const float fogDensitySpiritual = 0.005f;

    // ─────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        // Start in Normal state immediately with no transition
        ApplyStateImmediate("Normal");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Public entry point — called by AIBridgeYueFei
    // ─────────────────────────────────────────────────────────────────────

    public void UpdateEnvironment(string emotion)
    {
        if (emotion == currentState) return;

        currentState = emotion;
        Debug.Log("EnvironmentManagerYueFei: switching to " + emotion);

        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        transitionCoroutine = StartCoroutine(TransitionTo(emotion));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Smooth transition coroutine
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator TransitionTo(string state)
    {
        // Swap skybox instantly (material swap has no lerp)
        SwapSkybox(state);

        // Start correct particles
        UpdateParticles(state);

        // Fade audio
        UpdateAudio(state);

        // Lerp lighting and fog over transitionDuration seconds
        Color startLight = directionalLight != null ? directionalLight.color : Color.white;
        float startIntens = directionalLight != null ? directionalLight.intensity : 1f;
        Color startAmbient = RenderSettings.ambientLight;
        Color startFog = RenderSettings.fogColor;
        float startFogDen = RenderSettings.fogDensity;

        Color targetLight;
        float targetIntens;
        Color targetAmbient;
        Color targetFog;
        float targetFogDen;

        GetStateValues(state,
            out targetLight, out targetIntens,
            out targetAmbient,
            out targetFog, out targetFogDen);

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);

            if (directionalLight != null)
            {
                directionalLight.color = Color.Lerp(startLight, targetLight, t);
                directionalLight.intensity = Mathf.Lerp(startIntens, targetIntens, t);
            }

            RenderSettings.ambientLight = Color.Lerp(startAmbient, targetAmbient, t);

            if (controlFog)
            {
                RenderSettings.fogColor = Color.Lerp(startFog, targetFog, t);
                RenderSettings.fogDensity = Mathf.Lerp(startFogDen, targetFogDen, t);
            }

            yield return null;
        }

        // Snap to exact final values
        if (directionalLight != null)
        {
            directionalLight.color = targetLight;
            directionalLight.intensity = targetIntens;
        }
        RenderSettings.ambientLight = targetAmbient;
        if (controlFog)
        {
            RenderSettings.fogColor = targetFog;
            RenderSettings.fogDensity = targetFogDen;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Apply state instantly (used on Start)
    // ─────────────────────────────────────────────────────────────────────

    void ApplyStateImmediate(string state)
    {
        Color targetLight;
        float targetIntens;
        Color targetAmbient;
        Color targetFog;
        float targetFogDen;

        GetStateValues(state,
            out targetLight, out targetIntens,
            out targetAmbient,
            out targetFog, out targetFogDen);

        if (directionalLight != null)
        {
            directionalLight.color = targetLight;
            directionalLight.intensity = targetIntens;
        }

        RenderSettings.ambientLight = targetAmbient;

        if (controlFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = targetFog;
            RenderSettings.fogDensity = targetFogDen;
        }

        SwapSkybox(state);
        UpdateParticles(state);
        UpdateAudio(state);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  State value lookup
    // ─────────────────────────────────────────────────────────────────────

    void GetStateValues(string state,
        out Color light, out float intensity,
        out Color ambient,
        out Color fog, out float fogDensity)
    {
        switch (state)
        {
            case "Sad":
                light = lightSad;
                intensity = intensitySad;
                ambient = ambientSad;
                fog = fogSad;
                fogDensity = fogDensitySad;
                break;

            case "Angry":
                light = lightAngry;
                intensity = intensityAngry;
                ambient = ambientAngry;
                fog = fogAngry;
                fogDensity = fogDensityAngry;
                break;

            case "Spiritual":
                light = lightSpiritual;
                intensity = intensitySpiritual;
                ambient = ambientSpiritual;
                fog = fogSpiritual;
                fogDensity = fogDensitySpiritual;
                break;

            default: // Normal
                light = lightNormal;
                intensity = intensityNormal;
                ambient = ambientNormal;
                fog = fogNormal;
                fogDensity = fogDensityNormal;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Particle control
    // ─────────────────────────────────────────────────────────────────────

    void UpdateParticles(string state)
    {
        // Stop all first
        StopParticle(rainParticles);
        StopParticle(fireEmbersParticles);
        StopParticle(smokeParticles);
        StopParticle(dustParticles);
        StopParticle(petalParticles);
        StopParticle(lightRaysParticles);

        switch (state)
        {
            case "Normal":
                PlayParticle(dustParticles);
                break;

            case "Sad":
                PlayParticle(rainParticles);
                break;

            case "Angry":
                PlayParticle(fireEmbersParticles);
                PlayParticle(smokeParticles);
                break;

            case "Spiritual":
                PlayParticle(petalParticles);
                PlayParticle(lightRaysParticles);
                break;
        }
    }

    void PlayParticle(ParticleSystem ps)
    {
        if (ps != null && !ps.isPlaying)
        {
            ps.gameObject.SetActive(true);
            ps.Play();
        }
    }

    void StopParticle(ParticleSystem ps)
    {
        if (ps != null && ps.isPlaying)
        {
            ps.Stop();
            ps.gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Audio control
    // ─────────────────────────────────────────────────────────────────────

    void UpdateAudio(string state)
    {
        AudioClip target = null;

        switch (state)
        {
            case "Normal": target = normalAmbience; break;
            case "Sad": target = sadAmbience; break;
            case "Angry": target = angryAmbience; break;
            case "Spiritual": target = spiritualAmbience; break;
        }

        if (ambienceSource != null && target != null)
        {
            if (ambienceSource.clip != target)
            {
                ambienceSource.clip = target;
                ambienceSource.loop = true;
                ambienceSource.Play();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Skybox swap
    // ─────────────────────────────────────────────────────────────────────

    void SwapSkybox(string state)
    {
        Material sky = null;

        switch (state)
        {
            case "Normal": sky = skyboxNormal; break;
            case "Sad": sky = skyboxSad; break;
            case "Angry": sky = skyboxAngry; break;
            case "Spiritual": sky = skyboxSpiritual; break;
        }

        if (sky != null)
            RenderSettings.skybox = sky;
    }
}