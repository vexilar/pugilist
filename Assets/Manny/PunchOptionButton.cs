using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PunchOptionButton : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private string punchId; // "Jab", "Cross", etc.
    public string PunchId => punchId;

    [Header("UI")]
    [SerializeField] private Button button;
    [SerializeField] private Image armedHighlight; // border/glow object
    [SerializeField] private TMP_Text label;

    private PunchSelectUI owner;

    void Reset()
    {
        button = GetComponent<Button>();
    }

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(OnClicked);

        SetArmed(false);
    }

    public void SetOwner(PunchSelectUI ui)
    {
        owner = ui;
    }

    public void SetArmed(bool armed)
    {
        if (armedHighlight != null)
            armedHighlight.enabled = armed;

        // Optional: change label style slightly
        if (label != null)
            label.fontStyle = armed ? FontStyles.Bold : FontStyles.Normal;
    }

    private void OnClicked()
    {
        owner?.HandleOptionPressed(this);
    }
}
