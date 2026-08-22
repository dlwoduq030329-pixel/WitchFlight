using System;
using System.Threading.Tasks;
using UnityEngine;
using BACKND.Database;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance;

    public static Client DBClient;

    public bool IsInitialized { get; private set; }


    [Header("BACKND Database UUID")]
    [SerializeField]
    private string databaseUUID = "01a0294f-5c97-7f31-8c1b-92cd14cbfe3a";


    private const string SETTING_DOCUMENT = "PlayerSetting";


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


    // 로그인 성공 후 LoginManager에서 호출
    public async void InitializeDatabase()
    {
        if (IsInitialized)
            return;

        try
        {
            DBClient = new Client(databaseUUID);

            await DBClient.Initialize();

            IsInitialized = true;

            Debug.Log("BACKND Database 초기화 완료");

            // DB 초기화가 끝나면
            // 현재 유저의 DataConfig를 불러옴
            await LoadPlayerSetting();
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"BACKND Database 초기화 실패 : {e.Message}"
            );
        }
    }


    // 현재 DataConfig 값을 저장
    public async void SavePlayerSetting()
    {
        if (!IsInitialized)
        {
            Debug.LogError(
                "Database가 초기화되지 않았습니다."
            );

            return;
        }

        try
        {
            PlayerSetting setting = new PlayerSetting
            {
                hatIndex = DataConfig.hatIndex,
                broomIndex = DataConfig.broomIndex,
                magic1Index = DataConfig.magic1Index,
                magic2Index = DataConfig.magic2Index
            };


            // TODO:
            // BACKND Database SDK의 실제 Document 저장 API 연결

            Debug.Log(
                "플레이어 세팅 저장 요청"
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"플레이어 세팅 저장 실패 : {e.Message}"
            );
        }
    }


    // 로그인 후 DB에서 현재 유저 세팅 불러오기
    public async Task LoadPlayerSetting()
    {
        if (!IsInitialized)
        {
            Debug.LogError(
                "Database가 초기화되지 않았습니다."
            );

            return;
        }

        try
        {
            // TODO:
            // BACKND Database SDK의 실제 Document 조회 API 연결


            // DB에 저장된 데이터가 있다고 가정했을 때
            //
            // DataConfig.hatIndex = setting.hatIndex;
            // DataConfig.broomIndex = setting.broomIndex;
            // DataConfig.magic1Index = setting.magic1Index;
            // DataConfig.magic2Index = setting.magic2Index;


            Debug.Log(
                "플레이어 세팅 불러오기 완료"
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"플레이어 세팅 불러오기 실패 : {e.Message}"
            );
        }
    }


    // 신규 유저용 기본 세팅 생성
    public void CreateDefaultSetting()
    {
        DataConfig.hatIndex = 0;
        DataConfig.broomIndex = 0;
        DataConfig.magic1Index = 0;
        DataConfig.magic2Index = 0;

        SavePlayerSetting();
    }


    // 장비 변경 후 호출
    public void SaveCurrentDataConfig()
    {
        SavePlayerSetting();
    }
}


// Database에 저장할 플레이어 세팅 데이터
[Serializable]
public class PlayerSetting
{
    public int hatIndex;

    public int broomIndex;

    public int magic1Index;

    public int magic2Index;
}