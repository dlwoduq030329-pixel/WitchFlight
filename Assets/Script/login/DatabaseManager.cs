using System;
using System.Threading.Tasks;
using UnityEngine;
using BACKND.Database;


// =====================================================
// PlayerSetting
// BACKND Database : LoginPlayerData
// =====================================================
[Serializable]
public class PlayerSetting : BaseModel
{
    // =================================================
    // 실제 저장할 데이터
    // =================================================

    public string nickname;

    public int hatIndex;
    public int broomIndex;
    public int magic1Index;
    public int magic2Index;


    // =================================================
    // 테이블 이름
    // =================================================

    public override string GetTableName()
    {
        return "LoginPlayerData";
    }


    // =================================================
    // 테이블 타입
    // =================================================

    public override TableType GetTableType()
    {
        return TableType.UserTable;
    }


    // =================================================
    // 클라이언트 접근 허용
    // =================================================

    public override bool GetClientAccess()
    {
        return true;
    }


    // =================================================
    // 읽기 권한
    // =================================================

    public override string[] GetReadPermissions()
    {
        return new string[]
        {
            "SELF"
        };
    }


    // =================================================
    // 쓰기 권한
    // =================================================

    public override string[] GetWritePermissions()
    {
        return new string[]
        {
            "SELF"
        };
    }


    // =================================================
    // Primary Key
    //
    // 현재는 별도의 PK를 사용하지 않고
    // 현재 로그인 유저 기준으로 접근
    // =================================================

    public override string[] GetPrimaryKeyColumnNames()
    {
        return new string[0];
    }


    // =================================================
    // 컬럼 목록
    // =================================================

    public override string GetColumnList()
    {
        return
            "nickname, " +
            "hatIndex, " +
            "broomIndex, " +
            "magic1Index, " +
            "magic2Index";
    }


    // =================================================
    // 컬럼 타입
    // =================================================

    public override string GetColumnDataType(string columnName)
    {
        switch (columnName)
        {
            case "nickname":
                return "TEXT";

            case "hatIndex":
                return "INT";

            case "broomIndex":
                return "INT";

            case "magic1Index":
                return "INT";

            case "magic2Index":
                return "INT";

            default:
                return string.Empty;
        }
    }


    // =================================================
    // Nullable
    // =================================================

    public override bool IsColumnNullable(string columnName)
    {
        return true;
    }


    // =================================================
    // Property Nullable
    // =================================================

    public override bool IsPropertyNullableType(string columnName)
    {
        return false;
    }


    // =================================================
    // Default Value
    // =================================================

    public override string GetColumnDefaultValue(string columnName)
    {
        return string.Empty;
    }


    // =================================================
    // Property → DB Column
    // =================================================

    public override string GetColumnName(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(nickname):
                return "nickname";

            case nameof(hatIndex):
                return "hatIndex";

            case nameof(broomIndex):
                return "broomIndex";

            case nameof(magic1Index):
                return "magic1Index";

            case nameof(magic2Index):
                return "magic2Index";

            default:
                return string.Empty;
        }
    }


    // =================================================
    // DB Column → Value
    // =================================================

    public override object GetValue(string columnName)
    {
        switch (columnName)
        {
            case "nickname":
                return nickname;

            case "hatIndex":
                return hatIndex;

            case "broomIndex":
                return broomIndex;

            case "magic1Index":
                return magic1Index;

            case "magic2Index":
                return magic2Index;

            default:
                return null;
        }
    }


    // =================================================
    // DB Value → Property
    // =================================================

    public override void SetValue(string columnName, object value)
    {
        switch (columnName)
        {
            case "nickname":
                nickname = value?.ToString();
                break;


            case "hatIndex":
                hatIndex = Convert.ToInt32(value);
                break;


            case "broomIndex":
                broomIndex = Convert.ToInt32(value);
                break;


            case "magic1Index":
                magic1Index = Convert.ToInt32(value);
                break;


            case "magic2Index":
                magic2Index = Convert.ToInt32(value);
                break;
        }
    }
}



