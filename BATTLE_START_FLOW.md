# 배틀 시작 연출

## 처리 흐름

1. Main에서 기존 매칭 조건(`NetworkGameManager.Max Player Count`, 기본 2명)을 채우면 Battle을 로드한다.
2. 기존 PlayerData로 양쪽 Player를 생성하고 외형을 적용한다. 이동·회전·공격·피해·깃발 획득·경기 타이머는 아직 시작하지 않는다. 주 카메라는 내 캐릭터 얼굴을 본다.
3. 각 클라이언트는 씬 로드, 모든 Player와 외형 적용, 내 얼굴/적 얼굴 RenderTexture의 실제 렌더 완료를 확인한다. 자기 Player의 InputAuthority RPC로 호스트에 준비 완료를 알린다.
4. 양쪽 준비가 끝나면 기본 설정에서는 호스트가 `BattleManager.startGame()`을 자동 호출한다. 수동 호출도 가능하지만 준비 조건은 건너뛸 수 없다.
5. 두 얼굴을 각각 투명 RenderTexture → RawImage에 표시한다. 기본 연출 시간은 5초다.
6. 연출 종료 후 얼굴 화면을 숨기고 카메라를 내 캐릭터 뒤로 복귀시킨다. 공유 TickTimer로 `3 → 2 → 1`을 표시한다.
7. 호스트가 Playing 상태로 전환할 때 입력·전투·깃발 획득을 허용하고 경기 제한시간(기본 180초)을 시작한다.

네트워크 상태는 새 관리자를 만들지 않고 기존 BattleFlag에 추가했다. 클라이언트마다 독립적인 코루틴으로 시작 시각을 결정하지 않는다. 연출/카운트다운 중 한 명이 나가면 정해진 인원을 줄이지 않고 대기로 돌아간다. 호스트 이탈 복구는 기존 프로젝트의 범위를 따른다.

## Inspector

Battle 씬의 `battlemanager` 오브젝트:

- **BattleManager → Auto Start When Ready**: 기본 켜짐. 끄면 두 유저가 준비되어도 대기한다. 버튼/UnityEvent에서 `BattleManager.startGame()` 또는 `StartGame()`을 호출하면 시작 요청을 보낼 수 있다. 이 함수는 방에 참가하는 NetworkGameManager.StartMatch와 다르다.
- **BattleManager → Intro Duration Seconds**: 얼굴 연출 유지 시간, 기본 5초. 카운트다운은 별도로 3초 고정이다.
- **BattleIntroPresentation → Local Portrait / Opponent Portrait**: 직접 만든 Canvas 안의 RawImage를 지정한다. Texture 칸은 비워도 코드가 각자의 RenderTexture를 연결한다.
- **Intro Panel**: 얼굴 연출용 패널. 연출 때만 켜진다. BattleManager/Presentation 스크립트 자체는 이 패널이 아닌 항상 활성 상태인 오브젝트에 둔다.
- **Countdown Text / Waiting Text**: 선택 항목인 TextMeshPro UI. Intro Panel 바깥의, 항상 활성화된 Canvas 아래에 두는 것을 권장한다.
- UI 참조를 비워 두면 해당 UI는 표시하지 않는다. 기본 Canvas/RawImage/VS/라벨/대기/카운트다운 UI 자동 생성은 제거했다. UI가 없어도 네트워크 준비와 게임 시작은 계속 진행한다. Solo Text도 직접 연결할 수 있다. 전체 연결 안내는 UI_CONNECTIONS.md를 참고한다.
- **Portrait Half Height**: 얼굴 화면에 담기는 세로 범위의 절반. 작을수록 확대된다. **Head Offset / Portrait Yaw**로 중심과 방향을 조정한다. **Portrait Resolution**은 기본 512다.
- **On Intro Started / On Intro Ended**: 로컬 UI 애니메이션/사운드를 연결할 수 있다. 네트워크 연출 시간 자체는 Intro Duration Seconds로 정한다. Intro에서 대기로 취소될 때도 종료 이벤트가 발생한다.

Battle의 Main Camera → CameraFollow:

