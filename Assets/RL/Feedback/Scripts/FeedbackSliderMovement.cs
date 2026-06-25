using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FeedbackSliderMovement : MonoBehaviour
{
    private Slider slider;
    private Coroutine activeCoroutine;

    void Start()
    {
        slider = GetComponent<Slider>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            slider.value += 0.01f;
            
            if (activeCoroutine != null) StopCoroutine(activeCoroutine);
            activeCoroutine = StartCoroutine(PressAndHold(KeyCode.RightArrow, 0.5f, 0.0025f));
        }
        
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            slider.value -= 0.01f; 
            
            if (activeCoroutine != null) StopCoroutine(activeCoroutine);
            activeCoroutine = StartCoroutine(PressAndHold(KeyCode.LeftArrow, 0.5f, -0.0025f));
        }
    }

    private IEnumerator PressAndHold(KeyCode key, float waitTime, float increment)
    {
        float timer = 0f;
        
        while (timer < waitTime)
        {
            if (!Input.GetKey(key))
            {
                yield break; 
            }

            timer += Time.deltaTime;
            yield return null;
        }
        
        while (Input.GetKey(key))
        {
            slider.value += increment;
            yield return null;
        }
    }
}
