using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class Book_Navigation_Script : MonoBehaviour
{
    public string sceneToLoad;

    void Update()
    {
        // Mouse click (raycast)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log("Mouse Click Detected");

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                Debug.Log("Hit: " + hit.transform.name);

                if (hit.transform == transform)
                {
                    Debug.Log("Clicked the cube!");
                    SceneManager.LoadScene(sceneToLoad);
                }
            }
        }

        // SPACE key
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("SPACE PRESSED");
        }
    }
}