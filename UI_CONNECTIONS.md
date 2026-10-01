# 사용자 UI 연결 안내

기본 Canvas/패널/텍스트와 OnGUI UI는 더 이상 자동으로 생성하지 않습니다. 비어 있는 참조는 표시하지 않으며 게임 진행에는 영향을 주지 않습니다. 기존 얼굴 RawImage/Intro Panel 연결과 사용자 제작 씬 오브젝트는 유지했습니다.

## 공통

- UI는 사용자가 만든 Canvas 아래 배치합니다. 버튼/드롭다운에는 씬의 EventSystem과 Canvas의 GraphicRaycaster가 필요합니다.
- UI 제어 스크립트는 항상 활성화된 관리 오브젝트에 둡니다. 스크립트가 들어 있는 오브젝트나 그 부모를 숨김 대상 Panel/Root 필드에 지정하지 마세요.
- 텍스트는 TextMeshProUGUI(TMP_Text), 이미지 게이지는 Image Type을 Filled로 설정합니다. Slider 게이지는 Interactable을 끄고 Whole Numbers를 끄세요.
- 자동 생성 UI를 보여주던 Play 세션은 종료한 뒤 다시 실행합니다. 씬에 이미 배치한 사용자 UI는 삭제하지 않습니다.

## 준비 화면: Battle / battlemanager / BattleIntroPresentation

| 필드 | 연결 대상 |
|---|---|
| Local Portrait | 내 얼굴용 RawImage |
| Opponent Portrait | 적 얼굴용 RawImage |
| Intro Panel | 직접 만든 VS 연출 패널 |
| Countdown Text | 3·2·1용 TMP 텍스트 |
| Waiting Text | 기존 참조 호환용. 접속 대기 문구는 표시하지 않음 |
| Solo Text | 혼자 테스트할 때 표시할 TMP 텍스트(문구는 직접 작성) |

RawImage의 Texture는 비워둡니다. 준비된 캐릭터로 만든 투명 RenderTexture가 런타임에 자동 연결됩니다. Canvas/VS 문구/라벨은 새로 만들지 않습니다. Countdown 텍스트는 Intro Panel 밖에 두세요. Intro Panel은 배틀 입장 직후(다른 클라이언트 준비 대기 중 포함)부터 켜지며, 얼굴은 촬영 준비가 되면 표시됩니다. `On Intro Started/Ended`에 사용자 애니메이션을 연결하고, BattleManager의 `Intro Duration Seconds`와 길이를 맞춥니다. On Intro Started는 패널이 켜질 때가 아니라 양쪽 준비 완료 후 실제 연출 단계에 진입할 때 호출됩니다. UI를 연결하지 않아도 얼굴 촬영 준비와 네트워크 준비 보고는 계속 수행하므로 시작이 UI 누락 때문에 멈추지 않습니다.

### IntroPanel의 linkuserinfo

Battle 씬의 현재 IntroPanel 인스턴스에 추가했습니다. 원본 UI 프리팹은 수정하지 않았습니다. 별도의 패널로 교체하면 `linkuserinfo`를 직접 추가하세요. 텍스트/아이콘/UI는 생성하거나 자동 연결하지 않습니다.

- `LocalPlayerData`: 이 클라이언트의 내 PlayerData. 호스트 여부가 아니라 InputAuthority로 구분합니다.
- `OpponentPlayerData`: 내 팀과 다른 첫 번째 참가자의 PlayerData. 솔로 테스트 중에는 null입니다.
- `AllPlayerDatas`: 현재 초기화 완료된 전체 참가자 목록. 인원이 늘어나면 이 목록을 사용하세요.
- `GetPlayerData(PlayerRef)`: 특정 참가자 조회.
- `RefreshUserInfo()`: 즉시 다시 읽기. 기본적으로 패널 활성 중 0.2초마다 자동 확인합니다.
- Inspector의 `On Local User Info Changed` / `On Opponent User Info Changed`: 사용자 스크립트의 `public void ShowUser(PlayerData data)`를 Dynamic PlayerData로 연결합니다. 최초 조회/값 변경/연결 해제 때 호출되며 데이터가 없으면 null이 전달됩니다. UI를 지우거나 빈 상태로 표시하세요.
- `On Users Changed` 또는 C# `UsersChanged`: 전체 정보 변경 알림. 이 콜백에서 위 속성을 조회해 사용자 UI를 갱신할 수 있습니다. 연결을 나중에 했다면 `RefreshUserInfo()`로 최초 갱신하세요.

```csharp
public void ShowUser(PlayerData data)
{
    if (data == null) return; // 여기서 사용자 UI를 비우거나 숨기세요.
    // data.hat, data.broom, data.magic1, data.magic2
    // data.teamIndex, data.camp, data.ready
    // data.hairLength, data.hairColor, data.clothColor
    // data.BattleCurrentHp, data.BattleMaxHp
    // data.BattleCurrentAp, data.BattleMaxAp, data.BattleApRecoveryPerSecond
}
```

