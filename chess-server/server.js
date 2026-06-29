const path = require('path');
const dotenv = require('dotenv');
const result = dotenv.config({ path: path.join(__dirname, '.env'), override: true });
console.log("Dotenv result:", result);
console.log("Loaded MAILTRAP_USER:", process.env.MAILTRAP_USER);
console.log("Loaded MAILTRAP_PASS:", process.env.MAILTRAP_PASS ? "EXISTS" : "MISSING");
const express = require('express');
const cors = require('cors');
const http = require('http');
const crypto = require('crypto');
const fs = require('fs');
const nodemailer = require('nodemailer');
const swaggerJsdoc = require('swagger-jsdoc');
const swaggerUi = require('swagger-ui-express');
const { Server } = require("socket.io");
const { createClient } = require('@supabase/supabase-js');

const PORT = process.env.PORT || 11100;

const app = express();
app.use(cors());
app.use(express.json());

// Initialize Supabase Client
const supabaseUrl = process.env.SUPABASE_URL;
const supabaseKey = process.env.SUPABASE_KEY;
if (!supabaseUrl || !supabaseKey) {
  console.error("❌ ERROR: SUPABASE_URL and SUPABASE_KEY must be configured in .env!");
}
const supabase = createClient(supabaseUrl, supabaseKey);

// Crypto Helpers
function hashPassword(password, salt) {
  return crypto.pbkdf2Sync(password, Buffer.from(salt, 'hex'), 10000, 32, 'sha256').toString('hex');
}

function generateSalt() {
  return crypto.randomBytes(16).toString('hex');
}

// Email setup
let transporter;
const isGmailConfigured = (process.env.EMAIL_USER && process.env.EMAIL_PASS && !process.env.EMAIL_USER.includes("your_gmail")) || 
                          (process.env.MAILTRAP_USER && process.env.MAILTRAP_USER.includes("@gmail.com"));

const emailUser = isGmailConfigured ? (process.env.EMAIL_USER || process.env.MAILTRAP_USER) : null;
const emailPass = isGmailConfigured ? (process.env.EMAIL_PASS || process.env.MAILTRAP_PASS) : null;

if (isGmailConfigured) {
  transporter = nodemailer.createTransport({
    service: 'gmail',
    auth: {
      user: emailUser,
      pass: emailPass
    }
  });
} else {
  transporter = nodemailer.createTransport({
    host: "sandbox.smtp.mailtrap.io",
    port: 2525,
    auth: {
      user: process.env.MAILTRAP_USER,
      pass: process.env.MAILTRAP_PASS
    }
  });
}

async function sendVerificationEmail(email, code) {
  console.log(`✉️ Verification code for [${email}] is: ${code}`);
  
  const isMailtrapConfigured = process.env.MAILTRAP_USER && 
                               process.env.MAILTRAP_PASS && 
                               !process.env.MAILTRAP_USER.includes("your_mailtrap") && 
                               !process.env.MAILTRAP_PASS.includes("your_mailtrap") &&
                               !process.env.MAILTRAP_USER.includes("@gmail.com");

  if (!isGmailConfigured && !isMailtrapConfigured) {
    console.log("⚠️ No SMTP credentials configured. Verification email was not sent, but you can use the code printed above.");
    return;
  }

  const mailOptions = {
    from: isGmailConfigured ? `"${emailUser}" <${emailUser}>` : '"Chess Game" <no-reply@chessgame.com>',
    to: email,
    subject: "Chess Game - Verify your registration",
    text: `Your verification code is: ${code}. It will expire in 10 minutes.`,
    html: `<p>Your verification code is: <b>${code}</b>. It will expire in 10 minutes.</p>`
  };

  try {
    await transporter.sendMail(mailOptions);
    console.log(`📬 Verification email sent successfully to ${email}`);
  } catch (error) {
    console.error("❌ Error sending email:", error);
  }
}

// Swagger configuration
const swaggerOptions = {
  definition: {
    openapi: '3.0.0',
    info: {
      title: 'Chess Game API',
      version: '1.0.0',
      description: 'API for Chess Game registration, login, and email verification. Test verification using Swagger.',
    },
    servers: [
      {
        url: `http://localhost:${PORT}`,
      },
    ],
  },
  apis: [__filename], // current file
};

