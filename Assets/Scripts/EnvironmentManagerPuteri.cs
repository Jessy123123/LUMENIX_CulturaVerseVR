using UnityEngine;
using UnityEngine.Rendering; // Required for Post-Processing
using UnityEngine.Rendering.Universal; // Use .HighDefinition if using HDRP

public class EnvironmentManager : MonoBehaviour
{
    public GameObject normalEnvironment;
    public GameObject gloomyEnvironment;
    public GameObject angryEnvironment;
    public ParticleSystem rainParticles; // Drag your rain here in Inspector

    public Material angrySkybox;
    public Material normalSkybox;

    public Light lightningLight; // Assign this in the Inspector

    // Call this function when you want a flash
    public void TriggerThunder()
    {
        StartCoroutine(FlashLightning());
    }

    IEnumerator FlashLightning()
    {
        lightningLight.enabled = true;
        yield return new WaitForSeconds(0.1f); // Quick flicker
        lightningLight.enabled = false;
    }

    public void UpdateEnvironment(string emotion)
    {
        if (emotion == "Sad")
        {
            normalEnvironment.SetActive(false);
            gloomyEnvironment.SetActive(true);

            if (rainParticles != null) rainParticles.Play();
        }
        else if (emotion == "Angry")
        {
            normalEnvironment.SetActive(false);
            gloomyEnvironment.SetActive(false);
            angryEnvironment.SetActive(true);
            if (rainParticles != null) rainParticles.Stop();
            RenderSettings.skybox = angrySkybox; // Changes the sky color
            DynamicGI.UpdateEnvironment();       // Refreshes the lighting
        }
        else
        {
            normalEnvironment.SetActive(true);
            gloomyEnvironment.SetActive(false);
            angryEnvironment.SetActive(false);

            if (rainParticles != null) rainParticles.Stop();
        }
    }
}
