using UnityEngine;
using UnityEngine.UI;

public class ScrollViewResetter : MonoBehaviour
{
    private ScrollRect scrollRect;

    private void OnEnable()
    {
        if(scrollRect == null)
            scrollRect = GetComponent<ScrollRect>();

        ResetScroll();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        scrollRect = GetComponent<ScrollRect>();
    }

    private void ResetScroll()
    {
        // 1 = Top, 0 = Bottom
        scrollRect.verticalNormalizedPosition = 1f;
    }
}
