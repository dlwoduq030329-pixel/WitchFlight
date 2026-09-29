# WitchFlight 배틀 구현 및 확인 방법

## 적용한 규칙

- Main에서 장비를 고른 뒤 CreateRoom()으로 코드 방을 만들고 상대는 JoinRoom()으로 접속합니다. StartRandomMatch()는 코드 없는 랜덤 1대1입니다. 기존 StartMatch()는 자동 생성/참가 호환용으로 유지합니다. 코드 방은 기존 Max Player Count, 랜덤은 2명 고정입니다. 로비 대기는 CancelMatch()로 취소합니다. 연결 및 테스트는 UI_CONNECTIONS.md의 매칭 버튼 항목을 참고하세요.
- 코드 방은 인원/PlayerData가 준비되어도 자동으로 이동하지 않으며 방장의 NetworkGameManager.StartRoomGame() 호출로 Battle 씬에 진입합니다. 랜덤은 양쪽 로비에서 상대 프로필을 각각 1초 표시한 확인을 받은 뒤 자동 전환합니다. Battle 입장 후 기존 연출 시작 흐름은 유지합니다.
- 프로필 번호는 DataConfig.playerprofile에서 Networked int PlayerData.playerprofile로 전달합니다. 기본 0이며 접속 후 본인의 SetPlayerProfile(int)로 변경할 수 있습니다. linkuserinfo에 내/상대 Image와 Sprite 배열을 직접 연결합니다. 랜덤 매칭용 Main UI 한 곳에서 Confirm Random Match Preview를 켜야 자동 진행합니다. UI_CONNECTIONS.md를 참고하세요. 백엔드 저장은 아직 구현하지 않았습니다.
- 선택 값은 기존 DataConfig → PlayerData → Player/PlayerEquipment 흐름으로 전달됩니다. PlayerData는 씬 전환과 사망/부활 사이에도 유지됩니다.
- 배틀 입장 후에는 얼굴 시점으로 준비를 기다립니다. 양쪽의 씬/외형/얼굴 RenderTexture 준비가 끝나면 startGame() → 얼굴 VS 연출(기본 5초) → 후방 카메라와 3·2·1 → 전투 순서로 시작합니다. 설정과 테스트는 BATTLE_START_FLOW.md를 참고하세요.
- 전투 중 마우스 X/Y는 목표 시점을 움직이고, 캐릭터는 빗자루 선회 능력에 맞춰 그 방향을 따라갑니다. 큰 원은 화면 중앙의 목표 방향, 작은 원은 실제 캐릭터 정면의 발사 방향입니다. A/D는 목표 방향의 좌우 회전을 보조합니다. 록온이 시점이나 기체 조작을 대신하지 않습니다. 메뉴/사망/준비 화면/맵 복귀 연출 및 후방 전환 중에는 마우스 시점 입력을 받지 않으며, 리스폰과 맵 복귀 시 목표 시점을 초기화합니다.
- 비행 중 FlightVisualBob이 캐릭터/장비 외형에만 상하 사인 흔들림을 적용합니다. 기본 진폭은 ±0.08m, 주기는 약 1.7초이며 정지 시 서서히 줄어듭니다. 플레이어 루트/이동용 CharacterController/카메라는 흔들지 않고, 기존 다리 캡슐 중심도 반대로 보정합니다.
- 마우스 상하로 목표 고도를 향해 기수를 올리거나 내립니다. 새 조준 모드는 Quaternion 회전으로 수직/역전 비행을 처리하며 기존 Player의 Max Pitch 제한을 사용하지 않습니다. W/S를 누를 때마다 -3~-1, 0, +1~+3 단계가 변합니다. 0의 목표 속도는 정지, 음수는 기체 뒤 방향의 후진입니다. 실제 속도는 가감속 수치를 따라 변화하며 전후진 전환은 먼저 0까지 감속합니다.
- Shift는 누르는 동안 현재 진행 방향으로 부스트하며 빗자루의 `Boost Ap Cost Per Second`만큼 초당 AP를 소모합니다(기본 20). 부스트 중 자연 AP 회복은 중단되고, 놓으면 기존 감속 설정으로 일반 속도로 복귀합니다. 정지 단계에서 사용하면 +1로 출발합니다. 마나가 고갈되면 Shift를 놓았다 다시 눌러야 재사용됩니다. 경직/입력 억제/입력 누락 중에는 부스트하지 않습니다. 이전 Boost Duration/Cooldown은 호환용으로만 남아 있고 사용하지 않습니다.
- 1/2번은 선택한 마법, 3번은 고정 패링입니다. 우클릭으로도 패링을 사용합니다. 패링은 마나를 소비하고 기본 0.3초 동안 마법 피해를 막으며 공격자에게 같은 피해를 반사합니다.
- Fire/Ice 등 requiresTarget 마법은 큰 원/작은 원이 록온 허용각 이내일 때 좌클릭 홀드 중 화면 안의 적 진영 캐릭터를 선택합니다. 허용각은 ChPrefab의 Player → Lock Aim Alignment Tolerance(기본 1.5도) + Lock Aim Offset(기본 3도), 합계 기본 4.5도입니다. 중앙에 가까운 적을 먼저 선택하고 유효한 동안 같은 대상을 유지합니다. 허용각 이탈·화면 밖·거리 초과·벽 가림·은신이면 대상/충전을 초기화합니다. 릴리즈 시에도 같은 조건을 재검사하고 충전 완료 상태여야 유도탄을 발사합니다. 두 원이 기본 정렬 기준 1.5도 이내이면 두 Image는 초록색, 벗어나면 기존 색으로 돌아옵니다. 초록색 판정에 Offset은 적용하지 않습니다.
- Vision은 록온 없는 즉발 광선, Thunder는 록온 없는 전방 범위 투사체입니다. 비록온 마법도 좌클릭을 놓을 때 사용합니다. 카메라 방향 대신 실제 발사 순간의 캐릭터 정면으로 발사하므로 작은 원이 실제 사격 방향입니다. 시전 시간이 있는 마법도 발사 시점의 정면을 사용합니다. 두 원이 일치하지 않아도 발사할 수 있으며 Fire/Ice의 발사 후 유도는 유지됩니다.
- 중앙 깃발은 하나이며 접촉한 플레이어가 소유합니다. 보유자가 사망하거나 접속을 종료하면 그 자리에 떨어집니다. 종료 시 마지막으로 소유했던 진영이 승리합니다. 한 번도 소유하지 않았다면 무승부입니다.
- 사망 후 약 2초 동안 외형이 사라지고 오브젝트가 제거됩니다. 사망 시점부터 7초 뒤 같은 진영에서 HP/마나를 회복해 새 Player를 생성합니다.
- 모든 적 처치로 다음 방에 이동한다는 문구 대신, 이번 배틀은 기획서의 깃발/제한시간/부활 규칙을 사용합니다.

