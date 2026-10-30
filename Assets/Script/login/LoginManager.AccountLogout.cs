using System;
using System.Threading.Tasks;
using BackEnd;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class LoginManager
{
    // Runs on the persistent manager, so closing AccountSetUI cannot interrupt saving.
    public async void LogoutAndReloadScene()
    {
        if (IsLoggingOut) return;
        string scenePath = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
        {
            SetLoginStatus("현재 씬을 다시 불러올 수 없습니다. Build Settings를 확인해주세요.");
            Debug.LogError(LoginStatus);
            return;
        }

        IsLoggingOut = true;
        loginAttempt++;
        IsLoggingIn = false;
        try
        {
            SetLoginStatus("로그아웃 준비 중...");
            TitleProgression.Observe(PlayerData.Local, true);
            if (NetworkGameManager.Instance != null)
                await NetworkGameManager.Instance.DisconnectForLogoutAsync();

            // Do not clear an in-flight account save (profile, title or progression).
            SetLoginStatus("계정 정보 저장 중...");
            float deadline = Time.realtimeSinceStartup + 20f;
            while (DatabaseManager.Instance != null && DatabaseManager.Instance.IsAutoSavePending)
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new InvalidOperationException("계정 정보 저장이 지연되어 로그아웃하지 않았습니다. 연결 상태를 확인한 뒤 다시 눌러주세요.");
                await Task.Yield();
                if (this == null) return;
            }

            SetLoginStatus("로그아웃 중...");
            if (Backend.IsLogin)
            {
                var result = Backend.BMember.Logout();
                if (result == null || !result.IsSuccess())
                    throw new InvalidOperationException("로그아웃 실패: " + (result?.GetMessage() ?? "서버 응답 없음"));
            }
            IsLoggedIn = false;
            DatabaseManager.Instance?.ClearSession();
            SetLoginStatus(string.Empty);
            OnLoggedOut?.Invoke();
            NetworkGameManager.Instance?.DisposeAfterLogout();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var loading = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            if (loading == null) throw new InvalidOperationException("로그아웃 후 씬 로드를 시작하지 못했습니다.");
            while (!loading.isDone) await Task.Yield();
        }
        catch (Exception exception)
        {
            SetLoginStatus(exception.Message);
            Debug.LogException(exception);
        }
        finally { if (this != null) IsLoggingOut = false; }
    }
}
