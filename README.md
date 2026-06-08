🧩 Overview
This is a 2-player online chess game built in Unity, implementing all official rules of chess.

🎮 Features
✅ PvP online multiplayer via Socket.IO
✅ Full legal move logic (check, checkmate, stalemate, promotion, castling)
✅ Highlighting valid moves
✅ End game detection
✅ Restart game button
🎨 Simple and clean UI using Unity 2D
😅 (UI is a bit plain because I was lazy – didn’t polish it much)

🛠️ Technologies

Unity Engine (C#)

TextMeshPro for UI

Singleton pattern, ScriptableObjects, and Coroutines

Socket.IO + Node.js for real-time multiplayer

Design Patterns used for board management and move validation
<img width="2559" height="1439" alt="image" src="https://github.com/user-attachments/assets/5837f433-5e63-48e7-a2fd-a764f98172ac" />
image

🚀 Getting Started

Open this project in Unity 6+

Start the Node.js server (cd ServerNodeJS && node index.js)
<img width="680" height="296" alt="image" src="https://github.com/user-attachments/assets/8b506b77-323a-4efc-82f3-4494e5147ae1" />

Play the scene Main in Unity

Enjoy real-time online chess!

## ⏱️ Chess Clock System

The game includes a FIDE-style chess clock system to provide a more competitive and realistic chess experience.

### Supported Time Controls

The following official time formats are supported:

* 1+0 (Bullet)
* 1+1 (Bullet)
* 3+0 (Blitz)
* 3+2 (Blitz)
* 5+0 (Blitz)
* 10+0 (Rapid)
* 10+5 (Rapid)
* 15+10 (Rapid)
* 30+0 (Classical)
* 90+30 (Classical)

Format:

* First number = starting time (minutes)
* Second number = increment added after every legal move (seconds)

### How It Works

* Each player has an independent chess clock.
* Only the current player's clock is running.
* After a legal move:

  * The current player's clock stops.
  * Increment time is added (if applicable).
  * Turn switches to the opponent.
  * The opponent's clock starts.

### Timeout Rules

A player immediately loses the game when their remaining time reaches zero.

Examples:

* White timer reaches 00:00 → Black wins by timeout.
* Black timer reaches 00:00 → White wins by timeout.

### Online Synchronization

To prevent cheating, the server is responsible for managing the official game clock.

The server:

* Tracks both players' remaining time.
* Validates moves.
* Applies time increments.
* Detects timeout conditions.
* Synchronizes clock updates to all connected clients.

The Unity client is responsible only for:

* Displaying the remaining time.
* Sending move requests.
* Updating the UI based on server responses.

### Clock Behavior

The clock:

* Starts only after both players have joined and the match begins.
* Stops immediately when the game ends by:

  * Checkmate
  * Stalemate
  * Draw
  * Timeout
  * Player disconnection

### UI Features

* White player timer display
* Black player timer display
* Current game result display
* Time control selection before starting a match

This system follows international chess timing standards and provides a fair competitive multiplayer experience.

