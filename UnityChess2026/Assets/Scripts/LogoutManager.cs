using UnityEngine;

public class LogoutManager : MonoBehaviour
{
    public void Logout()
    {
        if (GameMenuUiSafetyRunner.TryLogoutFromGameMenu()) return;

        Debug.LogWarning("GameMenu not found; LogoutManager could not forward logout.");
    }
}

internal static class GameMenuUiSafetyRunner
{
    public static bool TryLogoutFromGameMenu()
    {
        GameMenu gameMenu = Object.FindObjectOfType<GameMenu>();
        if (gameMenu == null) return false;

        gameMenu.Logout();
        return true;
    }
}