## Inspector에서 편집할 위치

최고속 주변부 추가 블러는 Battle Main Camera의 SpeedCameraEffects → Top Speed Peripheral Blur에서 조절합니다. 실제 속도가 빗자루 기본 최고속도의 75%부터 증가하여 최고속에서 최대가 됩니다. PC_Renderer의 PeripheralSpeedBlurFeature에 연결되어 있으며 기존 3단계 연출은 유지합니다. 자세한 설정은 UI_CONNECTIONS.md를 참고하세요.

자동 생성 UI와 OnGUI 창은 제거했습니다. 준비 화면/전투 HUD/로비/바람 UI는 직접 만든 오브젝트를 연결해야 표시됩니다. 상세 필드와 연결 순서는 UI_CONNECTIONS.md를 참고하세요. 기존 사용자 얼굴 RawImage와 패널 연결은 유지합니다.

| 대상 | 파일/위치 | 주요 값 |
|---|---|---|
| 모자 3종 | Assets/Resources/EquipmentStatTable.asset / Hats | maxAp, apRecoveryPerSecond |
| 빗자루 3종 | 같은 파일 / Brooms | maxHp, maxSpeed, turnSpeed, speedStageTransitionSpeed, brakeSpeed, boostMultiplier, boostApCostPerSecond |
| 마법 10종 | Assets/Resources/MagicStatTable.asset / Magics | apCost, cooldownSeconds, lockChargeSeconds, castSeconds, damage, range, projectileSpeed/TurnSpeed/Lifetime/Radius |
| 패링 | 같은 파일 / Parry | parryApCost, parryWindowSeconds, parryCooldownSeconds |
| 카메라 | Battle의 CameraFollow | Enable Mouse Aim Steering(기본 켜짐), Mouse Yaw Sensitivity/Mouse Pitch Aim Sensitivity(기본 2.5), Follow Offset. 끄면 이전 자유 시점 조작 |
| 최고속 단계 연출 | Battle/Main Camera의 SpeedCameraEffects | Keep Local Player Sharp(켜짐), Motion Blur Mode(CameraAndObjects), Motion Blur Intensity(1), Motion Blur Distance Multiplier(1.8), Extra Field Of View(+10도), Transition Speed(5), Wind Opacity(0.3), Wind Line Count(36), Wind Speed(1.6), Show Wind Lines |
| 비행 흔들림 | ChPrefab의 FlightVisualBob | Amplitude(높이), Frequency(초당 횟수), Full Amplitude Speed, Blend Speed. Amplitude=0이면 끔 |
| 선회 기울임 | ChPrefab의 FlightVisualBob | Max Bank Angle(기본 25도), Full Bank Turn Speed(120도/초), Bank Blend Speed(5), Bank Pivot(공통 회전 중심). Max Bank Angle=0이면 기울임만 끔 |
| 이전 모드 상하 감도 | Assets/Ch/ChPrefab.prefab / Player | Mouse Pitch Sensitivity, Max Pitch. 새 조준 모드에는 적용하지 않음 |
| 경기/부활 | Battle의 BattleManager | Match Duration Seconds(초기 180), Death Despawn Delay(2), Respawn Delay Seconds(7) |
| 인원 | Main의 NetworkGameManager | Max Player Count(기본 2, 혼자 테스트할 때 1) |

