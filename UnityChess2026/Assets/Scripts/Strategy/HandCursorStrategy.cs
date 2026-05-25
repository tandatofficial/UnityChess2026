using UnityEngine;

public class HandCursorStrategy : ICursorStrategy
{
    private Texture2D handTexture;
    private Vector2 hotspot;

    public HandCursorStrategy(Texture2D texture, Vector2 hotspot)
    {
        handTexture = texture;
        this.hotspot = hotspot;
    }

    public void ApplyCursor()
    {
        Cursor.SetCursor(handTexture, hotspot, CursorMode.Auto);
    }

    public void ResetCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
