using UnityEngine;

public class AI_bridge_YueFei : MonoBehaviour

{

    // Link your existing EnvironmentManager script here in the Inspector

    public EnvironmentManager_YueFei envManager;

    // Call this method whenever the AI generates a response

    public void OnAIResponseReceived(string emotion)

    {

        if (envManager != null)

        {

            envManager.UpdateEnvironment(emotion);

        }

        else

        {

            Debug.LogError("EnvManager slot is EMPTY on the Bridge!");

        }

    }

    void Update() //To manually test the trigger only

    {

        // Press G to test Gloomy/Rain

        if (Input.GetKeyDown(KeyCode.G))

        {

            OnAIResponseReceived("Sad");

        }

        // Press A to test Angry/Lightning

        if (Input.GetKeyDown(KeyCode.A))

        {

            OnAIResponseReceived("Angry");

        }

        // Press N to return to Normal

        if (Input.GetKeyDown(KeyCode.N))

        {

            OnAIResponseReceived("Normal");

        }

    }

}

