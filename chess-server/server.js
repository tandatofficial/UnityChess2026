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

// Socket.IO Events (from original server.js)
let whitePlayer = null;
let blackPlayer = null;
let playerNames = {};

io.use((socket, next) => {
  if (socket.handshake.query.token === "UNITY") {
    next();
  } else {
    next(new Error("Authentication failed"));
  }
});

io.on("connection", (socket) => {
  const playerName = socket.handshake.query.name || "Unknown";
  playerNames[socket.id] = playerName;

  console.log(`🔌 New client connected: ${socket.id} (${playerName})`);

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

  socket.on("move", (data) => {
    const name = playerNames[socket.id] || "Unknown";
    console.log(`♟️ ${name} played move: ${data}`);
    socket.broadcast.emit("move", data);
  });

  socket.on("disconnect", () => {
    console.log(`❌ Disconnected: ${socket.id} (${playerNames[socket.id]})`);
    if (socket.id === whitePlayer) whitePlayer = null;
    if (socket.id === blackPlayer) blackPlayer = null;
    delete playerNames[socket.id];
  });
});

server.listen(PORT, () => {
  console.log(`🟢 Chess server running on port ${PORT}`);
  console.log(`📄 Swagger docs available at http://localhost:${PORT}/api-docs`);
});
