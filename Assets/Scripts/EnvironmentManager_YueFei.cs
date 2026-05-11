using UnityEngine;
using System.Collections; // Required for Coroutines
using UnityEngine.Rendering; // Required for Post-Processing
using UnityEngine.Rendering.Universal; // Use .HighDefinition if using HDRP
 
public class EnvironmentManager_YueFei : MonoBehaviour
{
    public EmotionDetector emotionDetector;
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
    //public void TriggerThunder()
    //{
    //    StartCoroutine(FlashLightning());
    //}

    //IEnumerator FlashLightning()
    //{
    //    lightningLight.enabled = true;
    //    yield return new WaitForSeconds(0.1f); // Quick flicker
    //    lightningLight.enabled = false;
    //}

    void Start()
    {
        emotionDetector = GetComponent<EmotionDetector>();
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
            angryEnvironment.SetActive(false);
            if (rainParticles != null) rainParticles.Play();
            RenderSettings.skybox = sadSkybox; // Changes the sky color
            DynamicGI.UpdateEnvironment();

        }
        else if (emotion == "Angry")
        {
            normalEnvironment.SetActive(false);
            gloomyEnvironment.SetActive(false);
            angryEnvironment.SetActive(true);
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
 
            if (rainParticles != null) rainParticles.Stop();
        }
    }
}