const swaggerDocs = swaggerJsdoc(swaggerOptions);
app.use('/api-docs', swaggerUi.serve, swaggerUi.setup(swaggerDocs));

// ----------------------------------------------------
// HTTP API Endpoints
// ----------------------------------------------------

/**
 * @swagger
 * /api/auth/register:
 *   post:
 *     summary: Register a new user
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - username
 *               - email
 *               - password
 *             properties:
 *               username:
 *                 type: string
 *               email:
 *                 type: string
 *               password:
 *                 type: string
 *     responses:
 *       200:
 *         description: Registration successful, verification code sent to email
 *       400:
 *         description: Bad request (missing fields, email/username already taken, etc.)
 */
app.post('/api/auth/register', async (req, res) => {
  const { username, email, password } = req.body;
  if (!username || !email || !password) {
    return res.status(400).json({ error: "Missing required fields (username, email, password)." });
  }

  try {
    // Check duplicate email
    const { data: existingEmail, error: emailErr } = await supabase
      .from('game_users')
      .select('email')
      .ilike('email', email)
      .maybeSingle();

    if (emailErr) {
      console.error("❌ Supabase email check error:", emailErr);
      return res.status(500).json({ error: "Database query failed." });
    }
    if (existingEmail) {
      return res.status(400).json({ error: "Email is already registered." });
    }

    // Check duplicate username
    const { data: existingUser, error: userErr } = await supabase
      .from('game_users')
      .select('username')
      .ilike('username', username)
      .maybeSingle();

    if (userErr) {
      console.error("❌ Supabase username check error:", userErr);
      return res.status(500).json({ error: "Database query failed." });
    }
    if (existingUser) {
      return res.status(400).json({ error: "Username is already taken." });
    }

    // Create User
    const salt = generateSalt();
    const passwordHash = hashPassword(password, salt);
    const code = Math.floor(100000 + Math.random() * 900000).toString(); // 6-digit code
    const expiry = Date.now() + 10 * 60 * 1000; // 10 minutes from now

    const { error: insertErr } = await supabase
      .from('game_users')
      .insert([{
        username,
        email,
        password_hash: passwordHash,
        salt,
        is_verified: false,
        verification_code: code,
        verification_code_expiry: expiry
      }]);

    if (insertErr) {
      console.error("❌ Supabase insert error:", insertErr);
      return res.status(500).json({ error: "Failed to create user in database." });
    }

    await sendVerificationEmail(email, code);

    const isMailtrapConfigured = process.env.MAILTRAP_USER && 
                                 process.env.MAILTRAP_PASS && 
                                 !process.env.MAILTRAP_USER.includes("your_mailtrap") && 
                                 !process.env.MAILTRAP_PASS.includes("your_mailtrap");

    if (isGmailConfigured || isMailtrapConfigured) {
      res.json({ message: "Registration successful. Please check your email for the verification code." });
    } else {
      res.json({ message: `Registration successful. [TEST MODE] Code is: ${code}` });
    }
  } catch (err) {
    console.error("❌ Register error:", err);
    res.status(500).json({ error: "Internal server error." });
  }
});

/**
 * @swagger
 * /api/auth/verify:
 *   post:
 *     summary: Verify email verification code
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - email
 *               - code
 *             properties:
 *               email:
 *                 type: string
 *               code:
 *                 type: string
 *     responses:
 *       200:
 *         description: Verification successful, user is now active
 *       400:
 *         description: Invalid or expired verification code
 */
app.post('/api/auth/verify', async (req, res) => {
  const { email, code } = req.body;
  if (!email || !code) {
    return res.status(400).json({ error: "Missing required fields (email, code)." });
  }

  try {
    const { data: user, error: queryErr } = await supabase
      .from('game_users')
      .select('*')
      .ilike('email', email)
      .maybeSingle();

    if (queryErr || !user) {
      console.error("❌ Supabase query error or not found:", queryErr);
      return res.status(400).json({ error: "Account not found." });
    }

    if (user.is_verified) {
      return res.status(400).json({ error: "Account is already verified." });
    }

    if (user.verification_code !== code) {
      return res.status(400).json({ error: "Invalid verification code." });
    }

    if (Date.now() > Number(user.verification_code_expiry)) {
      return res.status(400).json({ error: "Verification code has expired." });
    }

    // Update user verification status
    const { error: updateErr } = await supabase
      .from('game_users')
      .update({
        is_verified: true,
        verification_code: null,
        verification_code_expiry: null
      })
      .eq('id', user.id);

    if (updateErr) {
      console.error("❌ Supabase update error:", updateErr);
      return res.status(500).json({ error: "Failed to update verification status." });
    }

    res.json({ message: "Email verification successful. You can now log in." });
  } catch (err) {
    console.error("❌ Verify error:", err);
    res.status(500).json({ error: "Internal server error." });
  }
});

