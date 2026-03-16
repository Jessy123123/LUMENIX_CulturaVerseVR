using UnityEngine;
using UnityEngine.Rendering; // Required for Post-Processing
using UnityEngine.Rendering.Universal; // Use .HighDefinition if using HDRP

public class EnvironmentManager : MonoBehaviour
{
    public GameObject normalEnvironment;
    public GameObject gloomyEnvironment;
    public GameObject angryEnvironment;
    public ParticleSystem rainParticles; // Drag your rain here in Inspector

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
