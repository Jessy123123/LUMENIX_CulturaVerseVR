using UnityEngine;

public class PuteriTalk : MonoBehaviour
{
    Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            animator.Play("stand-talk-378997");
            Debug.Log("Talking animation triggered");
        }
    }
}