/**
 * @swagger
 * /api/auth/login:
 *   post:
 *     summary: Log in a user
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             type: object
 *             required:
 *               - usernameOrEmail
 *               - password
 *             properties:
 *               usernameOrEmail:
 *                 type: string
 *               password:
 *                 type: string
 *     responses:
 *       200:
 *         description: Login successful
 *       400:
 *         description: Invalid credentials or unverified email
 */
app.post('/api/auth/login', async (req, res) => {
  const { usernameOrEmail, password } = req.body;
  if (!usernameOrEmail || !password) {
    return res.status(400).json({ error: "Missing usernameOrEmail or password." });
  }

  try {
    const { data: user, error: queryErr } = await supabase
      .from('game_users')
      .select('*')
      .or(`email.ilike.${usernameOrEmail},username.ilike.${usernameOrEmail}`)
      .maybeSingle();

    if (queryErr || !user) {
      console.error("❌ Supabase query error or not found:", queryErr);
      return res.status(400).json({ error: "Account not found." });
    }

    const computedHash = hashPassword(password, user.salt);
    if (computedHash !== user.password_hash) {
      return res.status(400).json({ error: "Invalid password." });
    }

    if (!user.is_verified) {
      return res.status(400).json({ error: "Please verify your email before logging in." });
    }

    res.json({
      message: "Login successful.",
      user: {
        username: user.username,
        email: user.email,
        passwordHash: user.password_hash,
        salt: user.salt
      }
    });
  } catch (err) {
    console.error("❌ Login error:", err);
    res.status(500).json({ error: "Internal server error." });
  }
});

// Create Server
const server = http.createServer(app);
const io = new Server(server, {
  cors: {
    origin: "*"
  }
});

// ----------------------------------------------------
// Room-Based Multiplayer System
// ----------------------------------------------------

const rooms = new Map(); // roomId -> RoomState
const playerRoomMap = new Map(); // playerId -> roomId

function generateRoomId() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  let id = '';
  for (let i = 0; i < 6; i++) {
    id += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return id;
}

function createRoomState(roomId) {
  return {
    roomId,
    white: null,         // socket.id
    black: null,         // socket.id
    whitePlayerId: null, // UUID
    blackPlayerId: null, // UUID
    whiteName: null,
    blackName: null,
    status: 'waiting',   // waiting | playing | reconnecting | finished
    moves: [],
    currentTurn: 'white',
    boardState: null,
    reconnectTimer: null,
    rematchRequests: new Set(),
    rematchReadyCount: 0,
    cleanupTimer: null
  };
}

function getPlayerRoom(playerId) {
  const roomId = playerRoomMap.get(playerId);
  if (roomId) return rooms.get(roomId);
  return null;
}

function getOpponentId(room, playerId) {
  if (room.whitePlayerId === playerId) return room.black;
  if (room.blackPlayerId === playerId) return room.white;
  return null;
}

function broadcastAvailableRooms() {
  const availableRooms = [];
  for (const [roomId, room] of rooms.entries()) {
    if (room.status === 'waiting' && !room.isPrivate) {
      availableRooms.push({
        roomId: room.roomId,
        creatorName: room.whiteName
      });
    }
  }
  io.emit("availableRoomsList", JSON.stringify({ rooms: availableRooms }));
}

function cleanupRoom(roomId) {
  const room = rooms.get(roomId);
  if (room) {
    if (room.whitePlayerId) playerRoomMap.delete(room.whitePlayerId);
    if (room.blackPlayerId) playerRoomMap.delete(room.blackPlayerId);
    if (room.reconnectTimer) clearTimeout(room.reconnectTimer);
    rooms.delete(roomId);
    console.log(`🗑️ Room ${roomId} cleaned up. Active rooms: ${rooms.size}`);
    broadcastAvailableRooms();
  }
}

