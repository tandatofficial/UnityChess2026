
using UnityEngine;

public class DefaultCursorStrategy : ICursorStrategy
{
    public void ApplyCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    public void ResetCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
