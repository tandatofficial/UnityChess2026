using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Serializable]
public class Account
{
    public string Username;
    public string Email;
    public string PasswordHash;
    public string Salt;
}

[Serializable]
public class AccountList
{
    public List<Account> Accounts = new List<Account>();
}

public class AccountManager
{
    private static AccountManager _instance;
    public static AccountManager Instance => _instance ?? (_instance = new AccountManager());

    private readonly string _filePath;
    private AccountList _accounts;

    public Account CurrentAccount { get; private set; }
    public bool IsLoggedIn => CurrentAccount != null;

    private AccountManager()
    {
        _filePath = Path.Combine(Application.persistentDataPath, "accounts.json");
        Load();
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            string json = File.ReadAllText(_filePath);
            _accounts = JsonUtility.FromJson<AccountList>(json) ?? new AccountList();
        }
        else
        {
            _accounts = new AccountList();
            Save();
        }
    }

    private void Save()
    {
        string json = JsonUtility.ToJson(_accounts, true);
        File.WriteAllText(_filePath, json);
    }

    public bool Register(string username, string email, string password, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(email))
        {
            error = "Provide a username or email.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(email) && !ValidateEmail(email))
        {
            error = "Invalid email format.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(username) && !ValidateUsername(username))
        {
            error = "Username must be 3-20 characters, letters, numbers, underscore or dash.";
            return false;
        }

        if (!ValidatePassword(password, out string pwError))
        {
            error = pwError;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(email) &&
            _accounts.Accounts.Exists(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Email is already registered.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(username) &&
            _accounts.Accounts.Exists(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Username is already taken.";
            return false;
        }

        byte[] salt = GenerateSalt(16);
        byte[] hash = HashPassword(password, salt);

        Account account = new Account
        {
            Username = username,
            Email = email,
            PasswordHash = ToHex(hash),
            Salt = ToHex(salt)
        };

        _accounts.Accounts.Add(account);
        Save();

        return true;
    }

    public bool Login(string usernameOrEmail, string password, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            error = "Missing credentials.";
            return false;
        }

        Account found = _accounts.Accounts.Find(a =>
            string.Equals(a.Email, usernameOrEmail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Username, usernameOrEmail, StringComparison.OrdinalIgnoreCase));

        if (found == null)
        {
            error = "Account not found.";
            return false;
        }

        byte[] salt = FromHex(found.Salt);
        byte[] hash = HashPassword(password, salt);

        if (!string.Equals(ToHex(hash), found.PasswordHash, StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid password.";
            return false;
        }

        CurrentAccount = found;
        return true;
    }

    public void Logout()
    {
        CurrentAccount = null;
    }

    private static bool ValidateEmail(string email)
    {
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    }

    private static bool ValidateUsername(string username)
    {
        return Regex.IsMatch(username, "^[a-zA-Z0-9_-]{3,20}$");
    }

    private static bool ValidatePassword(string password, out string error)
    {
        error = null;

        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            error = "Password must be at least 8 characters.";
            return false;
        }

        if (!Regex.IsMatch(password, "[0-9]"))
        {
            error = "Password must contain at least one digit.";
            return false;
        }

        if (!Regex.IsMatch(password, "[A-Za-z]"))
        {
            error = "Password must contain at least one letter.";
            return false;
        }

        return true;
    }

    private static byte[] GenerateSalt(int size)
    {
        byte[] salt = new byte[size];

        using (var rng = new RNGCryptoServiceProvider())
        {
            rng.GetBytes(salt);
        }

        return salt;
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
        {
            return deriveBytes.GetBytes(32);
        }
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder(bytes.Length * 2);

        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static byte[] FromHex(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}

public class Login : MonoBehaviour
{
    [Header("Scene Settings")]
    public string mainMenuSceneName = "PvPOff";
    public string loginSceneName = "HomeUI";

    [Header("Panels")]
    public GameObject startPanel;
    public GameObject loginPanel;
    public GameObject registerPanel;

    [Header("Login Fields")]
    public TMP_InputField loginUsernameOrEmail;
    public TMP_InputField loginPassword;

    [Header("Register Fields")]
    public TMP_InputField regUsername;
    public TMP_InputField regEmail;
    public TMP_InputField regPassword;
    public TMP_InputField regConfirmPassword;

    [Header("Text UI")]
    public TMP_Text feedbackText;
    public TMP_Text currentUserText;

    private void Awake()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.firstSelectedGameObject = null;
        }
    }

    private void Start()
    {
        ShowStartPanel();
        UpdateCurrentUserUI();
    }

    public void ShowStartPanel()
    {
        if (startPanel) startPanel.SetActive(true);
        if (loginPanel) loginPanel.SetActive(false);
        if (registerPanel) registerPanel.SetActive(false);
        SetFeedback("", true);
        RestoreFullScreenBlockers();
    }

    public void ShowLoginPanel()
    {
        if (startPanel) startPanel.SetActive(false);
        if (loginPanel) loginPanel.SetActive(true);
        if (registerPanel) registerPanel.SetActive(false);
        SetFeedback("", true);
        BringPanelToFront(loginPanel);
        DisableFullScreenBlockersExcept(loginPanel);
        // focus first input
        if (loginUsernameOrEmail != null)
        {
            EventSystem.current.SetSelectedGameObject(loginUsernameOrEmail.gameObject);
            loginUsernameOrEmail.ActivateInputField();
        }
    }

    public void ShowRegisterPanel()
    {
        if (startPanel) startPanel.SetActive(false);
        if (loginPanel) loginPanel.SetActive(false);
        if (registerPanel) registerPanel.SetActive(true);
        SetFeedback("", true);
        BringPanelToFront(registerPanel);
        DisableFullScreenBlockersExcept(registerPanel);
        if (regUsername != null)
        {
            EventSystem.current.SetSelectedGameObject(regUsername.gameObject);
            regUsername.ActivateInputField();
        }
    }

    public void OnLoginButton()
    {
        string id = loginUsernameOrEmail ? loginUsernameOrEmail.text.Trim() : "";
        string pw = loginPassword ? loginPassword.text : "";

        if (AccountManager.Instance.Login(id, pw, out string error))
        {
            PlayerPrefs.SetString("loggedInUser", id);
            PlayerPrefs.Save();

            SetFeedback("Login successful", true);
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            SetFeedback(error ?? "Login failed", false);
        }
    }

    public void OnRegisterButton()
    {
        string username = regUsername ? regUsername.text.Trim() : "";
        string email = regEmail ? regEmail.text.Trim() : "";
        string pw = regPassword ? regPassword.text : "";
        string confirm = regConfirmPassword ? regConfirmPassword.text : "";

        if (pw != confirm)
        {
            SetFeedback("Passwords do not match", false);
            return;
        }

        if (AccountManager.Instance.Register(username, email, pw, out string error))
        {
            SetFeedback("Registration successful. You can now log in.", true);

            if (regUsername) regUsername.text = "";
            if (regEmail) regEmail.text = "";
            if (regPassword) regPassword.text = "";
            if (regConfirmPassword) regConfirmPassword.text = "";

            ShowLoginPanel();
        }
        else
        {
            SetFeedback(error ?? "Registration failed", false);
        }
    }

    public void OnLogoutButton()
    {
        AccountManager.Instance.Logout();

        PlayerPrefs.DeleteKey("loggedInUser");
        PlayerPrefs.Save();

        SceneManager.LoadScene(loginSceneName);
    }

    private void UpdateCurrentUserUI()
    {
        if (currentUserText == null) return;

        if (AccountManager.Instance.IsLoggedIn && AccountManager.Instance.CurrentAccount != null)
        {
            Account acc = AccountManager.Instance.CurrentAccount;
            string displayName = !string.IsNullOrEmpty(acc.Username) ? acc.Username : acc.Email;
            currentUserText.text = "Logged in: " + displayName;
        }
        else
        {
            currentUserText.text = "Not logged in";
        }
    }

    private void SetFeedback(string message, bool success)
    {
        if (feedbackText == null) return;

        feedbackText.text = message;
        feedbackText.color = success ? Color.green : Color.red;
    }

    public void OnBackButton()
    {
        if (loginUsernameOrEmail) loginUsernameOrEmail.text = "";
        if (loginPassword) loginPassword.text = "";

        if (regUsername) regUsername.text = "";
        if (regEmail) regEmail.text = "";
        if (regPassword) regPassword.text = "";
        if (regConfirmPassword) regConfirmPassword.text = "";

        if (feedbackText) feedbackText.text = "";

        ShowStartPanel();
    }

    // Bring panel to top of canvas so it's rendered above other UI
    private void BringPanelToFront(GameObject panel)
    {
        if (panel == null) return;
        panel.transform.SetAsLastSibling();
    }

    // Disable full-screen Image raycastTarget for images not under the active panel
    private void DisableFullScreenBlockersExcept(GameObject activePanel)
    {
        var canvas = activePanel != null ? activePanel.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        var images = canvas.GetComponentsInChildren<Image>(true);
        foreach (var img in images)
        {
            var rt = img.rectTransform;
            if (rt == null) continue;
            bool fullScreenAnchors = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
            if (!fullScreenAnchors) continue;
            bool isUnderActive = activePanel != null && img.transform.IsChildOf(activePanel.transform);
            if (!isUnderActive)
            {
                img.raycastTarget = false;
            }
            else
            {
                // ensure active panel images still allow raycasts
                img.raycastTarget = true;
            }
        }
    }

    private void RestoreFullScreenBlockers()
    {
        var canv = FindObjectOfType<Canvas>();
        if (canv == null) return;
        var images = canv.GetComponentsInChildren<Image>(true);
        foreach (var img in images)
        {
            var rt = img.rectTransform;
            if (rt == null) continue;
            if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one)
                img.raycastTarget = true;
        }
    }
}