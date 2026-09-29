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
| 조준 | Reticle(정중앙 UI는 직접 배치), Lock Marker(RectTransform), Lock Progress Fill, Lock Text |
| 카메라 | World Camera(현재 Battle Main Camera 연결됨) |
| 피해 숫자 | Damage Labels 배열에 미리 배치한 TMP 텍스트들을 연결. 프리팹이 아니라 씬 인스턴스입니다. |

피해 숫자는 연결된 슬롯만 순환 재사용합니다. 이동·축소·투명도 애니메이션이 적용되며 원래 색상/크기는 복원됩니다. 배열이 비면 피해 숫자는 표시하지 않습니다. Lock Marker와 피해 숫자의 위치는 Canvas 좌표로 변환합니다. Screen Space Camera/World Space Canvas를 쓰면 Canvas의 World Camera도 연결하세요. 피해 숫자는 활성화되어 있는 Canvas 아래 별도 부모에 두는 것이 편합니다.

Resume Button은 코드에서 리스너를 연결하므로 OnClick에 Resume을 중복 등록할 필요는 없습니다. 별도 버튼은 `BattleHud.ToggleMenu()` 또는 `Resume()`에 연결할 수 있습니다. 조작 안내 문구/레이아웃/장식은 사용자 UI에 직접 작성합니다.

## 로비 장비 UI: Main / NetworkGameManager가 있는 오브젝트 / LobbyLoadoutMenu

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
