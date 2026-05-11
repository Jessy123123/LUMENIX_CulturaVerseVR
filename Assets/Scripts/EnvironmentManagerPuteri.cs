using UnityEngine;
using System.Collections; // Required for Coroutines
using UnityEngine.Rendering; // Required for Post-Processing
using UnityEngine.Rendering.Universal; // Use .HighDefinition if using HDRP



public class EnvironmentManagerPuteri : MonoBehaviour
{
    private EmotionDetector emotionDetector;
    public Volume volume;

    private ColorAdjustments colorAdjustments;
    private Vignette vignette;

    public GameObject normalEnvironment;
    public GameObject gloomyEnvironment;
    public GameObject angryEnvironment;
    public ParticleSystem rainParticles; // Drag your rain here in Inspector

    public Material angrySkybox;
    public Material normalSkybox;
    public Material sadSkybox;

    public Light lightningLight; // Assign this in the Inspector
    string lastEmotion = "";

    // Call this function when you want a flash
    /*public void TriggerThunder()
    {
        StartCoroutine(FlashLightning());
    }

    IEnumerator FlashLightning()
    {
        lightningLight.enabled = true;
        yield return new WaitForSeconds(0.1f); // Quick flicker
        lightningLight.enabled = false;
    }*/
    void Start()
    {
        emotionDetector = GetComponent<EmotionDetector>();
        if (volume != null && volume.profile != null)
        {
            volume.profile.TryGet(out colorAdjustments);
            volume.profile.TryGet(out vignette);
        }
    }
    void Update()
    {
        if (emotionDetector == null) return;

        string current = emotionDetector.currentEmotion;

        if (current != lastEmotion)
        {
            UpdateEnvironment(current);
            lastEmotion = current;
        }
    }
    public void UpdateEnvironment(string emotion)
    {
        if (emotion == "Sad")
        {
            normalEnvironment.SetActive(false);
            gloomyEnvironment.SetActive(true);
            ApplyGloomyEffect(1f);
            if (rainParticles != null) rainParticles.Play();
            RenderSettings.skybox = sadSkybox; // Changes the sky color
            DynamicGI.UpdateEnvironment();
        }
        else if (emotion == "Angry")
        {
            normalEnvironment.SetActive(false);
            gloomyEnvironment.SetActive(false);
            angryEnvironment.SetActive(true);
            ApplyGloomyEffect(0.6f); // slightly gloomy but more intense vibe
            if (rainParticles != null) rainParticles.Stop();
            RenderSettings.skybox = angrySkybox; // Changes the sky color
            DynamicGI.UpdateEnvironment();       // Refreshes the lighting
            //TriggerThunder(); // Flash lightning when angry
        }
        else
        {
            normalEnvironment.SetActive(true);
            gloomyEnvironment.SetActive(false);
            angryEnvironment.SetActive(false);
            RenderSettings.skybox = normalSkybox;
            DynamicGI.UpdateEnvironment();       // Refreshes the lighting
            ApplyGloomyEffect(0f);

            if (rainParticles != null) rainParticles.Stop();
        }
    }
    void ApplyGloomyEffect(float intensity)
    {
        if (colorAdjustments == null || vignette == null) return;

        float targetSaturation = Mathf.Lerp(0f, -60f, intensity);
        float targetExposure = Mathf.Lerp(0f, -1f, intensity);
        float targetVignette = Mathf.Lerp(0.2f, 0.5f, intensity);

        colorAdjustments.saturation.value =
            Mathf.Lerp(colorAdjustments.saturation.value, targetSaturation, Time.deltaTime * 2f);

        colorAdjustments.postExposure.value =
            Mathf.Lerp(colorAdjustments.postExposure.value, targetExposure, Time.deltaTime * 2f);

        vignette.intensity.value =
            Mathf.Lerp(vignette.intensity.value, targetVignette, Time.deltaTime * 2f);
    }
}