데이터가 아직 도착하지 않았으면 null 상태로 기다렸다가 자동 연결합니다. 패널 비활성화 시 조회를 중단하고 참조를 비우며 다시 켜면 새로 읽습니다. 기존 Fusion PlayerData의 복제 값을 읽는 UI 연결 코드이며 새로운 RPC/네트워크 데이터는 만들지 않습니다. 현재 PlayerData에는 닉네임 필드가 없고, hairColor/clothColor의 값 전송은 기존 시스템에서 처리해야 합니다. 이 컴포넌트는 해당 필드의 수신 값만 읽습니다.

## 전투 HUD: Battle / battlemanager / BattleHud

| 그룹 | 필드 / 대상 |
|---|---|
| 전체 및 상태 패널 | Hud Root, Player Panel, Respawn Panel, Result Panel |
| 메뉴 | Menu Panel, Resume Button. Esc는 Menu Panel을 연결한 경우에만 메뉴를 엽니다. 네트워크 경기는 정지하지 않습니다. |
| 체력 | Hp Grid Bar에 사용자 GridHPBar 연결. Hp Text는 선택 사항. Hp Slider/Hp Fill은 호환용이며 비워도 됨 |
| 마나 | Ap Slider 또는 Ap Fill, Ap Text |
| 비행/장비 | Team Text, Speed Stage Text, Speed Text, Selected Magic Text, Magic Cost Text |
| 경기 | Match Time Text, Flag Text, Result Text, Respawn Text |
| 조준 | Desired Aim Marker(큰 원), Forward Aim Marker(작은 원), Aim Alignment Text(선택), Lock Marker(RectTransform), Lock Progress Fill, Lock Text. Reticle은 큰 원 미연결 시 기존 대체 UI |
| 카메라 | World Camera(현재 Battle Main Camera 연결됨) |
| 피해 숫자 | Damage Labels 배열에 미리 배치한 TMP 텍스트들을 연결. 프리팹이 아니라 씬 인스턴스입니다. |

피해 숫자는 연결된 슬롯만 순환 재사용합니다. 이동·축소·투명도 애니메이션이 적용되며 원래 색상/크기는 복원됩니다. 배열이 비면 피해 숫자는 표시하지 않습니다. Lock Marker와 피해 숫자의 위치는 Canvas 좌표로 변환합니다. Screen Space Camera/World Space Canvas를 쓰면 Canvas의 World Camera도 연결하세요. 피해 숫자는 활성화되어 있는 Canvas 아래 별도 부모에 두는 것이 편합니다.

Resume Button은 코드에서 리스너를 연결하므로 OnClick에 Resume을 중복 등록할 필요는 없습니다. 별도 버튼은 `BattleHud.ToggleMenu()` 또는 `Resume()`에 연결할 수 있습니다. 조작 안내 문구/레이아웃/장식은 사용자 UI에 직접 작성합니다.

### 비행 조준 원 연결

1. 전투 Canvas 아래에 큰 원/작은 원 Image를 직접 만들고 서로 독립된 형제 오브젝트로 배치합니다. Pivot은 (0.5, 0.5), Raycast Target은 끄고 Layout Group의 자동 배치 대상에서 제외하세요.
2. BattleHud의 `Desired Aim Marker`에 큰 원 RectTransform, `Forward Aim Marker`에 작은 원 RectTransform을 연결합니다. 같은 오브젝트나 서로의 부모/자식을 지정하지 마세요.
3. 큰 원은 화면 중앙의 목표 시점, 작은 원은 캐릭터 정면을 선택 마법 사거리까지 투영한 표시입니다. 작은 원이 화면 밖이면 숨깁니다. 벽과 충돌하는 실제 탄착점이나 이동 표적의 예측 조준점은 아닙니다.
4. 선택 사항인 `Aim Alignment Text`에는 TMP 텍스트를 연결합니다. 두 방향의 각도가 `ChPrefab → Player → Lock Aim Alignment Tolerance`(기본 1.5도) 이하면 AIM ALIGNED, 아니면 ALIGNING입니다. 정렬 시 두 원 Image의 RGB가 초록색으로 바뀌며 기존 알파는 유지합니다. 정렬이 풀리거나 HUD가 숨겨지면 원래 색으로 복원합니다. `Desired Aim Image`와 `Forward Aim Image`는 비워두면 각 Marker 자체의 Image를 사용하며, Image가 자식에 있으면 직접 연결하세요.
   록온 판정은 `Lock Aim Alignment Tolerance + Lock Aim Offset`을 사용합니다. Offset은 Player의 SerializeField이며 기본 3도, 합계 기본 4.5도입니다. 초록색 판정은 Offset을 더하지 않으므로 흰색이어도 록온 가능한 구간이 있습니다. Offset=0이면 이전 판정과 같습니다. 이 값은 UI 픽셀 반경이 아닌 각도이므로 큰 원 크기/FOV에 맞게 조절하세요. 비록온 마법의 발사는 제한하지 않습니다.
5. Main Camera의 CameraFollow에서 `Enable Mouse Aim Steering`을 켭니다(기본 켜짐). X/Y 감도는 `Mouse Yaw Sensitivity`/`Mouse Pitch Aim Sensitivity`로, 캐릭터 추적 선회 속도는 기존 빗자루 테이블의 turnSpeed로 조절합니다. Player에 새 컴포넌트를 붙일 필요는 없습니다.

