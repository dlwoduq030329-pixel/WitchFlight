using System;
using UnityEngine;
using BackEnd;
using LitJson;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance;

    private const string TABLE_NAME = "LoginPlayerData";

    // =========================================================
    // PlayerSetting
    // =========================================================

    [Serializable]
    public class PlayerSetting
    {
        public string nickname;

        public int hatIndex;
        public int broomIndex;
        public int magic1Index;
        public int magic2Index;
    }

    // 현재 로그인한 플레이어의 설정
    private PlayerSetting currentPlayerSetting;

    // DB row의 inDate
    private string currentPlayerSettingInDate;


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
        if (!Backend.IsInitialized)
        {
            Debug.LogError("BACKND가 초기화되지 않았습니다.");
            return;
        }

        if (!Backend.IsLogin)
        {
            Debug.LogError("로그인이 되어있지 않습니다.");
            return;
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
            Debug.LogError(
                $"데이터 조회 실패 : {callback.GetMessage()}"
            );

            return;
        }


        JsonData rows =
            callback.FlattenRows();


        // ---------------------------------------------------------
        // 데이터가 없는 신규 유저
        // ---------------------------------------------------------

        if (rows == null || rows.Count == 0)
        {
            currentPlayerSetting = null;
            currentPlayerSettingInDate = null;

            Debug.Log(
                $"[{TABLE_NAME}] 저장된 데이터가 없습니다."
            );

            return;
        }


        // ---------------------------------------------------------
        // 첫 번째 데이터 사용
        // ---------------------------------------------------------

        JsonData row = rows[0];


        if (row.Keys.Contains("inDate"))
        {
            currentPlayerSettingInDate =
                row["inDate"].ToString();
        }


        PlayerSetting setting =
            new PlayerSetting();


        // ---------------------------------------------------------
        // nickname
        // ---------------------------------------------------------

        if (row.Keys.Contains("nickname"))
        {
            setting.nickname =
                row["nickname"].ToString();
        }


        // ---------------------------------------------------------
        // hatIndex
        // ---------------------------------------------------------

        if (row.Keys.Contains("hatIndex"))
        {
            setting.hatIndex =
                ParseInt(row["hatIndex"]);
        }


        // ---------------------------------------------------------
        // broomIndex
        // ---------------------------------------------------------

        if (row.Keys.Contains("broomIndex"))
        {
            setting.broomIndex =
                ParseInt(row["broomIndex"]);
        }


        // ---------------------------------------------------------
        // magic1Index
        // ---------------------------------------------------------

        if (row.Keys.Contains("magic1Index"))
        {
            setting.magic1Index =
                ParseInt(row["magic1Index"]);
        }


        // ---------------------------------------------------------
        // magic2Index
        // ---------------------------------------------------------

        if (row.Keys.Contains("magic2Index"))
        {
            setting.magic2Index =
                ParseInt(row["magic2Index"]);
        }


        currentPlayerSetting =
            setting;


        // ---------------------------------------------------------
        // DataConfig에 적용
        // ---------------------------------------------------------

        DataConfig.hatIndex =
            setting.hatIndex;

        DataConfig.broomIndex =
            setting.broomIndex;

        DataConfig.magic1Index =
            setting.magic1Index;

        DataConfig.magic2Index =
            setting.magic2Index;


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
    }


    // =========================================================
    // 신규 데이터 등록
    // =========================================================

    public void RegisterPlayerSetting(string nickname)
    {
        if (!Backend.IsInitialized)
        {
            Debug.LogError("BACKND가 초기화되지 않았습니다.");
            return;
        }

        if (!Backend.IsLogin)
        {
            Debug.LogError("로그인이 되어있지 않습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            Debug.LogError("닉네임을 입력해주세요.");
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
        if (!Backend.IsInitialized)
        {
            Debug.LogError("BACKND가 초기화되지 않았습니다.");
            return;
        }

        if (!Backend.IsLogin)
        {
            Debug.LogError("로그인이 되어있지 않습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            Debug.LogError("닉네임을 입력해주세요.");
            return;
        }


        PlayerSetting setting =
            CreateCurrentSetting(nickname);


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
                Debug.LogError(
                    $"PlayerSetting 등록 실패 : {callback.GetMessage()}"
                );

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
                Debug.LogError(
                    $"PlayerSetting 수정 실패 : {callback.GetMessage()}"
                );

                return;
            }
        }


        currentPlayerSetting =
            setting;


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
            nickname;

        setting.hatIndex =
            DataConfig.hatIndex;

        setting.broomIndex =
            DataConfig.broomIndex;

        setting.magic1Index =
            DataConfig.magic1Index;

        setting.magic2Index =
            DataConfig.magic2Index;


        return setting;
    }


    // =========================================================
    // 숫자 변환
    // =========================================================

    private int ParseInt(JsonData value)
    {
        if (value == null)
            return 0;

        int result;

        if (int.TryParse(
            value.ToString(),
            out result))
        {
            return result;
        }

        return 0;
    }


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