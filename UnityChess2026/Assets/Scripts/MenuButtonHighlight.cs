using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class MenuButtonHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("References")]
    [SerializeField] private Image targetImage;
    [SerializeField] private TMP_Text targetText;
    [SerializeField] private Button button;

    [Header("Colors")]
    [SerializeField] private Color normalBackgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f);
    [SerializeField] private Color highlightedBackgroundColor = Color.white;
    [SerializeField] private Color normalTextColor = Color.white;
    [SerializeField] private Color highlightedTextColor = Color.black;

    [Header("Behavior")]
    [SerializeField] private bool disableBuiltinTransition = true;
    [SerializeField] private bool autoSelectOnHover = true;
    [SerializeField] private bool highlightKeyboardSelection = false;

    private bool isHovered;
    private bool isSelected;
    private bool isPressed;

    private void Reset()
    {
        button = GetComponent<Button>();
        targetImage = GetComponent<Image>();
        targetText = GetComponentInChildren<TMP_Text>(true);
    }

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        if (targetText == null)
        {
            targetText = GetComponentInChildren<TMP_Text>(true);
        }

        if (disableBuiltinTransition && button != null)
        {
            button.transition = Selectable.Transition.None;
        }

        ApplyVisualState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;

        if (autoSelectOnHover && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }

        ApplyVisualState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyVisualState();
    }

    public void OnSelect(BaseEventData eventData)
    {
        isSelected = true;
        ApplyVisualState();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        ApplyVisualState();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        ApplyVisualState();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        bool active = isHovered || isPressed || (highlightKeyboardSelection && isSelected);

        if (targetImage != null)
        {
            targetImage.color = active ? highlightedBackgroundColor : normalBackgroundColor;
        }

        if (targetText != null)
        {
            targetText.color = active ? highlightedTextColor : normalTextColor;
        }
    }
}