선회 기울임은 렌더링된 플레이어 루트의 좌우 회전 속도로 계산하므로 본인과 상대 모두에게 적용됩니다. 오른쪽 선회는 오른쪽, 왼쪽 선회는 왼쪽으로 기울고, 선회가 멈추면 부드럽게 복귀합니다. 이동/카메라 루트와 CharacterController는 기울이지 않습니다. 외형 내부의 다리 캡슐은 외형 기울기를 따르며, 기존 상하 흔들림에 대한 중심 보정은 유지합니다. 연출/사망/비활성 상태에서는 외형 오프셋을 복원합니다.

빗자루 현재값: Slow HP300/속도45/선회90, Standard HP220/속도60/선회135, Speed HP160/속도75/선회180.
감속 Brake Speed는 순서대로 18/28/40, 선회 반응 Turn Acceleration은 6/9/12, 선회 정지 반응 Turn Return Speed는 12/18/24입니다. 최고속도와 가속 값은 유지하며, 이전보다 천천히 감속하고 선회하도록 조정했습니다. 장비 데이터는 Player 생성 시 적용하므로 수정 후 매치를 다시 시작합니다.
모자 초기값: Classic 마나150/회복6, Twisted 100/12, Elemental 60/18.
미지정 장비/마법은 Classic, Standard, Fire, Ice로 보정합니다.

## 3단계 속도감 연출

