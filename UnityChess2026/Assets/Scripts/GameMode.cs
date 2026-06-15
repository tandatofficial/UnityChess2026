/// <summary>
/// Defines the game mode for the current session.
/// </summary>
public enum GameMode
{
    /// <summary>Local PvP on the same device.</summary>
    Local,
    /// <summary>Player vs Stockfish AI.</summary>
    AI,
    /// <summary>Online PvP via Socket.IO server.</summary>
    Online
}
