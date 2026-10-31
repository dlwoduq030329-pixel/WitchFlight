# 재화와 아이템 데이터 설정

## 뒤끝 LoginPlayerData에 추가할 컬럼

로그인/회원가입을 테스트하기 전에 아래 두 컬럼을 추가합니다. 다른 테이블을 만들 필요는 없습니다.

| 컬럼 이름 | 타입 | 기본값 | null |
| --- | --- | --- | --- |
| `money` | int | `20000` | 비허용 |
| `itemInventoryJson` | string | `{}` | 비허용 |

`money`는 신규 계정 생성 시 20,000으로 저장합니다. 기존 계정에서 값이 누락되거나 null이면 초기 잔액을 저장한 후 진행합니다. 저장된 0원은 그대로 유지하며 다시 20,000원을 지급하지 않습니다. 잘못된 숫자는 임의로 초기화하지 않고 오류를 표시합니다.

`itemInventoryJson`은 보유한 고정 ID 목록과 장착 슬롯을 한 JSON 문자열에 저장합니다. `{}` 또는 기존 기록에 기본 목소리가 없으면 로그인 시 기본 목소리(`3.1`)를 무료 보유 목록에 추가하고 저장합니다. 장착한 목소리가 없다면 기본 목소리를 장착하며, 이미 다른 목소리를 장착했다면 그 선택은 유지합니다. 전체 저장 시 다음 형식으로 기록합니다.

```json
{"version":1,"ownedItemIds":["hair_001","emote_001"],"equippedItems":[{"category":0,"slot":0,"itemId":"hair_001"},{"category":2,"slot":0,"itemId":"emote_001"}]}
```

종류 번호: Hair=0, Animation=1, Emote=2, Voice=3. 기존 번호와 이미 배포한 아이템 ID는 바꾸거나 다른 아이템에 재사용하지 않습니다.

## 재화 표시

Main/MainScene 로드 시, 비활성 패널을 포함해 이름이 `MoneyText`인 TMP 텍스트에 `PlayerMoneyLabel`을 자동으로 연결합니다. 별도 Inspector 할당은 필요 없습니다. 현재 잔액을 `20,000`처럼 표시하며 숫자만 출력합니다. 창을 다시 열거나 서버 값을 불러오거나 잔액을 변경하면 갱신합니다. 씬 전체 검색은 씬 로드 때 한 번만 합니다.

동적으로 새로 생성하는 UI 프리팹에는 `PlayerMoneyLabel`을 직접 붙이면 동일하게 동작합니다.

## 아이템 등록

`Assets/Resources/ItemCatalog.asset`을 선택하고 **Items**에 항목을 추가합니다. 현재 기존 기본 공주 목소리 항목과 `3.2`(4,800원)을 연결했습니다.

공통 필드: **Id / Category / Display Name / Description / Icon / Price**.

- **헤어**: `Hair Prefab`에 사용할 프리팹을 지정합니다.
- **애니메이션**: `Animation Clip`, `Animator State Name`에 클립과 상태 이름을 지정합니다.
- **이모티콘**: `Emote Id`에 기존 `EmoteTable`의 ID를 지정합니다. ItemCatalog의 문자열 ID와 EmoteTable의 숫자 ID는 서로 다른 용도입니다.
- **목소리**: `Voice Pack`에 CharacterVoicePack 에셋을 지정합니다. 아래 목소리 구매/장착 설명을 참고하세요. 기존 `Voice Cues`는 호환을 위해 남겨두었으며 현재 파일명 기반 교체에는 사용하지 않습니다.

목소리는 구매 및 재생 교체까지 연결했습니다. 헤어·애니메이션·이모티콘은 보유/장착 저장 기반만 있으며, 해당 상품의 구매와 실제 외형·재생 교체는 아직 연결하지 않았습니다.

## 코드 연결 지점

