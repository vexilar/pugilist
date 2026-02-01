using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PunchSelectUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private List<PunchOptionButton> options = new();

    [Header("Fade Timing")]
    [SerializeField] private float fadeInTime = 0.25f;   // quick
    [SerializeField] private float fadeOutTime = 0.125f;  // very fast

    public bool IsOpen { get; private set; }

    // Which option is currently "armed" (selected once)
    private PunchOptionButton armed;

    // Event you can hook to your combat/camera system
    public event Action<string> OnPunchConfirmed;

    Coroutine fadeRoutine;

    void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        foreach (var opt in options)
        {
            opt.SetOwner(this);
        }

        SetVisibleImmediate(false);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;

        armed = null;
        ClearAllHighlights();

        StartFade(visible: true, fadeInTime);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        StartFade(visible: false, fadeOutTime);
    }

    // Called by PunchOptionButton when clicked/pressed
    public void HandleOptionPressed(PunchOptionButton pressed)
    {
        if (!IsOpen) return;

        // 1st press: arm it (highlight)
        if (armed != pressed)
        {
            armed = pressed;
            ClearAllHighlights();
            pressed.SetArmed(true);
            return;
        }

        // 2nd press on same option: confirm
        OnPunchConfirmed?.Invoke(pressed.PunchId);
        Close();
    }

    private void ClearAllHighlights()
    {
        foreach (var opt in options)
            opt.SetArmed(false);
    }

    private void StartFade(bool visible, float duration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(visible, duration));
    }

    private IEnumerator FadeRoutine(bool visible, float duration)
    {
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;

        float start = canvasGroup.alpha;
        float end = visible ? 1f : 0f;

        if (duration <= 0f)
        {
            canvasGroup.alpha = end;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // UI should ignore timescale for turn-based pauses
            float a = Mathf.Clamp01(t / duration);
            canvasGroup.alpha = Mathf.Lerp(start, end, a);
            yield return null;
        }

        canvasGroup.alpha = end;

        // If we finished hiding, disable raycasts/interactable hard
        if (!visible)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    private void SetVisibleImmediate(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