큰 원을 연결하면 기존 Reticle은 숨깁니다. 기존 록온 표시는 별도로 유지됩니다. 원 참조가 비어 있어도 비행과 발사는 동작하며 UI는 생성하지 않습니다. 카메라/네트워크 렌더링 후 Canvas 렌더링 직전에 원 위치를 갱신합니다.

록온 도중 두 원이 추가 허용각까지 벗어나거나 대상이 화면 밖/벽 뒤/거리 밖으로 나가면 대상과 충전이 초기화됩니다. 다시 조건을 만족하면 처음부터 충전합니다. 적 자체가 두 원 중앙에 있어야 하는 것은 아니며 카메라 화면 안이면 기존 중앙 우선 선택 규칙을 따릅니다. 발사된 유도탄의 추적 규칙은 변경하지 않습니다. 서버는 수신된 목표 방향과 실제 기체 방향을 비교하고 거리/시야 차단을 재검사하며, 화면 포함 여부는 해당 클라이언트 카메라에서 판정합니다.

테스트: 시점을 빠르게 옮긴 직후 Vision이 작은 원으로 발사되는지, 시점 입력을 멈추면 두 원이 일치하는지, 시전 지연 마법이 실제 발사 순간의 정면으로 나가는지 확인하세요. Fire/Ice 록온, 메뉴, 리스폰, 맵 복귀도 함께 확인합니다. NetworkInputData가 변경되었으므로 에디터와 클라이언트 빌드를 모두 최신 코드로 실행해야 합니다. 실제 Play/두 클라이언트 검증은 별도로 필요합니다.

## 로비 장비 UI: Main / NetworkGameManager가 있는 오브젝트 / LobbyLoadoutMenu

### 코드 대전 / 랜덤 1대1 매칭 버튼

Main의 기존 NetworkGameManager를 각 버튼 OnClick에 연결합니다. 버튼/Canvas는 자동 생성하지 않습니다.

| 사용자 버튼 | 연결 함수 |
|---|---|
| 방 만들기 | NetworkGameManager.CreateRoom() — 입력한 코드로 Host 시작 |
| 방 접속 | NetworkGameManager.JoinRoom() — 입력한 기존 코드 방에 Client 참가, 방이 없으면 생성하지 않고 실패 |
| 랜덤 1대1 대전 | NetworkGameManager.StartRandomMatch() |
| 코드 방 게임 시작 (방장 전용) | NetworkGameManager.StartRoomGame() |
| 매칭 대기 취소 | NetworkGameManager.CancelMatch() |

방 만들기와 방 접속은 NetworkGameManager의 `Room Id Input`에 연결한 TMP_InputField를 공유합니다. 공백/미연결 상태에서는 시작하지 않습니다. 생성한 코드와 같은 코드를 상대가 입력한 뒤 방 접속을 누르면 됩니다. 기존 `StartMatch()`는 코드 방 자동 생성/참가 호환용으로 남겨두었으며, 새로 만든 세 버튼에는 위 함수를 각각 연결하세요. 함수 하나만 등록하고 기존 StartMatch 리스너와 중복 등록하지 마세요.

- 선택 사항: NetworkGameManager의 `Matchmaking Status Text`에 TMP 텍스트를 연결하면 연결 중/대기 인원/실패/취소 상태를 표시합니다. 비워도 동작합니다.
- 사용자 UI 코드에서 `IsMatching`으로 매칭 버튼/장비 수정 잠금, `CanCancelMatch`로 취소 버튼 활성 여부를 결정할 수 있습니다. `IsRoomHost`는 방장 여부, `CanStartRoomGame`은 코드 방 시작 가능 여부입니다. 시작 버튼의 interactable에 CanStartRoomGame을 사용하면 됩니다. 연결하지 않아도 함수 자체가 권한/준비를 재검사합니다. `MatchStatus`, `IsRandomMatch`, `Runner`도 조회할 수 있습니다.
- 랜덤 대전은 코드 입력을 읽지 않으며 항상 2명입니다. 코드 대전은 기존 Max Player Count를 사용하므로 혼자 테스트할 때 1을 유지할 수 있습니다. 랜덤은 Max Player Count=1이어도 혼자 시작하지 않습니다.
- 랜덤 방은 `WitchFlight-Random-1v1-v1` 그룹과 queue/players/battle 속성으로 검색합니다. 기존 공개 대기 방이 없으면 Fusion이 임의 이름으로 방을 생성합니다. 코드 방은 기본 그룹의 비공개 방으로 생성하여 랜덤 대상에서 제외합니다. 비공개는 목록 노출 제한이지 코드/비밀번호 인증 기능이 아닙니다.
- 랜덤: 두 명의 PlayerData/장비 정보 준비 완료 → 양쪽 로비에서 상대 프로필 표시 → 각자 1초 표시 완료 확인을 호스트에 전달 → 자동 Battle 전환. 코드 방(CreateRoom/JoinRoom 및 호환 StartMatch): 자신의 PlayerData가 준비되는 즉시 자기 프로필 표시, 상대 접속/데이터 준비 후 상대 프로필 표시 → 로비 대기 → 방장의 StartRoomGame 클릭 → Battle 전환. 준비되지 않은 상태에서 미리 누른 시작 요청을 예약하지 않습니다.
- 씬 전환 직전에 입장/목록 노출을 닫고 기존 캐릭터/초상화/카운트다운 흐름을 사용합니다. PlayerData/Player/PlayerEquipment는 새로 대체하지 않았습니다. 로비의 NetworkGameManager.StartRoomGame과 Battle 안의 BattleManager.startGame(연출 시작)은 서로 다른 함수입니다. Max Player Count=1인 코드 방도 버튼을 눌러야 시작합니다.
- 취소는 연결 중 또는 로비 대기 중에만 가능합니다. Battle 전환 시작 후에는 대전 나가기 기능으로 쓰지 않습니다. 취소/실패 시 이전 Runner와 SceneManager를 정리하며, 완료될 때까지 연속 매칭 요청을 막고 다음 요청에서 새 Runner를 생성합니다.
- 대기 중 클라이언트가 나가면 호스트는 계속 기다립니다. 호스트가 종료되면 연결 종료 상태를 표시하며 남은 사용자는 로비에서 다시 매칭해야 합니다. 자동 재매칭/호스트 이전/실력 점수 매칭은 이번 범위에 포함하지 않습니다.
- Runner는 지속되는 NetworkGameManager의 전용 세션 자식 오브젝트에 생성됩니다. linkuserinfo는 NetworkGameManager.Runner를 조회하도록 수정했습니다. 추가 사용자 코드도 GetComponent<NetworkRunner>() 대신 이 속성을 사용하세요.