- 본인 Player.LocalPlayer가 살아 있고 전투 중일 때, 전진 3단계(CurrentSpeedStage == 3)를 선택하면 모션블러/FOV/바람 효과가 약 0.6초에 95%까지 부드럽게 켜집니다. 실제 최고속도 도달을 기다리는 조건은 아닙니다.
- 기본 FOV 60에서 70으로 확대하며, 감속하면 원래 FOV와 블러 0으로 부드럽게 돌아갑니다. 후진 -3 및 상대방의 속도로는 활성화되지 않습니다.
- 사망, 플레이어 없음, 경기 종료, 얼굴 연출, 카운트다운, 메뉴에서는 즉시 기본 시야로 복귀합니다. 카메라 위치/회전/네트워크 보간은 건드리지 않습니다.
- 기존 post Volume에 연결되어 있습니다. 런타임 프로파일을 따로 복제하여 블러(CameraAndObjects, 기본 강도 1, 거리 배율 1.8, High)를 제어하고, 종료 시 복제 자원을 정리합니다. 원본 프로파일의 블러 기본 강도는 0입니다. 다른 후처리 항목은 유지합니다.
- Motion Blur Distance Multiplier는 번짐 거리 배율입니다. URP 17의 셰이더는 모션벡터에 intensity를 곱해 샘플 거리를 결정하므로, 런타임 전용 intensity의 상한을 3으로 확장하고 기본 강도×거리 배율×3단계 전환 비율을 전달합니다. 기본 설정의 최종 값 1.8은 이전 0.9보다 샘플 거리가 2배입니다. 화면상의 새 영역을 추가하는 마스크가 아니라 기존 배경 픽셀의 번짐 길이를 늘립니다. 원본 Volume 자산이나 Unity 패키지를 수정하지 않으며, 0 벡터인 내 캐릭터의 제외 처리는 유지됩니다. URP 업데이트 시 Volume 보간/강도 전달을 재확인해야 합니다. 과도한 거리 배율은 잔상·샘플 줄무늬가 생길 수 있으므로 1~1.8부터 조절합니다.
- Keep Local Player Sharp는 내 캐릭터의 MeshRenderer/SkinnedMeshRenderer(비활성 장비 포함)에 ForceNoMotion을 적용하여 캐릭터 픽셀의 블러 벡터를 0으로 만듭니다. 이 옵션이 켜져 있으면 CameraOnly가 아니라 CameraAndObjects를 사용합니다. 상대방 렌더러와 배경은 변경하지 않습니다. 재생성된 로컬 캐릭터를 다시 등록하며 메뉴/사망/연출/비활성화 때 원래 렌더러 설정으로 복원합니다. 현재 lilToon URP 셰이더의 MotionVectors 패스에 0 벡터 처리가 있습니다. 모션벡터 패스가 없는 커스텀 셰이더나 투명 표면은 별도의 확인이 필요합니다.
- 메인 카메라는 효과 활성 중 Post Processing, 깊이 텍스처, Every Frame Volume 갱신 및 post의 레이어 포함을 명시적으로 설정하고, 컴포넌트를 끄면 이전 설정을 복구합니다. Motion Blur Clamp는 URP의 CameraOnly 경로에서만 사용되므로 현재 강도 조절에는 Motion Blur Intensity를 사용합니다.
- 블러 단독 확인: Show Wind Lines를 끄고 Extra Field Of View를 0으로 놓은 뒤 3단계에서 벽 옆을 지나거나 선회합니다. 정지 화면/먼 하늘만 보고 전진할 때는 블러가 약하게 보일 수 있습니다. 플레이 중 post Volume의 런타임 Motion Blur Intensity가 0에서 1.8로 올라가는지도 확인합니다. 확인 후 FOV/바람 설정을 원래대로 돌립니다.
- 바람은 사용자가 만든 Canvas의 SpeedWindLines를 Main Camera의 Wind Lines에 연결했을 때만 표시합니다. 자동 Canvas 생성은 제거했습니다. 중앙 타원 영역을 비워 조준을 방해하지 않으며 클릭을 가로채지 않습니다.
- 바람 선의 굵기는 각 선이 새로 나타날 때 Wind Min Width~Wind Max Width 범위에서 랜덤하게 정합니다(기본 1080p 기준 전체 두께 1~6px, 해상도에 비례). 선이 움직이는 동안에는 굵기를 유지하고 다음 선으로 순환할 때 다시 정합니다. 동일한 값으로 지정하면 두께가 일정해집니다. 게임플레이의 UnityEngine.Random 상태는 변경하지 않습니다.
- 모션블러/FOV 연결은 유지되어 있습니다. 바람은 Wind Lines를 직접 연결해야 표시됩니다. 멀미/가독성 조절 시 Motion Blur Intensity, Extra Field Of View, Wind Opacity를 낮추거나 각 값을 0으로 설정합니다. Show Wind Lines를 끄면 바람 선만 꺼집니다.
- 테스트: 0→1→2는 효과 없음, 3은 부드러운 활성화, 3→2는 해제, -3은 효과 없음, 메뉴/사망/재생성/경기 종료 후 정상 복원, 호스트/클라이언트 각각 자기 3단계에만 반응하는지 확인합니다. 강한 선회 중 조준점/캐릭터 가독성과 FPS도 확인합니다.