```csharp
int balance = DataConfig.money;
bool added = DatabaseManager.Instance.TryAddMoney(1000);
bool spent = DatabaseManager.Instance.TrySpendMoney(500);

// 지급/구매 승인이 끝난 아이템을 보유 목록에 추가. 여기서는 재화를 차감하지 않습니다.
bool granted = ItemInventory.TryGrantItem("hair_001");
bool owned = ItemInventory.HasItem("hair_001");
bool equipped = ItemInventory.TryEquipItem("hair_001");
string selectedHair = ItemInventory.GetEquippedItemId(ItemCategory.Hair);
bool removed = ItemInventory.TryUnequipItem(ItemCategory.Hair);

// Emote 슬롯: 0=위, 1=오른쪽, 2=아래, 3=왼쪽. 다른 종류는 슬롯 0만 사용합니다.
ItemInventory.TryEquipItem("emote_001", 3);
string leftEmote = ItemInventory.GetEquippedItemId(ItemCategory.Emote, 3);
```

미로그인/데이터 로드 전 변경, 음수 금액, 잔액 초과 차감, int 범위 초과 지급, 중복 아이템 지급, 미보유 아이템 장착, 유효하지 않은 슬롯은 실패(false)합니다. 미보유 기본 외형·기본 감정표현을 자동으로 잠그지는 않습니다.

위 개별 지급·차감·장착 함수는 기존 DatabaseManager 자동 저장 큐를 사용합니다. 함수의 true는 로컬 변경을 수락했다는 의미이며, 서버 저장 완료는 `IsAutoSavePending`과 `LastSaveSucceeded`로 확인합니다. 구매 버튼에서 개별 지급/차감 함수를 조합하지 말고 아래 전용 구매 함수를 사용하세요.

재접속은 뒤끝 값을 읽으며, 로그아웃 시 로컬 잔액과 인벤토리를 비웁니다. 저장 대기 중 로비에 돌아와도 최신 잔액/인벤토리를 과거 응답으로 덮어쓰지 않도록 처리했습니다. 강제 종료 전에 서버 저장이 완료되지 않은 변경은 재접속 시 남아 있지 않을 수 있습니다.

## 목소리 상품과 구매 버튼

현재 `3.2` 상품의 Price는 **4800**이며 `VoiceWeakGirl.asset`을 연결했습니다. 기본 공주 목소리 항목의 기존 ID `3.1`은 유지했습니다. 가격/표시 이름/아이콘은 ItemCatalog에서 변경할 수 있습니다.

상점 UI의 활성 오브젝트에 **VoiceShopUI**를 추가합니다.

| 칸 | 연결할 UI |
| --- | --- |
| Item Name Text | 상품 이름 TMP |
| Status Text | 구매/장착 결과·잔액 부족 안내 TMP |
| Item Icon | 상품 아이콘 Image |
| Purchase Button | 선택 상품의 구매 확인창을 여는 별도 버튼 (선택 사항) |
| Equip Button | 별도 장착 버튼이 있을 때만 할당 (선택 사항) |
| Purchase Check | 공용 `purchaseCheck` 패널 |
| Confirm Button | `purchaseCheck` 하위의 `Confirm` 구매 확정 버튼 |
| Cancel Button | 구매 확인창 닫기 버튼 (선택 사항) |
| Purchase Check Name Text / Price Text | 구매 확인창의 상품명 / 가격 TMP (선택 사항) |

버튼을 위 칸에 넣으면 클릭 이벤트를 자동 연결하므로 OnClick에 같은 함수를 중복 등록하지 마세요. 직접 이벤트를 연결하려면 버튼 칸은 비워두고 다음 public 함수를 사용합니다.

