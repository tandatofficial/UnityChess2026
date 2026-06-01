using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

[Serializable]
public class Account
{
    public string Username;
    public string Email;
    public string PasswordHash;
    public string Salt;
}

public class AccountManager
{
    private static AccountManager _instance;
    public static AccountManager Instance => _instance ?? (_instance = new AccountManager());

    public Account CurrentAccount { get; set; }
    public bool IsLoggedIn => CurrentAccount != null;

    public void Logout()
    {
        CurrentAccount = null;
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
    public GameObject verificationPanel;

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

    [Header("Verification Fields")]
    public TMP_InputField verificationCodeInput;

    private string pendingVerificationEmail;

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
        if (verificationPanel == null)
        {
            CreateVerificationPanel();
        }
        else
        {
            verificationPanel.SetActive(false);
            ConfigureVerificationButtons();
        }
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

        StartCoroutine(LoginCoroutine(id, pw));
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

        StartCoroutine(RegisterCoroutine(username, email, pw));
    }

    public void OnVerifyButton()
    {
        string code = verificationCodeInput ? verificationCodeInput.text.Trim() : "";
        StartCoroutine(VerifyCoroutine(pendingVerificationEmail, code));
    }

    private void CreateVerificationPanel()
    {
        if (loginPanel == null) return;

        // Clone loginPanel
        verificationPanel = Instantiate(loginPanel, loginPanel.transform.parent);
        verificationPanel.name = "VerificationPanel";
        verificationPanel.SetActive(false);

        // Find input fields
        TMP_InputField[] inputs = verificationPanel.GetComponentsInChildren<TMP_InputField>(true);
        if (inputs.Length >= 2)
        {
            verificationCodeInput = inputs[0];
            verificationCodeInput.name = "Input_VerificationCode";
            verificationCodeInput.text = "";

            // Set Placeholder text
            var placeholder = verificationCodeInput.placeholder as TMP_Text;
            if (placeholder != null) placeholder.text = "Enter 6-digit code...";

            // Destroy the password field
            Destroy(inputs[1].gameObject);
        }

        // Find title text and change to "VERIFY EMAIL"
        TMP_Text[] texts = verificationPanel.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts)
        {
            if (t.text == "Login" || t.text == "LOGIN")
            {
                t.text = "VERIFY EMAIL";
            }
        }

        // Configure buttons
        Button[] buttons = verificationPanel.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            string btnName = b.gameObject.name.ToLower();
            TMP_Text btnTextComp = b.GetComponentInChildren<TMP_Text>();
            string btnText = btnTextComp != null ? btnTextComp.text.ToLower() : "";

            bool isBackButton = btnName.Contains("back") || btnName.Contains("cancel") || 
                               btnText.Contains("back") || btnText.Contains("cancel") || btnText.Contains("quay");

