using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    public Texture2D handCursorTexture;
    private ICursorStrategy currentStrategy;

    private readonly ICursorStrategy defaultStrategy = new DefaultCursorStrategy();
    private ICursorStrategy handStrategy;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        Vector2 hotspot = Vector2.zero;
        handStrategy = new HandCursorStrategy(handCursorTexture, hotspot);

        SetDefault();
    }

    public void SetHand()
    {
        currentStrategy = handStrategy;
        currentStrategy.ApplyCursor();
    }

    public void SetDefault()
    {
        currentStrategy = defaultStrategy;
        currentStrategy.ApplyCursor();
    }
}
