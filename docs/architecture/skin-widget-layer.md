# 스킨 위젯 레이어

플러그인은 데이터·동작·기본 외형을, 스킨은 배경과 위젯 외형·상호작용을 가진다. 결정과 이유는 [ADR 0109](../decisions/0109-widget-pixel-size-and-icon-menu.md), [ADR 0110](../decisions/0110-skin-widget-layer.md)에 있다. 메시지 JSON과 상태 형식의 정확한 모양은 [스킨 업로드 안내의 위젯 레이어 절](../../registry/skins/UPLOAD.md#위젯-레이어)이 기준이다.

## 레이어

```text
┌─────────────────────────────────────────────┐
│ 앱 레이어 (오버레이 창, WPF)                  │  편집 틀 · 모달(설정 포함) · 아이콘 메뉴
├─────────────────────────────────────────────┤
│ 위젯 레이어                                   │  탭 막대를 포함한 모든 위젯
│   · 스킨이 선언한 형식 → 월드 페이지가 그림    │
│   · 나머지 → 플러그인의 WPF 기본 외형          │
├─────────────────────────────────────────────┤
│ 스킨 배경 (월드 창, WebView2)                 │  2D 캔버스 또는 3D
└─────────────────────────────────────────────┘
```

- 월드 페이지 한 장이 배경과 스킨 위젯을 함께 그린다. 이 페이지는 오버레이 바로 아래, 입력을 받지 않는 별도 창(`WorldBackdrop`)에 있다.
- 오버레이가 모든 입력을 받는다. 스킨이 그리는 위젯 자리에는 투명한 타일이 있어, 입력을 받아 페이지로 넘긴다.
- 페이지 위젯은 오버레이 창 아래에 있으므로 WPF 위젯과 겹치면 WPF 위젯이 위에 보인다. 위젯끼리는 겹치게 배치할 수 없어 실제로는 드러나지 않는다.

## 역할

| 주체 | 가진 것 | 코드 |
|---|---|---|
| 플러그인 | 데이터, 동작, 기본 WPF 외형 | 위젯 뷰가 `IWidgetState`를 구현한다(선택) |
| 앱 | 배치, 편집, 좌표 변환, 메시지 중계, 동작 검증 | `OverlayTabs.SkinLayer.cs`, `Design/SkinWidgets.cs`, `Design/WorldBackdrop.cs` |
| 스킨 | 배경, 선언한 위젯의 외형과 상호작용 | `skin.json`의 `theme.world`, `theme.widgets`, `world.html` |

### `IWidgetState` (Facet.Plugins)

- `Type`: 상태 형식 이름 `<pluginId>/<widget>@<version>`, 예: `facet.clock/time@1`. 형식을 바꾸면 버전을 올린다.
- `Json()`: 지금 상태를 JSON 객체로 돌려준다.
- `Changed`: 상태가 바뀌면 알린다.
- `Act(name, argsJson)`: 이름이 붙은 동작을 실행한다. 인자 검사와 저장은 플러그인이 한다.

탭 막대는 앱이 제공하는 위젯 `builtin.tabs`이고 형식은 `facet.app/tabs@1`이다.

## 흐름

1. **선언:** 스킨이 `theme.widgets`에 그릴 형식을 적는다. 월드 페이지(`theme.world`)가 있어야 한다.
2. **판정:** 보이는 탭의 위젯 중 `IWidgetState.Type`이 목록에 있는 것을 스킨 위젯으로 고른다. 그 위젯의 WPF 뷰는 상태를 위해 살아 있지만 숨기고, 패널 대신 투명 타일을 둔다.
3. **상태 전송:** 앱이 `{ widgets: [...] }`를 보낸다. 항목마다 id, 형식, 오버레이 창 좌표의 위치와 크기, 기본 크기, 상태가 있다. 배치·이동·확대·탭 전환·`Changed` 때 33ms 단위로 모아 다시 보내고, 내용이 같으면 보내지 않는다.
4. **그리기:** 페이지가 위젯을 기본 크기로 그린 뒤 타일 크기로 가로·세로를 따로 늘인다. 목록에서 빠진 위젯은 지운다.
5. **입력:** 편집 모드가 아닐 때 투명 타일 위의 포인터(이동, 누르기, 떼기, 휠, 나감)를 위젯 기본 크기 좌표로 `{ pointer }`에 담아 보낸다. 호버, 누름 효과는 페이지가 그린다.
6. **동작:** 페이지가 `{ type: 'action', id, name, args }`를 보내면, 앱이 마지막으로 보낸 목록의 위젯인지와 이름·인자 모양을 검사한 뒤 `Act`를 호출하고 `{ actionResult }`로 답한다. 편집 중이거나 모르는 id면 거부한다.
7. **빈 곳 클릭:** 위젯이 아닌 곳의 왼쪽 클릭은 `{ next: true, x, y }`로 넘긴다([ADR 0107](../decisions/0107-world-click-position-and-cat-room.md)).

## 배치

- 위젯 위치와 크기는 보드 px로 저장한다. 위젯마다 기본 크기(`WidgetDescriptor.Width`/`Height`)가 있다.
- 내용은 기본 크기로 배치한 뒤 타일에 맞춰 늘인다. WPF 위젯은 `WidgetStretch`가, 스킨 위젯은 페이지가 같은 방식으로 늘인다.
- 크기를 조절할 때 기본은 자유 늘이기이고, Shift는 비율 유지, Alt는 그 드래그만 스냅을 반대로 쓴다. 격자 칸은 2px부터 1px 단위이고 스냅은 끌 수 있다.

## 대체 동작

- 스킨이 형식을 선언하지 않았거나, 월드 페이지가 없거나 실패하면 WPF 기본 외형으로 보인다.
- `IWidgetState`를 구현하지 않은 예전 플러그인도 기본 외형으로 보인다.

## 보안과 개인정보

- 일정 제목 같은 사용자 기록이 상태로 페이지에 넘어간다.
- 페이지는 네트워크가 막혀 있다(앞에 붙인 CSP, 죽은 프록시, WebRTC 정책, 다른 주소 이동 취소). 위치, 지역 같은 정보는 상태에 넣지 않는다.
- 월드 페이지나 위젯 레이어를 쓰는 스킨은 자동 공개하지 않고 관리자 검토를 거친다.

## 예: 고양이 방 스킨

`registry/skins/catroom`이 위젯 9종을 모두 선언하고 그린다.

| 형식 | 외형 | 동작 |
|---|---|---|
| `facet.app/tabs@1` | 나무 레일 위 종이 탭 | `select` |
| `facet.clock/time@1`, `weekday@1`, `date-time@1` | 고양이 얼굴 시계 | 없음 |
| `facet.weather/now@1` | 매달린 나무 팻말 | `refresh` |
| `facet.schedule/agenda@1` | 코르크 보드 포스트잇 | 없음 |
| `facet.condition/current@1` | 선반 위 고양이 얼굴 토큰 | `record` |
| `facet.condition/history@1` | 츄르 튜브 | `edit` |
| `facet.sleep/week@1` | 벤치 위 자는 고양이 | `edit` |

## 검사

- `impls/Facet.JournalChecks/SkinWidgetChecks.cs`: manifest `widgets` 검증, 좌표 변환, 투명 타일, 동작 라우팅, 실제 WebView2 테스트 페이지와의 메시지 왕복
- `impls/Facet.JournalChecks/WidgetStateChecks.cs`: 1st-party 형식별 상태 모양과 동작