테스트는 같은 Photon App ID/앱 버전/지역의 최신 에디터와 빌드로 진행하세요. 지역/버전이 다르면 서로 매칭되지 않을 수 있습니다. 기존 Photon 설정은 수정하지 않았습니다.

1. 첫 사용자 랜덤 시작 → 1/2 대기, 두 번째 랜덤 시작 → 2인 Battle 진입 및 서로 다른 진영/외형/초상화 확인.
2. 코드 대전 대기자와 랜덤 대기자가 서로 섞이지 않는지 확인. 사용자 A의 CreateRoom 후 같은 코드로 사용자 B의 JoinRoom을 호출하면 만나야 합니다. 없는 코드 참가가 방을 생성하지 않는지, 중복 코드 생성/가득 찬 방/닫힌 방의 실패 메시지, 공백 코드 거부도 확인하세요.
3. 랜덤/코드 버튼 연타, 연결 중 즉시 취소, 방 대기 취소 후 재시도, 연결 실패 후 재시도 시 Runner/PlayerData가 중복되지 않는지 확인.
4. 대기자 이탈 및 새 상대 입장, 호스트 종료, 3명 이상 동시 랜덤 요청 시 한 방의 정원이 2명을 넘지 않는지 확인.
5. 코드 방 2명 접속 후 자동 전환하지 않는지, 방장 버튼으로만 전환되는지 확인. 인원 부족/PlayerData 준비 전/비방장/연속 클릭 거부, 시작 전 이탈 시 버튼 비활성화, 랜덤 자동 시작 유지, 씬 전환 후 취소 불가 및 기존 준비 화면/3·2·1 확인.

