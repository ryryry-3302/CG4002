using UnityEngine;
using TMPro;
using UnityEngine.UI;

[ExecuteAlways]
public class GameUIThemeManager : MonoBehaviour
{
    [Header("Theme Colors")]
    public Color titleColor = new Color(1f, 0.843f, 0f, 1f); // Gold FFD700
    public Color buttonTextColor = Color.white;
    public Color buttonNormalColor = new Color(0.1f, 0.1f, 0.1f, 0.8f); // Dark Grey
    public Color buttonHoverColor = new Color(1f, 0.843f, 0f, 1f); // Gold

    public void ApplyTheme()
    {
        // 1. Find the Root Canvas (so it works even if you attached it to the Panel)
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) 
        {
            // Fallback if not inside a canvas
            rootCanvas = GetComponent<Canvas>(); 
        }
        if (rootCanvas == null) return;

        // 2. Find Title and Text
        TextMeshProUGUI[] allTexts = rootCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var text in allTexts)
        {
            // Smart Detection: It's a title if name contains "Title" OR if the font is huge (>40)
            if (text.gameObject.name.Contains("Title") || text.fontSize > 40)
            {
                text.color = titleColor;
            }
            else
            {
                text.color = buttonTextColor;
            }
        }

        // 3. Find Buttons
        Button[] buttons = rootCanvas.GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = buttonNormalColor;
            }

            // Set ColorBlock for interactions
            ColorBlock colors = btn.colors;
            colors.normalColor = buttonNormalColor;
            colors.highlightedColor = buttonHoverColor; // Gold when hovering
            colors.selectedColor = buttonHoverColor;
            colors.pressedColor = new Color(0.8f, 0.6f, 0f, 1f); // Darker Gold
            btn.colors = colors;
        }
    }

    void OnValidate()
    {
        // Auto apply when values change in editor
        ApplyTheme();
    }
}
