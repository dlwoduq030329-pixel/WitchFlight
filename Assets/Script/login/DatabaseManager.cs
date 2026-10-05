using System;
using UnityEngine;
using BackEnd;
using LitJson;
using System.Globalization;
using System.Collections.Concurrent;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance;

    private const string TABLE_NAME = "LoginPlayerData";

    [Header("New player defaults (used only when LoginPlayerData has no row)")]
    [SerializeField] private PlayerSetting newPlayerDefaults = new PlayerSetting
    {
        nickname = "test",
        magic1Index = (int)MagicType.None,
        magic2Index = (int)MagicType.Fire
    };

    [Header("DataConfig auto save")]
    [Tooltip("저장 실패 시 재시도 대기 시간입니다. 연속 실패 시 최대 30초까지 늘어납니다.")]
    [SerializeField, Min(0.5f)] private float autoSaveRetryDelay = 2f;

    // =========================================================
    // PlayerSetting
    // =========================================================

    [Serializable]
    public class PlayerSetting
    {
        public string nickname;

        public int hatIndex = (int)HatType.Classic;
        public int broomIndex = (int)BroomType.Standard;
        public int magic1Index = (int)MagicType.Fire;
        public int magic2Index = (int)MagicType.Ice;
        public int playerprofile;
        public int hairStylePreset;
        public float bangsLength;
        public float bangsDirection = 0.5f;
        public float sideHairLength;
        public float ahogeLength;
        public Color hairColor = Color.white;
        public Color clothColor = Color.white;
        public Color eyeColor = Color.white;
        public int wandIndex;

        public PlayerConfig GetPlayerConfig() => new PlayerConfig
        {
            hairStylePreset = hairStylePreset, bangsLength = bangsLength, bangsDirection = bangsDirection,
            sideHairLength = sideHairLength, ahogeLength = ahogeLength,
            hairColor = hairColor, clothColor = clothColor, eyeColor = eyeColor,
            hatIndex = hatIndex, broomIndex = broomIndex, wandIndex = wandIndex
        }.Sanitized();

        public void SetPlayerConfig(PlayerConfig config)
        {
            config = config.Sanitized();
            hairStylePreset = config.hairStylePreset;
            bangsLength = config.bangsLength;
            bangsDirection = config.bangsDirection;
            sideHairLength = config.sideHairLength;
            ahogeLength = config.ahogeLength;
            hairColor = config.hairColor;
            clothColor = config.clothColor;
            eyeColor = config.eyeColor;
            hatIndex = config.hatIndex;
            broomIndex = config.broomIndex;
            wandIndex = config.wandIndex;
        }
    }

    // 현재 로그인한 플레이어의 설정
    private PlayerSetting currentPlayerSetting;

    // DB row의 inDate
    private string currentPlayerSettingInDate;
    private string loadedUserInDate;
    private bool registrationWaitingForInitialization;
    private int registrationRequestVersion;
    private bool autoSaveDirty, autoSaveInFlight;
    private float nextAutoSaveTime;
    private int autoSaveFailures;
    private int dataSessionVersion;
    private readonly ConcurrentQueue<Action> saveCompletions = new ConcurrentQueue<Action>();
    public bool IsAutoSavePending => autoSaveDirty || autoSaveInFlight;
    public bool IsPlayerSettingLoaded { get; private set; }
    public bool IsDataConfigReady { get; private set; }
    public bool LastSaveSucceeded { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public bool HasLoadedProfile => Backend.IsLogin && IsPlayerSettingLoaded &&
        loadedUserInDate == Backend.UserInDate && HasPlayerSetting() &&
        !string.IsNullOrWhiteSpace(currentPlayerSetting.nickname);

    public void ClearSession()
    {
        dataSessionVersion++;
        autoSaveDirty = autoSaveInFlight = false;
        nextAutoSaveTime = 0f;
        autoSaveFailures = 0;
        // Logout/account changes cancel registration waiting on SDK initialization.
        registrationRequestVersion++;
        registrationWaitingForInitialization = false;
        currentPlayerSetting = null;
        currentPlayerSettingInDate = null;
        loadedUserInDate = null;
        IsPlayerSettingLoaded = false;
        IsDataConfigReady = false;
        LastSaveSucceeded = false;
        LastError = string.Empty;
        DataConfig.ResetToDefaults();
    }


    // =========================================================
    // Singleton
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
        DataConfig.Changed += OnDataConfigChanged;
    }

    private void OnDestroy()
    {
        DataConfig.Changed -= OnDataConfigChanged;
        dataSessionVersion++;
        if (Instance == this) Instance = null;
    }

    private bool CanAutoSave => Backend.IsInitialized && Backend.IsLogin && IsDataConfigReady &&
        IsPlayerSettingLoaded && loadedUserInDate == Backend.UserInDate && HasPlayerSetting();

    private void OnDataConfigChanged()
    {
        // Never turn an uninitialized/default snapshot into a write to another account.
        if (!CanAutoSave) return;
        autoSaveDirty = true;
        LastSaveSucceeded = false;
    }

    private void Update()
    {
        // SDK callbacks may run off-thread. All local state is updated here.
        while (saveCompletions.TryDequeue(out Action complete)) complete();
    }

    private void LateUpdate()
    {
        // One snapshot per frame, and at most one request in flight. Further edits
        // are coalesced into the next snapshot instead of producing stale writes.
        if (!autoSaveDirty || autoSaveInFlight || !CanAutoSave || Time.unscaledTime < nextAutoSaveTime) return;
        if (string.IsNullOrWhiteSpace(DataConfig.playerName))
        {
            nextAutoSaveTime = Time.unscaledTime + Mathf.Max(0.5f, autoSaveRetryDelay);
            Fail("자동 저장할 닉네임이 비어 있습니다. DataConfig.playerName을 확인해주세요.");
            return;
        }

        PlayerSetting snapshot = CreateCurrentSetting(DataConfig.playerName);
        string owner = loadedUserInDate;
        string row = currentPlayerSettingInDate;
        int session = dataSessionVersion;
        autoSaveDirty = false;
        autoSaveInFlight = true;
        LastSaveSucceeded = false;
        try
        {
            Backend.GameData.UpdateV2(TABLE_NAME, row, owner, CreateParam(snapshot), result =>
                saveCompletions.Enqueue(() => CompleteAutoSave(session, owner, row, snapshot, result)));
        }
        catch (Exception exception)
        {
            AutoSaveFailed(exception.Message);
        }
    }

    private void CompleteAutoSave(int session, string owner, string row, PlayerSetting snapshot, BackendReturnObject result)
    {
        // A late response after logout/relogin must never overwrite the new account's state.
        if (this == null || session != dataSessionVersion) return;
        autoSaveInFlight = false;
        if (!Backend.IsLogin || owner != Backend.UserInDate || owner != loadedUserInDate || row != currentPlayerSettingInDate)
        {
            autoSaveDirty = false;
            return;
        }
        if (result == null || !result.IsSuccess())
        {
            AutoSaveFailed(result == null ? "서버 응답이 없습니다." : result.GetMessage());
            return;
        }
        currentPlayerSetting = snapshot;
        // Do NOT apply this older snapshot to DataConfig: the user may have edited it in flight.
        LastSaveSucceeded = true;
        LastError = string.Empty;
        autoSaveFailures = 0;
        nextAutoSaveTime = 0f;
    }

    private void AutoSaveFailed(string error)
    {
        autoSaveInFlight = false;
        autoSaveDirty = true;
        LastSaveSucceeded = false;
        autoSaveFailures = Mathf.Min(autoSaveFailures + 1, 5);
        nextAutoSaveTime = Time.unscaledTime + Mathf.Min(30f,
            Mathf.Max(0.5f, autoSaveRetryDelay) * Mathf.Pow(2f, autoSaveFailures - 1));
        Fail($"유저 정보 자동 저장 실패 (재시도 예정): {error}");
    }


    // =========================================================
    // 로그인 성공 후 호출
    // =========================================================

    public void InitializeDatabase()
    {
        if (!Backend.IsInitialized)
        {
            Debug.LogError("BACKND가 초기화되지 않았습니다.");
            return;
        }

        if (!Backend.IsLogin)
        {
            Debug.LogError("BACKND 로그인이 되어있지 않습니다.");
            return;
        }

        Debug.Log("Database 초기화 시작");

        LoadPlayerSetting();
    }

   


    // =========================================================
    // 내 데이터 조회
    // =========================================================

    public void LoadPlayerSetting()
    {
        TryLoadPlayerSetting(true);
    }

    // Login checks the saved state without initializing the lobby character yet.
    public bool TryLoadPlayerSetting(bool applyToDataConfig)
    {
        try { return LoadPlayerSettingCore(applyToDataConfig); }
        catch (Exception exception)
        {
            ClearSession();
            return Fail($"데이터 조회 중 오류가 발생했습니다: {exception.Message}");
        }
    }

    private bool LoadPlayerSettingCore(bool applyToDataConfig)
    {
        // A missing column/row or failed load must not reuse another account's values.
        ClearSession();
        if (!Backend.IsInitialized)
        {
            return Fail("BACKND가 초기화되지 않았습니다.");
        }

        if (!Backend.IsLogin)
        {
            return Fail("로그인이 되어있지 않습니다.");
        }


        Debug.Log(
            $"[{TABLE_NAME}] 데이터 조회 시작"
        );


        // 현재 로그인한 유저의 데이터 조회
        BackendReturnObject callback =
            Backend.GameData.GetMyData(
                TABLE_NAME,
                new Where()
            );


        if (!callback.IsSuccess())
        {
            return Fail($"데이터 조회 실패 : {callback.GetMessage()}");
        }


        JsonData rows =
            callback.FlattenRows();

        if (rows == null || !rows.IsArray)
            return Fail("데이터 조회 응답 형식이 올바르지 않습니다. 다시 조회해주세요.");
        if (rows.Count > 1)
            return Fail("계정에 여러 PlayerSetting 행이 있습니다. 중복 데이터를 확인해주세요.");


        // ---------------------------------------------------------
        // 데이터가 없는 신규 유저
        // ---------------------------------------------------------

        if (rows.Count == 0)
        {
            loadedUserInDate = Backend.UserInDate;
            IsPlayerSettingLoaded = true;
            return InitializeMissingPlayerSetting(applyToDataConfig);
        }


        // ---------------------------------------------------------
        // 첫 번째 데이터 사용
        // ---------------------------------------------------------

        JsonData row = rows[0];


        currentPlayerSettingInDate = ReadString(row, "inDate", string.Empty);
        if (string.IsNullOrEmpty(currentPlayerSettingInDate))
        {
            return Fail("저장 데이터의 inDate가 없습니다. 다시 조회해주세요.");
        }

        PlayerSetting setting = ReadPlayerSetting(row);


        currentPlayerSetting =
            setting;


        // ---------------------------------------------------------
        // DataConfig에 적용
        // ---------------------------------------------------------

        loadedUserInDate = Backend.UserInDate;
        IsPlayerSettingLoaded = true;
        if (applyToDataConfig) ApplyLoadedSettingToDataConfig();


        Debug.Log(
            "====================================\n" +
            "PlayerSetting 불러오기 성공\n" +
            $"inDate : {currentPlayerSettingInDate}\n" +
            $"nickname : {setting.nickname}\n" +
            $"hatIndex : {setting.hatIndex}\n" +
            $"broomIndex : {setting.broomIndex}\n" +
            $"magic1Index : {setting.magic1Index}\n" +
            $"magic2Index : {setting.magic2Index}\n" +
            "===================================="
        );
        return true;
    }

    private bool InitializeMissingPlayerSetting(bool applyToDataConfig)
    {
        // Copy Inspector defaults, never a previous account's DataConfig.
        PlayerSetting defaults = newPlayerDefaults ?? new PlayerSetting
        {
            nickname = "test", magic1Index = 0, magic2Index = 1
        };
        var setting = new PlayerSetting
        {
            nickname = string.IsNullOrWhiteSpace(defaults.nickname) ? "test" : defaults.nickname.Trim(),
            magic1Index = NormalizeMagicIndex(defaults.magic1Index, (int)MagicType.None),
            magic2Index = NormalizeMagicIndex(defaults.magic2Index, (int)MagicType.Fire),
            playerprofile = Mathf.Max(0, defaults.playerprofile)
        };
        setting.SetPlayerConfig(defaults.GetPlayerConfig());

        // Called only after a successful GetMyData returned an empty array.
        BackendReturnObject result = Backend.GameData.Insert(TABLE_NAME, CreateParam(setting));
        if (!result.IsSuccess())
        {
            IsPlayerSettingLoaded = false; // Retry must query again before inserting.
            return Fail($"기본 유저 데이터 생성 실패: {result.GetMessage()}");
        }
        string inDate = result.GetInDate();
        if (string.IsNullOrWhiteSpace(inDate))
        {
            IsPlayerSettingLoaded = false;
            return Fail("기본 데이터 저장 결과를 확인할 수 없습니다. 다시 조회해주세요.");
        }

        currentPlayerSetting = setting;
        currentPlayerSettingInDate = inDate;
        LastSaveSucceeded = true;
        Debug.Log($"[{TABLE_NAME}] 기본 유저 데이터 생성 완료: {setting.nickname}");
        // The Main login flow applies the snapshot when opening the lobby UI.
        return !applyToDataConfig || ApplyLoadedSettingToDataConfig();
    }

    // Call only after switching the Main scene's UI to the lobby.
    public bool ApplyLoadedSettingToDataConfig()
    {
        if (!HasLoadedProfile)
            return Fail("LoginPlayerData의 저장 데이터 또는 nickname이 없습니다. 회원가입 시 저장 경로를 확인해주세요.");
        ApplyToDataConfig(currentPlayerSetting);
        IsDataConfigReady = true;
        LastError = string.Empty;
        return true;
    }

    public void SuspendLobbyInitialization() => IsDataConfigReady = false;

    // Explicit signup UI action. Login never opens a nickname UI.
    public bool SaveRegistrationNickname(string nickname)
    {
        LastSaveSucceeded = false;
        LastError = string.Empty;
        if (string.IsNullOrWhiteSpace(nickname)) return Fail("닉네임을 입력해주세요.");
        if (!Backend.IsInitialized) return Fail("BACKND 초기화가 완료되지 않았습니다.");
        if (!Backend.IsLogin) return Fail("회원가입 계정의 인증 상태를 확인해주세요. 다시 로그인 후 시도해주세요.");
        if (autoSaveInFlight) return Fail("자동 저장이 진행 중입니다. 완료 후 닉네임을 확정해주세요.");
        // Signup reaches this method without going through the login UI's load path.
        // A successful empty lookup creates defaults; then update that SAME row's nickname.
        if ((!IsPlayerSettingLoaded || loadedUserInDate != Backend.UserInDate || !HasPlayerSetting()) &&
            !TryLoadPlayerSetting(false)) return false;
        if (!CanSaveLoadedAccount()) return false;
        // A generated default profile must still accept the signup nickname.
        if (HasLoadedProfile && currentPlayerSetting.nickname == nickname.Trim() && !autoSaveDirty)
        {
            LastSaveSucceeded = true;
            return ApplyLoadedSettingToDataConfig();
        }
        SaveCurrentSettingInternal(nickname, true);
        return LastSaveSucceeded && HasLoadedProfile && ApplyLoadedSettingToDataConfig();
    }

    private bool Fail(string message)
    {
        LastError = message;
        Debug.LogError(message);
        return false;
    }


    // =========================================================
    // 신규 데이터 등록
    // =========================================================

    public void RegisterPlayerSetting(string nickname)
    {
        if (registrationWaitingForInitialization) return;
        LastSaveSucceeded = false;
        LastError = string.Empty;
        if (!Backend.IsInitialized)
        {
            LoginManager loginManager = LoginManager.Instance;
            if (loginManager == null)
            {
                Fail("BACKND 초기화를 요청할 LoginManager가 없습니다.");
                return;
            }

            registrationWaitingForInitialization = true;
            int requestVersion = ++registrationRequestVersion;
            Debug.Log("BACKND 초기화를 시도합니다. 성공 후 유저 정보 등록을 재개합니다.");
            loginManager.EnsureBackendInitialized((success, error) =>
            {
                if (this == null || requestVersion != registrationRequestVersion) return;
                registrationWaitingForInitialization = false;
                if (!success)
                {
                    Fail($"BACKND 초기화 실패로 등록을 중단했습니다: {error}");
                    return;
                }
                // Re-check login and the loaded account before writing any data.
                RegisterPlayerSetting(nickname);
            });
            return;
        }

        if (!Backend.IsLogin)
        {
            Fail("로그인이 되어있지 않습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            Fail("닉네임을 입력해주세요.");
            return;
        }

        if (!CanSaveLoadedAccount()) return;
        if (HasPlayerSetting())
        {
            Debug.LogWarning("PlayerSetting already exists. Use SaveCurrentSetting to update it.");
            return;
        }


        PlayerSetting setting =
            CreateCurrentSetting(nickname);


        Param param =
            CreateParam(setting);


        BackendReturnObject callback =
            Backend.GameData.Insert(
                TABLE_NAME,
                param
            );


        if (!callback.IsSuccess())
        {
            Debug.LogError(
                $"PlayerSetting 등록 실패 : {callback.GetMessage()}"
            );

            return;
        }


        // 새 row의 inDate 저장
        currentPlayerSettingInDate =
            callback.GetInDate();

        currentPlayerSetting =
            setting;
        ApplyToDataConfig(setting);
        LastSaveSucceeded = true;
        IsDataConfigReady = HasLoadedProfile;

        Debug.Log(
            $"PlayerSetting 등록 성공\n" +
            $"inDate : {currentPlayerSettingInDate}"
        );
    }


    // =========================================================
    // 현재 설정 저장
    //
    // 데이터가 없으면 Insert
    // 데이터가 있으면 UpdateV2
    // =========================================================

    public void SaveCurrentSetting(string nickname)
    {
        SaveCurrentSettingInternal(nickname, false);
    }

    private void SaveCurrentSettingInternal(string nickname, bool registeringNickname)
    {
        try { SaveCurrentSettingCore(nickname, registeringNickname); }
        catch (Exception exception)
        {
            LastSaveSucceeded = false;
            // The server may have accepted the write before a client-side exception.
            // Require a fresh read before any retry can insert a second row.
            IsPlayerSettingLoaded = false;
            IsDataConfigReady = false;
            Fail($"저장 결과를 확인할 수 없습니다. 다시 로그인하여 데이터를 조회해주세요: {exception.Message}");
        }
    }

    private void SaveCurrentSettingCore(string nickname, bool registeringNickname)
    {
        LastSaveSucceeded = false;
        LastError = string.Empty;
        if (autoSaveInFlight)
        {
            Fail("자동 저장이 진행 중입니다. 완료 후 다시 저장해주세요.");
            return;
        }
        if (!Backend.IsInitialized)
        {
            Fail("BACKND가 초기화되지 않았습니다.");
            return;
        }

        if (!Backend.IsLogin)
        {
            Fail("로그인이 되어있지 않습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            Fail("닉네임을 입력해주세요.");
            return;
        }

        if (!CanSaveLoadedAccount()) return;
        if (!registeringNickname && HasPlayerSetting() && !IsDataConfigReady)
        {
            Fail("로비 데이터를 적용한 후 커스터마이징을 저장해주세요.");
            return;
        }


        PlayerSetting setting =
            CreateCurrentSetting(nickname);

        // Signup nickname registration must preserve any previously saved customization.
        if (registeringNickname && currentPlayerSetting != null && !IsDataConfigReady)
        {
            setting.SetPlayerConfig(currentPlayerSetting.GetPlayerConfig());
            setting.playerprofile = currentPlayerSetting.playerprofile;
            setting.magic1Index = currentPlayerSetting.magic1Index;
            setting.magic2Index = currentPlayerSetting.magic2Index;
        }


        Param param =
            CreateParam(setting);


        BackendReturnObject callback;


        // =====================================================
        // 신규 데이터
        // =====================================================

        if (string.IsNullOrEmpty(
            currentPlayerSettingInDate))
        {
            callback =
                Backend.GameData.Insert(
                    TABLE_NAME,
                    param
                );


            if (!callback.IsSuccess())
            {
                Fail($"PlayerSetting 등록 실패 : {callback.GetMessage()}");

                return;
            }


            currentPlayerSettingInDate =
                callback.GetInDate();
        }


        // =====================================================
        // 기존 데이터 수정
        // =====================================================

        else
        {
            callback =
                Backend.GameData.UpdateV2(
                    TABLE_NAME,
                    currentPlayerSettingInDate,
                    Backend.UserInDate,
                    param
                );


            if (!callback.IsSuccess())
            {
                Fail($"PlayerSetting 수정 실패 : {callback.GetMessage()}");

                return;
            }
        }


        currentPlayerSetting =
            setting;
        // Apply only when the lobby has initialized DataConfig for this account.
        if (IsDataConfigReady) ApplyToDataConfig(setting);
        LastSaveSucceeded = true;

        // A successful explicit save already includes every pending local change.
        autoSaveDirty = false;
        autoSaveFailures = 0;
        nextAutoSaveTime = 0f;

        Debug.Log(
            "PlayerSetting 저장 성공"
        );
    }


    // =========================================================
    // PlayerSetting → Param
    // =========================================================

    private Param CreateParam(
        PlayerSetting setting)
    {
        Param param =
            new Param();


        param.Add(
            "nickname",
            setting.nickname
        );

        param.Add(
            "hatIndex",
            setting.hatIndex
        );

        param.Add(
            "broomIndex",
            setting.broomIndex
        );

        param.Add(
            "magic1Index",
            setting.magic1Index
        );

        param.Add(
            "magic2Index",
            setting.magic2Index
        );
        PlayerConfig config = setting.GetPlayerConfig();
        param.Add("playerprofile", setting.playerprofile);
        param.Add("hairStylePreset", config.hairStylePreset);
        param.Add("bangsLength", config.bangsLength);
        param.Add("bangsDirection", config.bangsDirection);
        param.Add("sideHairLength", config.sideHairLength);
        param.Add("ahogeLength", config.ahogeLength);
        param.Add("hairColor", "#" + ColorUtility.ToHtmlStringRGBA(config.hairColor));
        param.Add("clothColor", "#" + ColorUtility.ToHtmlStringRGBA(config.clothColor));
        param.Add("eyeColor", "#" + ColorUtility.ToHtmlStringRGBA(config.eyeColor));
        // Wand appearance is not persisted; the current 14-column schema omits wandIndex.
        // Schema-defined LoginPlayerData stores bangsLength only. ReadPlayerSetting
        // still accepts legacy hairLength rows, but new writes must not require that column.

        return param;
    }


    // =========================================================
    // 현재 DataConfig → PlayerSetting
    // =========================================================

    private PlayerSetting CreateCurrentSetting(
        string nickname)
    {
        PlayerSetting setting =
            new PlayerSetting();


        setting.nickname =
            nickname.Trim();

        setting.hatIndex =
            DataConfig.hatIndex;

        setting.broomIndex =
            DataConfig.broomIndex;

        setting.magic1Index =
            DataConfig.magic1Index;

        setting.magic2Index =
            DataConfig.magic2Index;
        setting.SetPlayerConfig(DataConfig.GetPlayerConfig());
        setting.playerprofile = Mathf.Max(0, DataConfig.playerprofile);
        setting.magic1Index = NormalizeMagicIndex(setting.magic1Index, (int)MagicType.Fire);
        setting.magic2Index = NormalizeMagicIndex(setting.magic2Index, (int)MagicType.Ice);

        return setting;
    }


    // =========================================================
    // 숫자 변환
    // =========================================================

    private bool CanSaveLoadedAccount()
    {
        if (Backend.IsLogin && IsPlayerSettingLoaded && loadedUserInDate == Backend.UserInDate) return true;
        return Fail("현재 계정의 데이터 조회를 먼저 완료해주세요. 조회 실패 시 저장하지 않습니다.");
    }

    // Consumes a FlattenRows() row. Missing columns support existing accounts.
    public static PlayerSetting ReadPlayerSetting(JsonData row)
    {
        var setting = new PlayerSetting
        {
            nickname = ReadString(row, "nickname", string.Empty),
            hatIndex = ReadInt(row, "hatIndex", (int)HatType.Classic),
            broomIndex = ReadInt(row, "broomIndex", (int)BroomType.Standard),
            magic1Index = NormalizeMagicIndex(ReadInt(row, "magic1Index", (int)MagicType.Fire), (int)MagicType.Fire),
            magic2Index = NormalizeMagicIndex(ReadInt(row, "magic2Index", (int)MagicType.Ice), (int)MagicType.Ice),
            playerprofile = Mathf.Max(0, ReadInt(row, "playerprofile", 0)),
            hairStylePreset = ReadInt(row, "hairStylePreset", 0),
            bangsLength = ReadFloat(row, "bangsLength", ReadFloat(row, "hairLength", 0f)),
            bangsDirection = ReadFloat(row, "bangsDirection", 0.5f),
            sideHairLength = ReadFloat(row, "sideHairLength", 0f),
            ahogeLength = ReadFloat(row, "ahogeLength", 0f),
            hairColor = ReadColor(row, "hairColor"),
            clothColor = ReadColor(row, "clothColor"),
            eyeColor = ReadColor(row, "eyeColor"),
            wandIndex = ReadInt(row, "wandIndex", 0)
        };
        setting.SetPlayerConfig(setting.GetPlayerConfig());
        return setting;
    }

    private static void ApplyToDataConfig(PlayerSetting setting)
    {
        using var batch = DataConfig.BeginChangeBatch(false);
        DataConfig.ApplyPlayerConfig(setting.GetPlayerConfig());
        // Reuse the existing backend column; no duplicate playerName column is needed.
        DataConfig.playerName = (setting.nickname ?? string.Empty).Trim();
        DataConfig.playerprofile = setting.playerprofile;
        DataConfig.magic1Index = setting.magic1Index;
        DataConfig.magic2Index = setting.magic2Index;
    }

    private static JsonData ReadValue(JsonData row, string key)
        => row != null && row.IsObject && row.Keys.Contains(key) ? row[key] : null;

    private static string ReadString(JsonData row, string key, string fallback)
    {
        JsonData value = ReadValue(row, key);
        return value != null && value.IsString ? (string)value : fallback;
    }

    private static int ReadInt(JsonData row, string key, int fallback)
    {
        JsonData value = ReadValue(row, key);
        return value != null && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : fallback;
    }

    private static float ReadFloat(JsonData row, string key, float fallback)
    {
        JsonData value = ReadValue(row, key);
        if (value == null) return fallback;
        float number;
        if (value.IsDouble) number = (float)(double)value;
        else if (!float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return fallback;
        return float.IsNaN(number) || float.IsInfinity(number) ? fallback : number;
    }

    private static Color ReadColor(JsonData row, string key)
    {
        string html = ReadString(row, key, string.Empty);
        return !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out Color color) ? color : Color.white;
    }

    private static int NormalizeMagicIndex(int value, int fallback)
        => value >= (int)MagicType.None && value <= (int)MagicType.Scane ? value : fallback;


    // =========================================================
    // 외부 접근
    // =========================================================

    public PlayerSetting GetPlayerSetting()
    {
        return currentPlayerSetting;
    }


    public string GetNickname()
    {
        if (currentPlayerSetting == null)
            return string.Empty;

        return currentPlayerSetting.nickname;
    }


    public string GetPlayerSettingInDate()
    {
        return currentPlayerSettingInDate;
    }


    public bool HasPlayerSetting()
    {
        return
            currentPlayerSetting != null &&
            !string.IsNullOrEmpty(
                currentPlayerSettingInDate
            );
    }
}