// =====================================================
// DatabaseManager
// =====================================================

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance;

    public static Client DBClient;


    [Header("BACKND Database UUID")]
    [SerializeField]
    private string databaseUUID;


    private bool isInitialized;


    // 현재 로그인한 유저의 데이터
    private PlayerSetting currentPlayerSetting;



    // =====================================================
    // Singleton
    // =====================================================

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



    // =====================================================
    // Database 초기화
    //
    // BACKND 로그인 성공 후 호출
    // =====================================================

    public async void InitializeDatabase()
    {
        if (isInitialized)
        {
            await LoadPlayerSetting();
            return;
        }


        if (string.IsNullOrWhiteSpace(databaseUUID))
        {
            Debug.LogError(
                "BACKND Database UUID가 비어있습니다."
            );

            return;
        }


        try
        {
            DBClient = new Client(databaseUUID);


            await DBClient.Initialize();


            isInitialized = true;


            Debug.Log(
                "BACKND Database 초기화 완료"
            );


            // 로그인한 유저의 데이터 불러오기
            await LoadPlayerSetting();
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"Database 초기화 실패 : {e}"
            );
        }
    }



    // =====================================================
    // PlayerSetting 불러오기
    // =====================================================

    public async Task LoadPlayerSetting()
    {
        if (!isInitialized)
        {
            Debug.LogError(
                "Database가 초기화되지 않았습니다."
            );

            return;
        }


        try
        {
            PlayerSetting setting =
                await LoadSetting();


            // =============================================
            // 데이터가 없는 경우
            // =============================================

            if (setting == null)
            {
                currentPlayerSetting = null;


                Debug.Log(
                    "LoginPlayerData가 없습니다. 신규 유저입니다."
                );


                // 여기서 닉네임 입력 UI 호출 가능
                //
                // UIManager.Instance.OpenNicknamePanel();


                return;
            }



            // =============================================
            // 데이터가 존재하는 경우
            // =============================================

            currentPlayerSetting = setting;


            // DB → DataConfig

            DataConfig.hatIndex =
                setting.hatIndex;

            DataConfig.broomIndex =
                setting.broomIndex;

            DataConfig.magic1Index =
                setting.magic1Index;

            DataConfig.magic2Index =
                setting.magic2Index;



            Debug.Log(
                "PlayerSetting 불러오기 완료\n" +

                $"닉네임 : {setting.nickname}\n" +

                $"모자 : {setting.hatIndex}\n" +

                $"빗자루 : {setting.broomIndex}\n" +

                $"마법1 : {setting.magic1Index}\n" +

                $"마법2 : {setting.magic2Index}"
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"PlayerSetting 불러오기 실패 : {e}"
            );
        }
    }



    // =====================================================
    // 신규 플레이어 등록
    // =====================================================

    public async void RegisterPlayerSetting(
        string nickname
    )
    {
        if (!isInitialized)
        {
            Debug.LogError(
                "Database가 초기화되지 않았습니다."
            );

            return;
        }


        if (string.IsNullOrWhiteSpace(nickname))
        {
            Debug.LogError(
                "닉네임이 비어있습니다."
            );

            return;
        }


        try
        {
            PlayerSetting setting =
                CreateCurrentSetting(nickname);


            await DBClient
                .From<PlayerSetting>()
                .Insert(setting);


            currentPlayerSetting =
                setting;


            Debug.Log(
                "LoginPlayerData 신규 등록 완료"
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"PlayerSetting 등록 실패 : {e}"
            );
        }
    }



    // =====================================================
    // 현재 설정 저장
    //
    // 데이터가 없으면 Insert
    // 데이터가 있으면 현재 유저 데이터 Update
    // =====================================================

    public async void SaveCurrentSetting(
        string nickname
    )
    {
        if (!isInitialized)
        {
            Debug.LogError(
                "Database가 초기화되지 않았습니다."
            );

            return;
        }


        try
        {
            PlayerSetting setting =
                CreateCurrentSetting(nickname);


            // =============================================
            // 기존 데이터 없음
            // =============================================

            if (currentPlayerSetting == null)
            {
                await DBClient
                    .From<PlayerSetting>()
                    .Insert(setting);


                currentPlayerSetting =
                    setting;


                Debug.Log(
                    "LoginPlayerData 신규 등록 완료"
                );


                return;
            }



            // =============================================
            // 기존 데이터 있음
            //
            // 현재 로그인 유저 기준으로 수정
            // =============================================

            await DBClient
                .From<PlayerSetting>()
                .OfCurrentUser()
                .Set(x => x.nickname, setting.nickname)
                .Set(x => x.hatIndex, setting.hatIndex)
                .Set(x => x.broomIndex, setting.broomIndex)
                .Set(x => x.magic1Index, setting.magic1Index)
                .Set(x => x.magic2Index, setting.magic2Index)
                .Update();


            currentPlayerSetting =
                setting;


            Debug.Log(
                "LoginPlayerData 수정 완료"
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"PlayerSetting 저장 실패 : {e}"
            );
        }
    }



    // =====================================================
    // DB에서 현재 유저 데이터 조회
    // =====================================================

    private async Task<PlayerSetting> LoadSetting()
    {
        PlayerSetting setting =
            await DBClient
                .From<PlayerSetting>()
                .OfCurrentUser()
                .FirstOrDefault();


        return setting;
    }



    // =====================================================
    // 현재 DataConfig를 PlayerSetting으로 변환
    // =====================================================

    private PlayerSetting CreateCurrentSetting(
        string nickname
    )
    {
        return new PlayerSetting
        {
            nickname = nickname,

            hatIndex =
                DataConfig.hatIndex,

            broomIndex =
                DataConfig.broomIndex,

            magic1Index =
                DataConfig.magic1Index,

            magic2Index =
                DataConfig.magic2Index
        };
    }



    // =====================================================
    // 현재 닉네임 가져오기
    // =====================================================

    public string GetNickname()
    {
        if (currentPlayerSetting == null)
            return string.Empty;


        return currentPlayerSetting.nickname;
    }



    // =====================================================
    // 현재 PlayerSetting 가져오기
    // =====================================================

    public PlayerSetting GetPlayerSetting()
    {
        return currentPlayerSetting;
    }



    // =====================================================
    // 초기화 여부
    // =====================================================

    public bool IsInitialized()
    {
        return isInitialized;
    }
}