- **Face Distance / Face Height Offset**: 입장 대기 중 실제 캐릭터를 바라보는 주 카메라 구도. RawImage 얼굴 구도와는 별개다.
- 기존 Follow Offset/Speed 설정은 카운트다운과 전투 중 후방 시점에 그대로 사용된다.

`TagManager`의 기존 빈 31번 레이어를 **BattlePortrait**로 설정했다. 얼굴 촬영에만 사용하므로 다른 오브젝트에 배정하지 않는다. 주 카메라는 이 레이어를 제외한다. 실제 플레이어와 충돌 레이어는 바꾸지 않는다.

## 얼굴 촬영 방식

외형 적용 후 현재 보이는 메시·재질·재질 속성을 복사한 **정지 포즈 얼굴 사진**이다. Player/NetworkObject/스크립트/충돌체를 복제하지 않는다. 스킨 메시만 현재 포즈로 굽고 촬영 카메라는 투명 ARGB32 배경과 포스트 프로세싱 끄기를 사용한다. 두 RenderTexture를 촬영한 뒤 카메라는 멈춰 비용을 줄인다. 연출이 끝나면 생성한 메시·카메라·RenderTexture를 해제한다. 라이브 표정/포즈 애니메이션 재생은 별도 확장 사항이다.

## 변경 파일

- 새 파일: `Assets/Script/BattleIntroPresentation.cs`, `.meta`, 이 문서.
- 기존 코드 수정: BattleManager, BattleFlag, Player, NetworkGameManager, CameraFollow, BattleHud, enemyLockOn, MagicProjectile, hpfollow.
- 씬/설정 수정: `Assets/Scenes/Battle.unity`, `ProjectSettings/TagManager.asset`.
- 이번 작업에서 ChPrefab/FlagOBJ 프리팹을 추가 수정하지 않았다. 기존 네트워크 컴포넌트와 머리 본을 재사용한다.
- 기존 파일은 UTF-8이며 별도의 인코딩 변환은 하지 않았다.

## 테스트 체크리스트

1. Unity 임포트/컴파일 완료 후 Main에서 실행한다. 새 네트워크 필드/RPC가 있으므로 에디터와 실행 파일 모두 최신 코드로 다시 빌드/실행한다.
2. 다른 장비/머리 길이로 같은 방에 2명 접속: 준비 중에는 서로 이동·공격 불가, 내 얼굴이 주 카메라에 보이는지 확인한다.
3. 한쪽의 로딩이 늦으면 먼저 입장한 쪽은 대기해야 한다. 양쪽 모두 준비되면 각자의 왼쪽은 나, 오른쪽은 상대이고 장비가 맞아야 한다.
4. 얼굴 이미지 배경은 투명해야 한다. 패널 배경은 사용자 UI가 담당한다. 모자/얼굴이 잘리면 Portrait Half Height와 Head Offset을 조절한다.
5. 연출 5초 뒤 두 화면 모두 후방 시점으로 돌아오고 3, 2, 1 이후 조작이 시작되어야 한다. 경기 타이머는 그때부터 감소해야 한다.
6. 연출 중 W/S/A/D/클릭을 눌러도 이동·공격·피해가 없어야 한다. 시작 후 비행, 적 HP바, 패링, 깃발 획득, 사망/부활은 기존 동작을 유지해야 한다.
7. Auto Start When Ready를 끄고 startGame()을 호출하기 전후를 비교한다. 두 번째 유저가 준비되기 전에 호출해도 먼저 출발하면 안 된다. 반복 호출도 한 번만 시작되어야 한다.
8. 연출/카운트다운 중 한 명 연결 종료: 남은 쪽이 혼자 전투를 시작하지 않아야 한다.
9. 혼자 테스트할 때만 Main의 Max Player Count를 1로 바꾸면 상대 자리에 SOLO PRACTICE가 표시되며 동일한 연출/카운트다운을 진행한다. 기본 2명 설정은 유지했다.

정적 씬/레이어 검증과 C# 컴파일을 수행했다. 실제 2클라이언트 접속과 화면 캡처 검증은 별도로 필요하다.