## 마법별 처리

| 마법 | 처리 |
|---|---|
| Fire | 긴 록온, 높은 피해의 유도탄 |
| Ice | 짧은 록온, 낮은 피해의 유도탄, 피격 시 선회 둔화. 패링 성공 시 둔화 없음 |
| Vision | 긴 사거리의 즉발 광선, 낮은 피해/소모 |
| Thunder | 짧은 시전 후 직선 발사, 충돌/사거리/수명 종료 지점 범위 피해. 벽 너머 피해 차단 |
| Flare | 자신을 향한 록온 해제, 비행 중인 유도탄의 추적 중단 |
| Smoke | 적과 최소 거리 이상 떨어졌을 때 은신, 록온/유도 추적 해제 |
| Wind | 5초 동안 속도·가속·선회 버프. 저장 enum 이름 Dark는 기존 저장값 호환 때문에 유지 |
| Decoy | 즉시 0.2초 은신과 추적 해제, 현재 외형을 복사한 2초 분신 |
| Mine | 전방 5m까지 이동 후 정지, 5초 후 활성화, 적·아군·본인 모두 범위 피해 |
| Scan | 반경 20m 은신 적과 지뢰 탐지. 저장 enum Scane은 호환 때문에 유지 |

Mine은 독립 네트워크 오브젝트라 시전자 사망 후에도 설정된 수명까지 유지됩니다.
발사한 마법의 궤적과 피해는 Host가 계산하고 Fusion으로 복제합니다.
마법 모델/피격 효과는 동작을 확인할 수 있는 기본 도형·트레일·복사 메시입니다. 장비 외형은 기존 자식 오브젝트의 활성/비활성 방식입니다.

## 파일 변경

기존 파일 수정:
- Assets/Script/Camera/CameraFollow.cs
- Assets/Script/NetworkGameManager.cs
- Assets/Script/NetworkInputData.cs
- Assets/Script/Player.cs
- Assets/Script/PlayerData.cs
- Assets/Script/enemyLockOn.cs
- Assets/Script/EquipmentStatTable.cs
- Assets/Script/MagicStatTable.cs
- Assets/Script/vibe/BattleManager.cs
- Assets/Resources/EquipmentStatTable.asset
- Assets/Resources/MagicStatTable.asset
- Assets/Scenes/Main.unity
- Assets/Scenes/Battle.unity
- Assets/Ch/ChPrefab.prefab
- Assets/FlagOBJ.prefab

새 파일(각 Unity 자산의 .meta 포함):
- Assets/Script/MagicProjectile.cs, Assets/MagicProjectile.prefab
- Assets/Script/BattleFlag.cs
- Assets/Script/CombatPresentation.cs
- Assets/Script/CombatTransientEffect.cs
- Assets/Script/BattleHud.cs
- Assets/Script/LobbyLoadoutMenu.cs
- Assets/Resources/CombatFade.shader
- Assets/Editor/WitchFlightBattleValidation.cs
- BATTLE_IMPLEMENTATION.md

신규 네트워크 관리자는 없습니다. 기존 NetworkGameManager/Runner와 BattleManager를 사용합니다.
수정 파일은 UTF-8을 유지했으며 별도의 CP949 → UTF-8 변환은 하지 않았습니다.
PlayerEquipment는 이번 작업에서 재작성하지 않았습니다. 이전에 구현한 활성/비활성 전환을 재사용합니다.

## 실행 전 확인