- 각 상품 버튼의 기존 OnClick에 `VoiceShopUI.SelectVoice(string)`과 상품 ID만 연결합니다 (`3.2`, 기본 `3.1`). 추가 컴포넌트나 가격 텍스트 할당은 필요 없습니다.
- 버튼 하위 `PriceText`를 자동으로 찾습니다. 없으면 유일한 `Text` 또는 `Text (TMP)`를 사용합니다. `ItemText` 등 상품명 텍스트는 수정하지 않습니다.
- 상위 StoreUI의 VoiceShopUI가 기존 SelectVoice 이벤트에서 버튼과 상품 ID를 자동으로 수집합니다. 편집기 실행 및 빌드 시 자동 수집하며 매 프레임 검색하지 않습니다.
- 첫 클릭은 미리듣기, 같은 상품 재클릭은 `purchaseCheck` 열기. 다른 상품을 클릭하면 그 상품의 첫 미리듣기부터 시작합니다.
- 구매 확정: `VoiceShopUI.ConfirmPurchase()`. 이전 `PurchaseSelectedVoice()`는 이제 확인창만 열며 재화를 차감하지 않습니다.
- 구매 취소: `VoiceShopUI.CancelPurchase()`.
- 보유 상품 장착: `VoiceShopUI.EquipSelectedVoice()`.
- 기본 목소리 복귀: `VoiceShopUI.UseBasicVoice()`.

권장 구성은 **StoreUI의 VoiceShopUI 하나 + 상품 버튼의 SelectVoice(ID) + 공용 purchaseCheck**입니다. VoiceShopUI는 purchaseCheck 밖에 둡니다. Confirm Button 필드에 할당하면 확인 이벤트가 자동 연결됩니다. 상품 버튼의 기존 즉시 구매/PurchaseCheck SetActive 이벤트만 제거하고 SelectVoice 이벤트는 유지하세요. VoiceShopItemButton은 실행 중 자동으로 붙는 내부 처리이며 사용자가 직접 추가하거나 ID를 입력하지 않아도 됩니다.

미리듣기는 CharacterVoicePack의 **Preview Clip**에 원하는 음성을 할당합니다. 비어 있으면 같은 팩의 Greet 음성, 없으면 첫 음성을 사용하며 Voice 볼륨 설정을 따릅니다. 다른 상품 선택이나 상점 닫기/씬 이동 시 재생을 중단합니다. 미리듣기와 구매는 장착 상태를 변경하지 않습니다.

각 버튼의 하위 가격 텍스트는 UI가 다시 활성화될 때와 서버에서 인벤토리가 로드될 때 자기 Item Id의 보유·장착 정보로 갱신됩니다. 씬 이동 후에도 `가격 / 보유중 / 장착중`이 유지됩니다. VoiceShopUI의 단일 Price Text 할당 칸은 제거했습니다. 새 씬에서도 StoreUI 아래 상품 버튼의 SelectVoice(ID) 연결만 유지하세요.

코드 호출: `DatabaseManager.Instance.TryPurchaseVoice("3.2", out string message)`.

구매는 서버 잔액/보유 목록을 다시 조회하고, 중복 보유·가격·잔액을 검사한 후 **money와 itemInventoryJson을 같은 UpdateV2 요청으로 저장**합니다. 성공 응답 후 잔액과 보유 목록만 바꿉니다. 구매만으로 장착 목소리는 바뀌지 않으며, 상점의 장착 버튼을 눌러야 적용됩니다. 이미 보유한 상품을 다시 눌러도 재차 차감하지 않습니다. 20,000원에서 WeakGirl을 사면 15,200원입니다.

다른 계정 자동 저장이 진행 중인 동안에는 구매를 거절하고 잠시 후 재시도 안내를 표시합니다. 구매 응답이 실패/불명확하면 후속 자동 저장을 막아 과거 잔액으로 덮어쓰지 않고, 재로그인 후 서버의 실제 보유/잔액을 복원하도록 안내합니다. 실제 뒤끝 계정의 구매 수신은 별도로 테스트해야 합니다.

현재는 기존 클라이언트 계정 저장 구조를 이용합니다. 서로 다른 기기에서 같은 계정으로 동시에 구매하는 요청의 조건부 갱신이나 서버 측 가격/영수증 검증 기능까지 제공하는 것은 아닙니다.

## 새 목소리 폴더 연결