            if (isBackButton)
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(ShowStartPanel);
            }
            else
            {
                if (btnTextComp != null && (btnTextComp.text == "Login" || btnTextComp.text == "LOGIN"))
                {
                    btnTextComp.text = "Verify";
                }
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(OnVerifyButton);
            }
        }
    }

    private void ConfigureVerificationButtons()
    {
        if (verificationPanel == null) return;

        Button[] buttons = verificationPanel.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            string btnName = b.gameObject.name.ToLower();
            TMP_Text btnTextComp = b.GetComponentInChildren<TMP_Text>();
            string btnText = btnTextComp != null ? btnTextComp.text.ToLower() : "";

            bool isBackButton = btnName.Contains("back") || btnName.Contains("cancel") || 
                               btnText.Contains("back") || btnText.Contains("cancel") || btnText.Contains("quay");

            if (isBackButton)
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(ShowStartPanel);
            }
            else
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(OnVerifyButton);
            }
        }
    }

    public void ShowVerificationPanel()
    {
        if (startPanel) startPanel.SetActive(false);
        if (loginPanel) loginPanel.SetActive(false);
        if (registerPanel) registerPanel.SetActive(false);
        if (verificationPanel) verificationPanel.SetActive(true);

        SetFeedback("", true);
        BringPanelToFront(verificationPanel);
        DisableFullScreenBlockersExcept(verificationPanel);

        if (verificationCodeInput != null)
        {
            EventSystem.current.SetSelectedGameObject(verificationCodeInput.gameObject);
            verificationCodeInput.ActivateInputField();
        }
    }

    [Serializable]
    public class RegisterData
    {
        public string username;
        public string email;
        public string password;
    }

    [Serializable]
    public class VerifyData
    {
        public string email;
        public string code;
    }

    [Serializable]
    public class LoginData
    {
        public string usernameOrEmail;
        public string password;
    }

    [Serializable]
    public class BackendResponse
    {
        public string message;
        public string error;
        public UserData user;
    }

    [Serializable]
    public class UserData
    {
        public string username;
        public string email;
        public string passwordHash;
        public string salt;
    }

    private IEnumerator RegisterCoroutine(string username, string email, string password)
    {
        SetFeedback("Registering...", true);
        string url = "http://localhost:11100/api/auth/register";

        RegisterData data = new RegisterData { username = username, email = email, password = password };
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                pendingVerificationEmail = email;
                string successMsg = "Registration successful. Check your email for verification code.";
                try
                {
                    BackendResponse res = JsonUtility.FromJson<BackendResponse>(request.downloadHandler.text);
                    if (res != null && !string.IsNullOrEmpty(res.message)) successMsg = res.message;
                }
                catch {}
                SetFeedback(successMsg, true);

                if (regUsername) regUsername.text = "";
                if (regEmail) regEmail.text = "";
                if (regPassword) regPassword.text = "";
                if (regConfirmPassword) regConfirmPassword.text = "";

                ShowVerificationPanel();
            }
            else
            {
                string errMsg = "Registration failed";
                try
                {
                    BackendResponse res = JsonUtility.FromJson<BackendResponse>(request.downloadHandler.text);
                    if (res != null && !string.IsNullOrEmpty(res.error)) errMsg = res.error;
                }
                catch {}
                SetFeedback(errMsg, false);
            }
        }
    }

    private IEnumerator VerifyCoroutine(string email, string code)
    {
        SetFeedback("Verifying...", true);
        string url = "http://localhost:11100/api/auth/verify";

        VerifyData data = new VerifyData { email = email, code = code };
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                SetFeedback("Verification successful! You can now log in.", true);
                ShowLoginPanel();
            }
            else
            {
                string errMsg = "Verification failed";
                try
                {
                    BackendResponse res = JsonUtility.FromJson<BackendResponse>(request.downloadHandler.text);
                    if (res != null && !string.IsNullOrEmpty(res.error)) errMsg = res.error;
                }
                catch {}
                SetFeedback(errMsg, false);
            }
        }
    }

    private IEnumerator LoginCoroutine(string id, string pw)
    {
        SetFeedback("Logging in...", true);
        string url = "http://localhost:11100/api/auth/login";

        LoginData data = new LoginData { usernameOrEmail = id, password = pw };
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                BackendResponse res = null;
                try {
                    res = JsonUtility.FromJson<BackendResponse>(request.downloadHandler.text);
                } catch {}

                if (res != null && res.user != null)
                {
                    AccountManager.Instance.CurrentAccount = new Account {
                        Username = res.user.username,
                        Email = res.user.email,
                        PasswordHash = res.user.passwordHash,
                        Salt = res.user.salt
                    };
                }

                PlayerPrefs.SetString("loggedInUser", id);
                PlayerPrefs.Save();

                SetFeedback("Login successful", true);
                SceneManager.LoadScene(mainMenuSceneName);
            }
            else
            {
                string errMsg = "Login failed";
                try
                {
                    BackendResponse res = JsonUtility.FromJson<BackendResponse>(request.downloadHandler.text);
                    if (res != null && !string.IsNullOrEmpty(res.error)) errMsg = res.error;
                }
                catch {}
                SetFeedback(errMsg, false);
            }
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