설치된 Fusion API로 C# 컴파일을 확인했습니다. 실제 Photon 접속/동시 참가/취소 타이밍은 두 클라이언트 이상의 Play 테스트가 필요합니다.
참고: [Photon Fusion 공식 매칭 문서](https://doc.photonengine.com/fusion/v2/manual/connection-and-matchmaking/matchmaking).

### 프로필 번호 PlayerData.playerprofile

- 기본값은 0인 Networked int입니다. linkuserinfo의 Sprite 배열로 이미지에 매핑합니다. 파일 생성/백엔드 저장은 추가하지 않았습니다. 음수는 0으로 보정하며 번호의 임의 상한은 두지 않았습니다.
- 접속 전 `DataConfig.playerprofile = 원하는번호;`로 설정하면 기존 장비 RPC를 통해 자기 PlayerData에 전달되고 상대에게 복제됩니다.
- 접속 후 본인의 PlayerData에서 `SetPlayerProfile(원하는번호)`를 호출하세요. InputAuthority만 요청할 수 있고 호스트가 반영합니다. 상대 PlayerData의 값을 직접 변경하지 마세요. DataConfig만 바꾸는 것은 이미 생성된 PlayerData를 갱신하지 않습니다.
- 읽기는 `data.playerprofile`입니다. linkuserinfo의 LocalPlayerData/OpponentPlayerData로 접근할 수 있으며, 번호가 바뀌면 기존 On Local/Opponent User Info Changed 알림과 연결한 Image에도 반영됩니다.
- 테스트: 서로 다른 번호(예: 1/2)로 접속 후 양쪽에서 같은 값이 보이는지, 본인 번호 변경이 상대에 반영되는지, Battle 이동/리스폰 후 유지되는지 확인하세요. 프로필은 앱 재시작 후 저장되지 않습니다.
- PlayerData의 네트워크 레이아웃과 장비 RPC 인수가 바뀌었으므로 에디터/테스트 빌드를 모두 갱신해야 합니다. C# 컴파일과 별도로 Unity의 Fusion 코드 생성/임포트 및 2클라이언트 검증이 필요합니다.

### 코드 방 Waiting Room UI (모든 참가자 화면)

UI 오브젝트 자체를 네트워크로 복제하지 않습니다. 기존 Fusion의 ActivePlayers/PlayerData를 각 클라이언트의 linkuserinfo가 읽어 같은 참가자 목록과 프로필을 그립니다. 기본 갱신 간격은 0.2초이며 네트워크 도착 지연은 별도로 존재합니다. 호스트 전용 딕셔너리에 의존하지 않습니다.

Main의 항상 켜져 있는 UI 컨트롤러 오브젝트에 `linkuserinfo`를 두고 아래 참조를 연결하세요. **Waiting Room Panel 안이나 그 자식에 컨트롤러를 두지 마세요.** 비활성화된 패널 안에 있으면 접속 후 스스로 열 수 없습니다. NetworkGameManager도 이 패널 밖에 둡니다. 같은 UI를 여러 linkuserinfo가 동시에 수정하지 않도록 한 곳에서 관리하세요.

- `Waiting Room Panel`: 기존 대기실 패널. 코드 방 생성/입장 성공 → 본인 및 방장의 PlayerData 준비 → 제목/슬롯 초기화 → 패널 표시 순서입니다. Main 씬 안에서 UI만 열리며 씬 이동은 없습니다. 취소/연결 종료/Battle 전환 시 숨겨집니다. 실패한 입장에는 열리지 않고 랜덤에서는 사용하지 않습니다.
- `Waiting Room Slots`: 기존 참가자 슬롯을 정원만큼 연결합니다. 슬롯/UI는 자동 생성하지 않습니다. 배열 순서와 Sprite 목록은 모든 클라이언트에서 같아야 합니다.
  - `Occupied Root`: 참가자가 있을 때 보일 슬롯 내용 영역.
  - `Empty Root`: 빈 자리 표시 영역. Occupied Root와 별도 형제 오브젝트로 둡니다.
  - `Profile Image`: 해당 참가자의 프로필 UI Image. 기존 Profile Sprites/Fallback Profile Sprite 목록을 공유합니다.
  - `Player Label`: 선택 사항. PlayerData.playerName을 표시합니다. PlayerData가 도착하기 전에는 '정보 받는 중...'을 표시합니다.
  - `Team Label`: 선택 사항. 동기화된 teamIndex를 표시합니다.
  - `Local Player Marker`: 선택 사항. 그 화면의 본인에게만 켜지는 '나' 표시입니다.
- `Waiting Room Code Text`, `Waiting Room Count Text`: 방 코드와 현재 접속 인원/서버 정원 표시.
- `roomkeepername`: 방장 이름만 표시합니다 (예: `이재엽`). 이전 Waiting Room Title Text 연결은 FormerlySerializedAs로 유지합니다.
- `startButton` (기존 Waiting Room Start Button): 시작 버튼 오브젝트 자체를 방장에게만 표시합니다. 방장도 기존 인원/PlayerData 준비 조건을 만족해야 interactable=true입니다. OnClick은 **NetworkGameManager.StartRoomGame()을 직접 연결**하세요. 클릭 리스너는 자동으로 추가하지 않습니다. 이 버튼 오브젝트 안에도 linkuserinfo/NetworkGameManager를 넣지 마세요. 함수를 직접 호출해도 정원이 차지 않으면 실패하고 현재 인원/정원을 MatchStatus에 표시합니다. 정원은 기존 Max Player Count이며 1이면 혼자 시작할 수 있으므로 2인 방은 2로 설정하세요.

방 만들기/접속 선택 버튼은 사용자가 만든 룸 번호 입력 UI를 열도록 연결하고, **번호 입력 후 확인 버튼**에서 CreateRoom()/JoinRoom()을 호출하세요. NetworkGameManager.Room Id Input을 그 입력창에 연결합니다. 접속 버튼에서 Waiting Room Panel.SetActive(true)를 별도로 호출하지 마세요. 성공 및 데이터 준비 여부를 linkuserinfo가 판정합니다.

이름은 접속 시 DataConfig.playerName → 비어 있으면 기존 DatabaseManager.GetNickname() → 그래도 없으면 `Player 번호` 순서로 결정하여 PlayerData.playerName(NetworkString 32)에 전달합니다. 방장 여부(IsRoomOwner)는 호스트가 설정하며 참가자가 RPC로 지정하지 않습니다. 이름은 이번 접속 동안 유지되고 백엔드 저장 구조는 변경하지 않습니다. 새 네트워크 필드와 RPC 인수 때문에 양쪽 빌드를 갱신해야 합니다.

대기실 슬롯은 방장을 항상 첫 번째로 두고 나머지는 PlayerRef 순으로 모든 화면에 동일하게 정렬합니다(내가 항상 첫 번째인 배열이 아님). 데이터 도착 전에도 자리를 예약하고, 데이터가 도착하면 프로필/팀을 채웁니다. 입장/퇴장/프로필 변경 후 다시 그리며, 퇴장 시 남은 인원으로 목록을 정렬하고 빈 자리를 초기화합니다. 네트워크 전파 전까지 순간적인 표시 차이는 가능합니다. 기존 Local/Opponent Profile Image는 각 화면 기준 내/상대 표시로 그대로 유지됩니다. 이 참조들과 슬롯의 Profile Image에 동일한 Image를 중복 연결하지 마세요.

#### 1대1 로비의 다섯 필드 (권장 연결)

| Inspector 필드 | 타입 | 표시 내용 |
|---|---|---|
| roomkeepername | TMP_Text | 방장 이름만 표시 |
| Aslotname | TMP_Text | 방장 이름 (모든 화면에서 A 고정) |
| Bslotname | TMP_Text | 이후 입장한 참가자 이름 |
| AslotProfile | UI Image | 방장의 profileimage에 해당하는 Sprite |
| BslotProfile | UI Image | 참가자의 profileimage에 해당하는 Sprite |
| startButton | UI Button | 방장에게만 표시, 정원 및 PlayerData 준비 후 클릭 가능 |

이 다섯 필드를 연결하면 기존 Waiting Room Slots 배열을 새로 채울 필요가 없습니다. 기존 배열은 연결 보존/여러 명 표시를 위해 남겨두었습니다. B가 비어 있으면 이름을 비우고 이미지를 숨기며, 접속했지만 데이터 수신 중이면 이름에 `정보 받는 중...`을 표시합니다. B가 퇴장하면 같은 방식으로 비웁니다. 세 명 이상이라면 이 다섯 필드에는 방장과 첫 참가자만 표시하고 전체 명단은 기존 배열을 사용하세요.

PlayerData.profileimage는 기존 Networked playerprofile의 읽기 별칭입니다. 두 이름은 같은 값이며 기본값은 0입니다. Profile Sprites의 Element 0에 기본 프로필을 넣으세요. 기존 접속 전 DataConfig.playerprofile 지정/접속 후 SetPlayerProfile(int) 변경 방법은 그대로 유지되며, 명시적으로 선택한 다른 번호를 매번 0으로 덮어쓰지 않습니다. 프로필 번호 변경 시 A/B 이미지도 갱신됩니다.

검증: A가 코드 방 생성 → A만 표시, B가 같은 코드 입장 → 양쪽에 같은 두 참가자/프로필/팀/인원 표시, B의 '나' 표시는 B 슬롯에만 표시, B 프로필 변경 → 양쪽 이미지 갱신, B 퇴장 → A 화면에서 빈 자리 및 시작 버튼 비활성화, 다시 입장 → 정상 갱신, 호스트 종료/없는 방 입장 실패 → 대기실 숨김을 확인하세요. 랜덤의 상대 표시 후 1초 및 Battle Intro 흐름도 회귀 테스트하세요.

### 로비 프로필 UI와 랜덤 매칭 1초 대기 연결 (필수)

Main의 매칭/코드 방 UI에 활성 상태인 `linkuserinfo` 컴포넌트를 하나 배치합니다. 기존 Battle Intro의 컴포넌트만으로는 Main의 프로필 표시를 처리할 수 없습니다. UI/씬은 자동으로 생성하거나 연결하지 않습니다.

- `Local Profile Image`: 자기 프로필용 UI Image.
- `Opponent Profile Image`: 상대 프로필용 UI Image. RawImage가 아닙니다.
- `Profile Sprites`: Element 0 = playerprofile 0, Element 1 = 1 순서의 Sprite 목록. 양쪽 클라이언트에 같은 목록을 사용하세요.
- `Fallback Profile Sprite`: 등록되지 않은 번호에 사용할 선택적 기본 이미지. 이것도 없으면 해당 Image를 숨깁니다.
- `Confirm Random Match Preview`: Main 로비의 컴포넌트 한 곳에서만 체크합니다. Battle Intro에서는 끕니다.

PlayerData 참조는 자동으로 찾습니다. 내 데이터만 있으면 내 이미지만 표시하며 상대가 들어오면 적 진영 데이터를 받아 상대 이미지가 표시됩니다. 랜덤/코드 방 모두 같은 연결을 사용합니다. 코드 방에서는 표시 확인이나 1초 타이머가 시작 버튼의 추가 조건이 되지 않습니다.

랜덤에서는 상대 Image가 활성 상태이고 Sprite가 적용되어 있으며 Canvas/CanvasGroup이 숨겨져 있지 않은 동안 1초를 셉니다(Time.timeScale과 무관). 양쪽 모두 표시를 확인해야 호스트가 씬을 바꿉니다. 패널 비활성화/프로필 변경/상대 교체 시 표시 타이머를 초기화합니다. **UI 미연결, 패널 비활성화, 이미지 미등록(기본 이미지도 없음), 확인 옵션 해제 시에는 랜덤 매칭이 시작되지 않습니다.** 실제 다른 UI에 가려지는지는 렌더 순서까지 자동 판별하지 않으므로 패널을 보이도록 구성하세요.

테스트: 서로 다른 프로필 번호로 ① 코드 방 생성 직후 내 이미지, 참가 후 양쪽 상대 이미지 ② 코드 방 자동 시작 없음 ③ 랜덤에서 늦게 접속한 쪽도 상대 표시 후 최소 1초 유지 ④ 1초 중 취소/상대 이탈/새 상대 접속 ⑤ 잘못된 번호의 기본 이미지 처리 ⑥ 프로필 변경 시 갱신 및 재대기를 확인하세요. 씬 진입 후 기존 배틀 인트로/카운트다운은 그대로입니다.

Loby UI Root(Inspector 표시: Lobby Ui Root), Loadout Panel, Toggle Button, Close Button을 연결합니다. 드롭다운의 Options는 사용자가 아래 순서대로 설정합니다. 컨트롤 이벤트는 참조를 넣으면 코드에서 자동 연결합니다.

- Hat Dropdown: Classic, Twisted, Elemental
- Broom Dropdown: Slow, Standard, Speed
- Magic 1/2 Dropdown: Fire, Ice, Vision, Thunder, Flare, Smoke, Wind, Decoy, Mine, Scan
- Hair Length Slider: Min 0, Max 10, Whole Numbers 켜기
- 선택 사항: Status Text, Hat Stats Text, Broom Stats Text, Magic 1/2 Stats Text, Hair Length Text

드롭다운 값은 0부터 시작하고 DataConfig에는 기존 enum ID(1부터)를 저장합니다. 패링은 기존 고정 3번 슬롯입니다. 매칭을 시작하면 변경 컨트롤을 잠급니다. UI가 비어 있으면 기존 DataConfig를 변경하지 않습니다. 데이터 저장 API/매칭 버튼은 기존 구조 그대로입니다. 직접 만든 버튼을 사용할 경우 OpenPanel/ClosePanel/TogglePanel 또는 SetHat/SetBroom/SetMagic1/SetMagic2/SetHairLength에 연결할 수 있습니다.

## 바람 UI: Battle / Main Camera / SpeedCameraEffects

1. 사용자 Canvas에 빈 UI 오브젝트(RectTransform)를 만들고 전체 화면 Stretch로 맞춥니다.
2. `UI / WitchFlight Speed Wind Lines` 컴포넌트를 추가합니다. Image/RawImage를 별도로 추가할 필요는 없습니다.
3. 이 컴포넌트를 Main Camera의 `SpeedCameraEffects → Wind Lines`에 드래그합니다.
4. Canvas는 Screen Space Overlay 또는 Main Camera를 지정한 Screen Space Camera로 설정합니다. HUD 뒤에 정렬하세요.

이 연결이 없으면 바람 선은 표시하지 않습니다. 모션블러/FOV는 연결 여부와 무관하게 기존대로 동작합니다. Show Wind Lines, Wind Opacity, Wind Min/Max Width 등은 계속 Main Camera에서 조절합니다. 사용자 Canvas/오브젝트는 코드가 삭제하지 않습니다.

## 최고속 주변부 추가 블러

PC_Renderer에 PeripheralSpeedBlurFeature를 연결했습니다. SpeedCameraEffects가 있는 게임용 Base 카메라에만 적용하며, Mobile_Renderer는 변경하지 않았습니다.

Battle Main Camera → SpeedCameraEffects → Top Speed Peripheral Blur에서 설정합니다.

- Enable Peripheral Blur: 추가 효과 켜기/끄기.
- Peripheral Blur Start Speed Ratio: 실제 전진 속도가 빗자루 기본 최고속도의 이 비율부터 효과가 강해집니다. 기본 0.75, 최고속도에서 최대이며 부스트도 최대 강도를 넘지 않습니다.
- Peripheral Blur Strength: 가장자리 추가 블러 길이. 기본 2, 최대 4.
- Use Speed Stage Inner Radius: 기본 켜짐. 속도 단계별 중앙 보호 반경을 사용합니다. 끄면 기존 고정 반경을 사용합니다.
- Inner Radius Stage 0/1/2: 기본 0.85 / 0.75 / 0.60. 0단계 값은 후진 선택 중에도 사용하지만 후진 블러를 새로 활성화하지는 않습니다.
- Inner Radius Stage 3 / Fixed: 기존 Peripheral Blur Inner Radius 저장값을 유지합니다(기본 0.45). 단계별 모드에서는 3단계 반경, 고정 모드에서는 모든 단계의 반경입니다. 작을수록 블러가 중앙까지 넓어집니다.
- Inner Radius Transition Speed: 단계 변경 시 반경 전환 반응성. 기본 5, 높을수록 빠릅니다. 준비/메뉴/리스폰 후에는 현재 단계 반경으로 초기화합니다.
- Use Boost Inner Radius: 기본 켜짐. 실제 IsBoosting 상태일 때 단계별/고정 반경보다 부스트 전용 반경을 우선합니다. 끄면 부스트 중에도 단계별/고정 반경을 사용합니다.
- Inner Radius Boost: 부스트 전용 반경, 기본 0.30. 부스트 시작/종료 모두 Inner Radius Transition Speed로 부드럽게 전환합니다. Shift 해제·마나 고갈·경직으로 부스트가 종료되면 현재 단계(또는 고정) 반경으로 돌아갑니다. 부스트 중 속도 단계를 바꿔도 부스트 반경이 우선합니다.
- Peripheral Blur Max Distance: 샘플 이동 거리 상한. 기본 0.08, 번짐 길이가 부족할 때 조절하세요.

기존 3단계 모션블러/FOV/바람은 유지되며 이번 추가 블러만 실제 속도에 따라 동작합니다. 모션벡터 방향으로 주변 오브젝트를 더 흐리게 하고, Keep Local Player Sharp로 모션벡터가 0인 캐릭터 픽셀은 제외합니다. 중앙은 이번 추가 효과에서 제외되는 것이며 기존 모션블러까지 끄지는 않습니다. 후처리 이전에 실행하여 고도 블랙아웃을 유지합니다. 추가 샘플 12개를 사용하므로 고해상도에서 GPU 비용을 확인하세요. Overlay UI는 처리하지 않지만 World Space UI는 장면의 일부로 처리될 수 있습니다.

반경은 선택한 속도 단계 기준이며 실제 부스트 중에는 부스트 반경이 우선합니다. 블러 강도와 시작 조건은 실제 전진 속도 기준으로 유지되므로 부스트만 켰다고 저속에서도 강제로 블러를 활성화하지 않습니다. 기본 시작 비율 0.75에서는 보통 1/2단계의 정상 속도만으로 블러가 켜지지 않습니다. 모든 전진 단계에서 반경 차이를 보고 싶으면 Start Speed Ratio를 예를 들어 0.2로 낮추세요. 3단계에서 2/1/0단계로 감속 중 아직 실제 속도가 높으면 반경 변화가 보일 수 있습니다.

확인 항목: 아직 느린 상태의 3단계/최고속/감속/부스트/후진을 비교하고, 캐릭터와 장비 선명도 및 메뉴/리스폰/고도 블랙아웃을 확인하세요. 단계별 반경, 고정 모드 전환, 반경의 부드러운 전환도 확인하세요. C# 컴파일은 통과했으나 Unity 셰이더 임포트와 실제 플레이 화면은 별도 확인이 필요합니다.

## 적 HP바

### 칸형 GridHPBar 연결

- 각 GridHPBar의 `Grid Parent`에는 HP 칸 Image들만 들어 있는 부모를 연결합니다. 배경/아이콘 Image는 그 밖에 두세요.
- 전투 중 내 체력: BattleHud의 `Hp Grid Bar`에 연결합니다. 기존 Player.NowHp / Player.MaxHp를 UpdateHP에 전달합니다. Slider는 필요 없습니다.
- 준비 화면 내 체력: linkuserinfo의 `On Local User Info Changed`에 내 GridHPBar를 넣고, **Dynamic PlayerData → UpdateFromPlayerData**를 선택합니다.
- 준비 화면 적 체력: `On Opponent User Info Changed`에 상대 GridHPBar의 같은 함수를 연결합니다.
- 적 머리 위 체력: 기존 hpfollow는 사용자 프리팹 안의 GridHPBar를 자동으로 찾아 UpdateHP를 호출합니다. 별도 Slider가 필요 없습니다.
- 연결 직후 수동 갱신이 필요하면 linkuserinfo.RefreshUserInfo를 호출합니다. 수신 전/연결 해제/null/최대 HP 0인 경우 칸을 비웁니다.
- Test_DecreaseHP는 표시 테스트 전용입니다. 실제 Player/PlayerData의 체력을 변경하지 않으며 실시간 바인딩 중에는 실제 HP 값으로 다시 갱신됩니다.
- 준비 화면과 전투 HUD는 별도의 GridHPBar를 사용하세요. 같은 바에 여러 데이터 소스를 연결하면 서로 덮어씁니다.

기존 `ChPrefab → hpfollow → Hp Bar Prefab`에 사용자 프리팹을 넣는 방식은 유지합니다. 지정된 사용자 프리팹만 복제하며 임의의 체력바 디자인은 만들지 않습니다. 빈 값이면 표시하지 않습니다.

## 확인할 항목

1. UI 참조를 비운 상태에서 Main→Battle→연출→카운트다운→전투가 진행되고 기본 UI가 나타나지 않는지 확인.
2. 연결한 얼굴 RawImage의 내/적 얼굴, 패널 숨김, Countdown Text 3·2·1 표시 확인.
3. HUD의 HP/AP 감소·회복, 속도 단계, 록온, 깃발·종료 결과·부활 확인.
4. 메뉴 미연결 상태에서 Esc가 이동 입력을 잠그지 않는지, 연결 상태에서 Esc/Resume이 동작하는지 확인.
5. 피해 숫자 슬롯 여러 개 연결 후 연속 피격, 사망·재생성, 화면 밖 대상 확인.
6. 로비 선택이 DataConfig를 갱신하고 매칭 중 변경이 막히는지 확인.
7. 바람 미연결 시 자동 Canvas가 생기지 않는지, 연결 후 3단계에서만 표시되는지 확인.

코드 컴파일과 정적 씬 검사를 수행하며 실제 UI 배치/Play 및 2클라이언트 테스트는 사용자 UI 연결 후 필요합니다.