1. Unity로 돌아와 스크립트/프리팹 임포트가 완료될 때까지 기다립니다. Play 중이었다면 중지 후 재실행합니다.
2. Tools → WitchFlight → Validate Battle Setup을 실행해 저장된 씬·테이블·프리팹 참조를 확인합니다. 이 검사는 자산을 수정하지 않습니다.
3. 투사체 등록 관련 오류가 있을 때 Tools → Fusion → Rebuild Prefab Table을 실행합니다. 프리팹은 FusionPrefab 라벨과 NetworkObject 연결을 이미 포함합니다.
4. Main 씬부터 시작합니다. Battle 씬만 단독 Play하면 Runner/PlayerData가 없어 네트워크 매칭 흐름을 시작하지 않습니다.
5. 에디터와 테스트 빌드는 둘 다 최신 코드로 실행합니다. NetworkInputData와 복제 속성이 바뀌어 이전 빌드와 혼용하면 안 됩니다.

## 두 클라이언트 테스트

| 확인 | 기대 결과 |
|---|---|
| 서로 다른 장비/머리 길이, 같은 roomId 접속 | 두 명이 모인 뒤 Battle, 서로 다른 외형과 능력치 |
| A/B 스폰 | (-70,5,0), (70,5,0)에서 서로 중앙을 향함 |
| W 3회, S 6회 | +3 → 0 → -3, 부드러운 감속 후 후진 |
| 상승·하강·A/D·후진·Shift | 목표 시점에 실제 기수가 뒤따르며 기존 속도 단계/부스트 유지 |
| 두 원이 떨어진 상태에서 Vision 발사 | 큰 원이 아닌 작은 원 방향으로 광선 발사 |
| 마우스를 움직인 뒤 정지 | 작은 원이 큰 원으로 수렴, 빗자루 선회 능력에 따라 소요 시간 변화 |
| 수직/역전 비행 및 사망·리스폰·맵 복귀 | 회전 급반전 없이 목표 추적, 연출 종료 후 목표 시점 초기화 |
| Fire/Ice 홀드 후 조기 릴리즈 | 미완성 록온은 발사/마나 소비 없음 |
| 완전 록온 릴리즈 | 투사체가 실제 이동하고 충돌할 때 한 번 피해 |
| 대상 화면 이탈/벽 뒤 이동/은신 | 충전 및 발사 대상 해제 |
| Vision/Thunder | 적이 선택되지 않아도 조준 방향으로 발사, Thunder는 시전 지연/범위 피해 |
| 마나 부족 및 패링 | 부족하면 발동 불가, 패링 창 동안 피해 0 및 반사 |
| Ice를 패링 | 방어자에게 둔화 미적용 |
| Smoke/Flare/Decoy | 발사 전 록온과 이미 날아오는 탄의 유도 모두 끊김, 분신은 양쪽에서 보임 |
| Mine | 이동·활성화 지연·아군/본인 피해, 시전자 사망 후에도 유지 |
| Scan | 범위 내 숨은 적·지뢰 표시 |
| 깃발에 고속으로 접촉 | 통과해도 획득, 벽을 사이에 두면 획득 불가 |
| 깃발 보유자 사망 | 사망 위치에 같은 깃발 드롭, 상대가 접촉하여 획득 |
| 사망/부활 | 약2초 페이드 및 제거, 사망7초 후 원 진영 부활·외형 복원·카메라 재연결 |
| 메뉴 연 상태에서 부활 | 메뉴와 커서 유지, 온라인 경기는 계속 진행 |
| 제한시간 종료 | 마지막 깃발 보유 진영 결과 표시, 이동·공격·부활 종료 |

## 현재 검증 범위

신규 런타임 스크립트를 포함하여 설치된 Unity 6000.0.77f1의 C# 컴파일러와 기존 프로젝트 참조로 격리 컴파일했습니다. 결과 DLL은 프로젝트 밖의 임시 폴더에만 생성했습니다.
프리팹 GUID·컴포넌트 목록·테이블 값과 UTF-8 인코딩도 정적으로 확인했습니다.

두 클라이언트의 실제 Play 검증, Unity 내부 Fusion IL 위빙, 셰이더 렌더링은 이 작업 환경에서 실행 확인하지 못했습니다.
현재 비행은 기존 Host 권한 시뮬레이션을 유지하므로 네트워크 지연이 큰 환경의 입력 지연은 별도 예측 이동 작업이 필요합니다.
로비 선택은 현재 접속의 DataConfig에 적용됩니다. 백엔드 저장 API는 변경하거나 자동 호출하지 않습니다.