// Socket.IO Authentication
io.use((socket, next) => {
  if (socket.handshake.query.token === "UNITY") {
    next();
  } else {
    next(new Error("Authentication failed"));
  }
});

// Socket.IO Connection Handler
io.on("connection", (socket) => {
  const playerName = socket.handshake.query.name || "Unknown";
  const playerId = socket.handshake.query.playerId;
  console.log(`🔌 Connected: ${socket.id} (Player: ${playerId}, Name: ${playerName})`);

  if (!playerId) {
    socket.disconnect();
    return;
  }

  // --- RECONNECTION LOGIC ---
  const existingRoom = getPlayerRoom(playerId);
  if (existingRoom && existingRoom.status !== 'finished') {
    const isWhite = existingRoom.whitePlayerId === playerId;
    if (isWhite) existingRoom.white = socket.id;
    else existingRoom.black = socket.id;

    if (existingRoom.reconnectTimer) {
      clearTimeout(existingRoom.reconnectTimer);
      existingRoom.reconnectTimer = null;
    }

    if (existingRoom.status === 'reconnecting') {
      existingRoom.status = 'playing';
    }

    socket.join(existingRoom.roomId);

    const opponentSocketId = isWhite ? existingRoom.black : existingRoom.white;
    if (opponentSocketId) {
      io.to(opponentSocketId).emit("opponentReconnected", JSON.stringify({ message: "Opponent returned" }));
    }

    socket.emit("reconnectSuccess", JSON.stringify({
      roomId: existingRoom.roomId,
      color: isWhite ? "white" : "black",
      boardState: existingRoom.boardState,
      currentTurn: existingRoom.currentTurn
    }));

    console.log(`🔄 ${playerName} reconnected to room ${existingRoom.roomId}`);
  }

  // --- GET AVAILABLE ROOMS ---
  socket.on("getAvailableRooms", () => {
    const availableRooms = [];
    for (const [rId, rm] of rooms.entries()) {
      if (rm.status === 'waiting' && !rm.isPrivate) {
        availableRooms.push({
          roomId: rm.roomId,
          creatorName: rm.whiteName
        });
      }
    }
    socket.emit("availableRoomsList", JSON.stringify({ rooms: availableRooms }));
  });

  // Send initial room list on connection
  {
    const availableRooms = [];
    for (const [rId, rm] of rooms.entries()) {
      if (rm.status === 'waiting' && !rm.isPrivate) {
        availableRooms.push({
          roomId: rm.roomId,
          creatorName: rm.whiteName
        });
      }
    }
    socket.emit("availableRoomsList", JSON.stringify({ rooms: availableRooms }));
  }

  // --- CREATE ROOM ---
  socket.on("createRoom", (data) => {
    // Ensure cleanup of any old room this player was in
    const oldRoom = getPlayerRoom(playerId);
    if (oldRoom) {
      cleanupRoom(oldRoom.roomId);
    }

    let roomId = generateRoomId();
    while (rooms.has(roomId)) {
      roomId = generateRoomId();
    }

    let isPrivate = false;
    if (data) {
      try {
        const parsed = typeof data === 'string' ? JSON.parse(data) : data;
        isPrivate = !!parsed.isPrivate;
      } catch (e) {
        console.error("Error parsing createRoom data:", e);
      }
    }

    const room = createRoomState(roomId);
    room.white = socket.id;
    room.whitePlayerId = playerId;
    room.whiteName = playerName;
    room.isPrivate = isPrivate;
    rooms.set(roomId, room);
    playerRoomMap.set(playerId, roomId);

    socket.join(roomId);
    socket.emit("roomCreated", JSON.stringify({ roomId }));
    console.log(`🏠 Room ${roomId} (private: ${isPrivate}) created by ${playerName} (white). Active rooms: ${rooms.size}`);
    broadcastAvailableRooms();
  });

  // --- LEAVE ROOM ---
  socket.on("leaveRoom", () => {
    const room = getPlayerRoom(playerId);
    if (!room) return;

    if (room.status === 'playing') {
      const isWhite = room.whitePlayerId === playerId;
      const opponentSocketId = isWhite ? room.black : room.white;
      
      if (opponentSocketId) {
        io.to(opponentSocketId).emit("gameOver", JSON.stringify({
          winner: isWhite ? "black" : "white",
          reason: `${playerName} left the room`,
        }));
      }
    }
    
    cleanupRoom(room.roomId);
  });

  // --- JOIN ROOM ---
  socket.on("joinRoom", (data) => {
    let parsed;
    try {
      parsed = typeof data === 'string' ? JSON.parse(data) : data;
    } catch (e) {
      parsed = { roomId: data };
    }

    const roomId = parsed.roomId ? parsed.roomId.toUpperCase() : null;
    const room = rooms.get(roomId);

    if (!room) {
      socket.emit("joinError", JSON.stringify({ error: "Room not found." }));
      return;
    }

    if (room.status !== 'waiting') {
      socket.emit("joinError", JSON.stringify({ error: "Room is already full or playing." }));
      return;
    }

    if (room.whitePlayerId === playerId) {
      socket.emit("joinError", JSON.stringify({ error: "You cannot join your own room." }));
      return;
    }

    const oldRoom = getPlayerRoom(playerId);
    if (oldRoom && oldRoom.roomId !== roomId) {
       cleanupRoom(oldRoom.roomId);
    }

    room.black = socket.id;
    room.blackPlayerId = playerId;
    room.blackName = playerName;
    room.status = 'playing';
    playerRoomMap.set(playerId, roomId);

    socket.join(roomId);

    io.to(room.white).emit("gameStart", JSON.stringify({
      roomId: room.roomId,
      color: "white",
      opponentName: room.blackName,
    }));

    io.to(room.black).emit("gameStart", JSON.stringify({
      roomId: room.roomId,
      color: "black",
      opponentName: room.whiteName,
    }));

    console.log(`🎮 Game started in room ${roomId}: ${room.whiteName} vs ${room.blackName}`);
    broadcastAvailableRooms();
  });

  // --- MOVE ---
  socket.on("move", (data) => {
    const room = getPlayerRoom(playerId);
    if (!room || room.status !== 'playing') {
      return;
    }

    const isWhite = room.whitePlayerId === playerId;
    const expectedTurn = isWhite ? 'white' : 'black';

    if (room.currentTurn !== expectedTurn) return;

    let parsed;
    try {
      parsed = typeof data === 'string' ? JSON.parse(data) : data;
    } catch (e) {
      parsed = { move: data };
    }

    const moveStr = parsed.move || data;
    room.boardState = parsed.boardState || room.boardState;

    room.moves.push({
      player: expectedTurn,
      move: moveStr,
      timestamp: Date.now(),
    });

    room.currentTurn = room.currentTurn === 'white' ? 'black' : 'white';

    const opponentSocketId = isWhite ? room.black : room.white;
    io.to(opponentSocketId).emit("move", JSON.stringify({ move: moveStr }));
    console.log(`♟️ [${room.roomId}] ${playerName} (${expectedTurn}): ${moveStr} | Turn -> ${room.currentTurn}`);
  });

  // --- GAME OVER ---
  socket.on("gameOver", (data) => {
    const room = getPlayerRoom(playerId);
    if (!room || room.status !== 'playing') return;

    let parsed;
    try {
      parsed = typeof data === 'string' ? JSON.parse(data) : data;
    } catch (e) {
      parsed = { winner: "unknown", reason: "unknown" };
    }

    room.status = 'finished';

    io.to(room.roomId).emit("gameOver", JSON.stringify({
      winner: parsed.winner || "unknown",
      reason: parsed.reason || "unknown",
    }));

    console.log(`🏁 [${room.roomId}] Game over: ${parsed.reason} | Winner: ${parsed.winner}`);

    room.cleanupTimer = setTimeout(() => cleanupRoom(room.roomId), 30000);
  });

  // --- RESIGN ---
  socket.on("resign", () => {
    const room = getPlayerRoom(playerId);
    if (!room || room.status !== 'playing') return;

    const isWhite = room.whitePlayerId === playerId;
    const winner = isWhite ? "black" : "white";
    const loserName = isWhite ? room.whiteName : room.blackName;

    room.status = 'finished';

    io.to(room.roomId).emit("gameOver", JSON.stringify({
      winner,
      reason: `${loserName} resigned`,
    }));

    console.log(`🏳️ [${room.roomId}] ${loserName} resigned. ${winner} wins!`);
    room.cleanupTimer = setTimeout(() => cleanupRoom(room.roomId), 30000);
  });

  // --- REQUEST REMATCH ---
  socket.on("requestRematch", () => {
    const room = getPlayerRoom(playerId);
    if (!room || room.status !== 'finished') return;

    room.rematchRequests.add(playerId);
    console.log(`🔄 [${room.roomId}] Player ${playerName} (${playerId}) requested a rematch. Count: ${room.rematchRequests.size}`);

    if (room.rematchRequests.size === 1) {
      // Notify the other player
      const isWhite = room.whitePlayerId === playerId;
      const opponentSocketId = isWhite ? room.black : room.white;
      if (opponentSocketId) {
        io.to(opponentSocketId).emit("opponentRequestedRematch", JSON.stringify({ message: "Opponent wants a rematch" }));
      }
    } else if (room.rematchRequests.size === 2) {
      // Cancel room cleanup timer
      if (room.cleanupTimer) {
        clearTimeout(room.cleanupTimer);
        room.cleanupTimer = null;
      }
      // Notify both players that rematch is agreed
      io.to(room.roomId).emit("rematchAgreed", JSON.stringify({ message: "Rematch agreed" }));
      room.rematchReadyCount = 0;
      console.log(`🤝 [${room.roomId}] Rematch agreed by both players!`);
    }
  });

  // --- PLAYER READY FOR REMATCH ---
  socket.on("playerReadyForRematch", () => {
    const room = getPlayerRoom(playerId);
    if (!room) return;

    room.rematchReadyCount++;
    console.log(`🆕 [${room.roomId}] Player ${playerName} is ready for rematch. Ready count: ${room.rematchReadyCount}`);

    if (room.rematchReadyCount === 2) {
      // Swap colors
      const oldWhite = room.white;
      const oldWhitePlayerId = room.whitePlayerId;
      const oldWhiteName = room.whiteName;

      room.white = room.black;
      room.whitePlayerId = room.blackPlayerId;
      room.whiteName = room.blackName;

      room.black = oldWhite;
      room.blackPlayerId = oldWhitePlayerId;
      room.blackName = oldWhiteName;

      // Reset room state
      room.status = 'playing';
      room.moves = [];
      room.currentTurn = 'white';
      room.boardState = null;
      room.rematchRequests.clear();
      room.rematchReadyCount = 0;

      // Start the game with swapped colors
      io.to(room.white).emit("gameStart", JSON.stringify({
        roomId: room.roomId,
        color: "white",
        opponentName: room.blackName,
      }));

      io.to(room.black).emit("gameStart", JSON.stringify({
        roomId: room.roomId,
        color: "black",
        opponentName: room.whiteName,
      }));

      console.log(`🎮 Rematch game started in room ${room.roomId}: ${room.whiteName} (white) vs ${room.blackName} (black)`);
    }
  });

  // --- DISCONNECT ---
  socket.on("disconnect", () => {
    console.log(`❌ Disconnected: ${socket.id} (Player: ${playerId}, Name: ${playerName})`);

    const room = getPlayerRoom(playerId);
    if (!room) return;

    if (room.status === 'waiting') {
      cleanupRoom(room.roomId);
      return;
    }

    if (room.status === 'playing') {
      room.status = 'reconnecting';
      const isWhite = room.whitePlayerId === playerId;
      const opponentSocketId = isWhite ? room.black : room.white;

      if (opponentSocketId) {
        io.to(opponentSocketId).emit("opponentDisconnected", JSON.stringify({
          message: `${playerName} disconnected. Waiting 30s...`,
        }));
      }

      room.reconnectTimer = setTimeout(() => {
        const currentRoom = rooms.get(room.roomId);
        if (!currentRoom) return;

        if (currentRoom.status === 'reconnecting') {
           currentRoom.status = 'finished';
           const winner = isWhite ? "black" : "white";
           
           if (opponentSocketId) {
             io.to(opponentSocketId).emit("gameOver", JSON.stringify({
               winner,
               reason: `${playerName} abandoned the match`,
             }));
           }
           setTimeout(() => cleanupRoom(room.roomId), 10000);
        }
      }, 30000);
    }
  });
});

server.listen(PORT, () => {
  console.log(`🟢 Chess server running on port ${PORT}`);
  console.log(`📄 Swagger docs available at http://localhost:${PORT}/api-docs`);
  console.log(`🎮 Room-based multiplayer system active.`);
});
