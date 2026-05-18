const { Server } = require("socket.io");
const io = new Server({
  cors: {
    origin: "*"
  }
});

let whitePlayer = null;
let blackPlayer = null;
let playerNames = {}; // 👈 Dùng để lưu tên theo socket.id

io.use((socket, next) => {
  if (socket.handshake.query.token === "UNITY") {
    next();
  } else {
    next(new Error("Authentication failed"));
  }
});

io.on("connection", (socket) => {
  const playerName = socket.handshake.query.name || "Unknown";
  playerNames[socket.id] = playerName; // 👈 Lưu tên

  console.log(`🔌 New client: ${socket.id} (${playerName})`);

  let assignedColor = null;
  if (!whitePlayer) {
    whitePlayer = socket.id;
    assignedColor = "white";
  } else if (!blackPlayer) {
    blackPlayer = socket.id;
    assignedColor = "black";
  } else {
    socket.emit("assignColor", "spectator");
    console.log(`👀 Spectator connected: ${socket.id} (${playerName})`);
    return;
  }

  socket.emit("assignColor", assignedColor);
  console.log(`🎨 Assigned ${assignedColor} to ${socket.id} (${playerName})`);
  console.log(`✅ Client connected and assigned color: ${assignedColor}`);

  // Ghi log tên người chơi và nước đi
  socket.on("move", (data) => {
    const name = playerNames[socket.id] || "Unknown";
    console.log(`♟️ ${name} played move: ${data}`);
    socket.broadcast.emit("move", data);
  });

  socket.on("disconnect", () => {
    console.log(`❌ Disconnected: ${socket.id} (${playerNames[socket.id]})`);
    if (socket.id === whitePlayer) whitePlayer = null;
    if (socket.id === blackPlayer) blackPlayer = null;

    delete playerNames[socket.id]; // Xoá tên khi disconnect
  });
});

io.listen(11100);
console.log("🟢 Server đang chạy ở cổng 11100");