1. `Assets/Sound/Voice/<목소리이름>`에 음성을 넣습니다. 기본 음성과 파일명을 맞춥니다.
2. Create → WitchFlight → Items → Character Voice Pack으로 에셋을 생성합니다.
3. 해당 에셋 Inspector에서 **폴더에서 목소리 파일 가져오기**를 눌러 폴더를 선택합니다. 개별 AudioClip 드래그도 가능합니다. 폴더에 파일을 나중에 추가했다면 다시 가져옵니다.
4. ItemCatalog에 고정 ID / Voice 종류 / 가격 / Voice Pack을 지정합니다. 전투 동기화를 위해 ID는 64자 이하로 사용합니다.

파일명이 정확히 같으면 우선 대응합니다. 기존 파일의 `SFX_Voice_Greet`와 새 파일의 `SFX_Voice_Greet_01`, 기존 `SFX_Voice_Attack-03`와 새 `SFX_Voice_Attack_03` 차이도 호환합니다. 대응 음성이 없으면 해당 기본 음성을 사용합니다. 현재 Basic_Princess 17개 모두 WeakGirl에 대응됩니다.

교체는 기본 음성을 선택한 다음 재생 직전에 수행하므로 원래 랜덤 변형, 음량, 공간 음향, Voice 믹서 설정은 유지합니다. 로비 인사·전투 공격/피격/패링/회복/사망/VS/승패 등 **Basic_Princess의 클립을 사용하는 재생**에 적용됩니다. 고양이·BGM·마법 효과음은 대상이 아닙니다.

전투 참가자의 목소리 ID는 PlayerData로 전달합니다. 캐릭터가 낸 음성은 해당 캐릭터의 장착 목소리로 재생하고, 자신의 로비 인사/승패 안내는 자신의 계정 목소리를 사용합니다. 모든 클라이언트 빌드에 동일한 ItemCatalog와 음성 에셋이 포함되어야 합니다.

자동 연결된 상품 카드는 보유/장착 변경 시 모든 열린 카드의 하위 PriceText가 함께 갱신됩니다. 기본 목소리 역시 동일하게 `보유중`/`장착중`으로 표시하며 구매 버튼은 비활성화합니다. 기존 `money`, `itemInventoryJson` 컬럼을 그대로 사용하므로 추가 뒤끝 컬럼은 없습니다.

### 구매 후 표시 및 장착 연결 수정

Main 씬의 KingHime(3.1), EasyGirl(3.2)의 기존 SelectVoice 연결로 가격 갱신을 자동 연결합니다. 각각 하위 PriceText를 자동 갱신합니다. 공용 Confirm은 미보유 상품에서는 구매, 보유 상품에서는 장착으로 동작합니다. 구매 직후 자동 장착은 하지 않습니다. 같은 보유 상품을 재클릭하여 확인창을 열고 장착을 누르면 재화 차감 없이 적용합니다. 별도 Equip Button 할당은 선택 사항입니다. 상품 ID는 저장 키이므로 구매 기록 생성 후 변경하지 마세요.

PurchaseCheck 내부의 Item, ItemImage, ItemText, PriceText, DialogText는 이름으로 자동 탐색합니다. 구매창은 선택한 상품 버튼의 ItemImage 스프라이트(없으면 카탈로그 아이콘), 카탈로그 상품명/가격을 표시합니다. 장착창은 Item 전체를 숨기고 DialogText를 장착 하시겠습니까?로 바꾸며 Confirm을 장착으로 표시합니다. 다시 구매창을 열면 Item과 원래 구매 질문을 복원합니다. 추가 Inspector 할당은 필요 없습니다.

상품 버튼 하위 CheckImage는 비활성 상태도 자동 탐색합니다. 저장된 장착 ID와 일치하는 상품만 활성화하고, 다른 상품의 체크는 끕니다. 미리듣기/구매만으로 체크가 변경되지는 않습니다. 씬 재진입 및 서버 데이터 로드 시에도 장착 상태를 복원하며 추가 할당이나 뒤끝 컬럼은 필요 없습니다.
