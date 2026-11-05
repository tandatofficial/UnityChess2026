const path = require('path');
const dotenv = require('dotenv');
dotenv.config({ path: path.join(__dirname, '.env') });
const { issueSession } = require('./session');
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
app.use(express.json({ limit: '16kb' }));
const attempts = new Map();
app.use('/api/auth', (req, res, next) => {
  const now = Date.now();
  for (const [key, value] of attempts) if (value.until < now) attempts.delete(key);
  const entry = attempts.get(req.ip) || { count: 0, until: now + 60000 };
  attempts.set(req.ip, entry);
  if (++entry.count > 20) return res.status(429).json({ error: 'Too many requests. Try again in a minute.' });
  if (!req.body || typeof req.body !== 'object' || Object.values(req.body).some(v => typeof v !== 'string' || v.length > 256))
    return res.status(400).json({ error: 'Invalid request fields.' });
  next();
});

// Initialize Supabase Client safely
const supabaseUrl = process.env.SUPABASE_URL;
const supabaseKey = process.env.SUPABASE_KEY;
let supabase = null;
if (!supabaseUrl || !supabaseKey) {
  console.warn("⚠️ WARNING: SUPABASE_URL and SUPABASE_KEY are not configured. Auth endpoints will return 503 until configured.");
} else {
  supabase = createClient(supabaseUrl, supabaseKey);
}

// Crypto Helpers
async function hashPassword(password, salt, legacy = false) {
  const iterations = legacy ? 10000 : 600000;
  const hash = await new Promise((resolve, reject) => crypto.pbkdf2(password, Buffer.from(salt, 'hex'), iterations, 32, 'sha256', (error, value) => error ? reject(error) : resolve(value.toString('hex'))));
  return legacy ? hash : 'pbkdf2-sha256:600000:' + hash;
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
  
  const isMailtrapConfigured = process.env.MAILTRAP_USER && 
                               process.env.MAILTRAP_PASS && 
                               !process.env.MAILTRAP_USER.includes("your_mailtrap") && 
                               !process.env.MAILTRAP_PASS.includes("your_mailtrap") &&
                               !process.env.MAILTRAP_USER.includes("@gmail.com");

  if (!isGmailConfigured && !isMailtrapConfigured) {
    throw new Error('Email delivery is not configured');
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
    console.error("Email delivery failed");
    throw error;
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
        url: process.env.RENDER_EXTERNAL_URL || `http://localhost:${PORT}`,
        description: process.env.RENDER_EXTERNAL_URL ? 'Render Production Server' : 'Local Server',
      },
      {
        url: '/',
        description: 'Current Host (Relative)',
      },
    ],
  },
  apis: [__filename], // current file
};

const swaggerDocs = swaggerJsdoc(swaggerOptions);
app.use('/api-docs', swaggerUi.serve, swaggerUi.setup(swaggerDocs));

// ----------------------------------------------------
// Health Check & Status Endpoints (Required for Render)
// ----------------------------------------------------
app.get('/health', (req, res) => {
  res.status(200).json({
    status: 'healthy',
    uptime: Math.floor(process.uptime()),
    timestamp: new Date().toISOString(),
  });
});

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
  if (!supabase) {
    return res.status(503).json({ error: "Database service is not configured." });
  }
  const { username, email, password } = req.body;
  if (!username || !email || !password) {
    return res.status(400).json({ error: "Missing required fields (username, email, password)." });
  }

  try {
    if (!/^[a-zA-Z0-9_]{3,32}$/.test(username) || !/^[^\s@,%()]+@[^\s@,%()]+\.[^\s@,%()]+$/.test(email) || password.length < 8)
      return res.status(400).json({ error: 'Use a 3-32 character username (letters, digits, underscore), valid email and password of at least 8 characters.' });
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
    const passwordHash = await hashPassword(password, salt);
    const code = crypto.randomInt(100000, 1000000).toString(); // 6-digit code
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

    try { await sendVerificationEmail(email, code); }
    catch (error) {
      await supabase.from('game_users').delete().eq('email', email).eq('is_verified', false);
      return res.status(503).json({ error: 'Verification email could not be sent. Please try again later.' });
    }

    res.json({ message: "Registration successful. Please check your email for the verification code." });
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
  if (!supabase) {
    return res.status(503).json({ error: "Database service is not configured." });
  }
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
  if (!supabase) {
    return res.status(503).json({ error: "Database service is not configured." });
  }
  const { usernameOrEmail, password } = req.body;
  if (!usernameOrEmail || !password) {
    return res.status(400).json({ error: "Missing usernameOrEmail or password." });
  }

  try {
    const { data: user, error: queryErr } = await supabase
      .from('game_users')
      .select('*')
      .ilike(usernameOrEmail.includes('@') ? 'email' : 'username', usernameOrEmail.replace(/[%_\\]/g, c => String.fromCharCode(92) + c))
      .maybeSingle();

    if (queryErr || !user) {
      console.error("❌ Supabase query error or not found:", queryErr);
      return res.status(400).json({ error: "Account not found." });
    }

    const computedHash = await hashPassword(password, user.salt, !user.password_hash.startsWith('pbkdf2-sha256:600000:'));
    if (computedHash.length !== user.password_hash.length || !crypto.timingSafeEqual(Buffer.from(computedHash), Buffer.from(user.password_hash))) {
      return res.status(400).json({ error: "Invalid password." });
    }

    if (!user.is_verified) {
      return res.status(400).json({ error: "Please verify your email before logging in." });
    }

    res.json({
      message: "Login successful.",
      token: issueSession(user),
      user: {
        username: user.username,
        email: user.email
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
  },
  maxHttpBufferSize: 16384,
  pingTimeout: 10000,
  pingInterval: 5000
});

// ----------------------------------------------------
// Room-Based Multiplayer System
// ----------------------------------------------------

const multiplayer = require('./multiplayer').attachMultiplayer(io);
app.get('/', (req, res) => res.json({ status: 'online', activeRooms: multiplayer.rooms.size }));
function shutdown() { multiplayer.close(); io.close(); server.close(() => process.exit(0)); }
process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);

server.listen(PORT, '0.0.0.0', () => {
  const baseUrl = process.env.RENDER_EXTERNAL_URL || `http://localhost:${PORT}`;
  console.log(`🟢 Chess server running on port ${server.address().port}`);
  console.log(`📄 Swagger docs available at ${baseUrl}/api-docs`);
  console.log(`🩺 Health check available at ${baseUrl}/health`);
  console.log(`🎮 Room-based multiplayer system active.`);
});
