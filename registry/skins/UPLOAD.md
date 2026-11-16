# 스킨 업로드 양식

스킨은 `skin.json`과 PNG로 만들고, 필요하면 글꼴과 3D 월드 페이지를 더합니다. 자동 검사를 통과하면 바로 목록에 공개됩니다. 3D 월드 페이지가 있는 스킨만 관리자 검토 후 공개됩니다.

## 폴더 구성

```text
my-skin/
├─ skin.json     (필수)
└─ sky.png       (theme에서 쓰는 이미지만)
```

넣을 수 있는 파일은 `.json`, `.png`, `.txt`, `.md`, `.html`, `.ttf`, `.otf`이고, 전체 64MB까지입니다.

## skin.json

```json
{
  "id": "my-skin",
  "name": "내 스킨",
  "version": "1.0.0",
  "description": "내 스킨 설명",
  "colors": { "Bg": "#06141A", "Panel": "#DE0B1F26", "Ink": "#E8FFF6", "Mint": "#6CF2C2" },
  "theme": { "background": "sky.png", "backgroundOpacity": 0.6, "stars": 160 }
}
```

| 항목 | 필수 | 규칙 |
|---|---|---|
| `id` | 예 | 영문 소문자·숫자로 시작하고 소문자·숫자·`.`·`_`·`-`만 씁니다. 64자까지이며 `.`으로 끝날 수 없습니다. 내장 스킨 `default`는 쓸 수 없고, 이미 목록에 있는 ID는 처음 올린 사람만 갱신할 수 있습니다. |
| `name` | 예 | 목록에 보이는 이름, 64자까지 |
| `version` | 예 | 영문·숫자로 시작하고 영문·숫자·`.`·`-`만 씁니다. 32자까지이며 `1.0.0` 형식을 권장합니다. |
| `description` | 아니요 | 목록에 200자까지 표시 |
| `colors` | 아니요 | 아래 색 토큰. 쓰지 않은 색은 기본 스킨을 따릅니다. |
| `theme` | 아니요 | 아래 시각 효과 |

색상만 바꾸는 예시는 [midnight](midnight/skin.json), 시각 효과까지 쓰는 예시는 [space](space/skin.json)에 있습니다.

## 색 토큰

값은 `#RRGGBB` 또는 `#AARRGGBB`입니다. 목록에 없는 이름이 있으면 거부됩니다.

| 토큰 | 쓰임 |
|---|---|
| `Bg`, `OverlayBackdrop`, `ModalBackdrop` | 배경, 오버레이 배경, 모달 뒤 배경 |
| `Panel`, `Edge` | 패널 면과 테두리 |
| `Ink`, `Muted` | 기본 글자, 보조 글자 |
| `Mint`, `OnAccent` | 강조색, 강조색 위 글자 |
| `Danger`, `DangerSoft` | 오류와 삭제 |
| `Input`, `Selected`, `Hover` | 입력란, 선택, 마우스 올림 |
| `ScheduleNow` | 곧 끝나는 현재 일정 |
| `CalendarSunday`, `CalendarSaturday` | 달력의 일요일, 토요일 |

## theme

| 항목 | 범위 | 설명 |
|---|---|---|
| `background` | PNG 이름 | 배경 이미지 |
| `backgroundOpacity` | 0–1 | 배경 이미지 불투명도 |
| `stars` | 0–500 | 별 개수 |
| `nebulaSpeed` | 0–8 | 성운 속도, 0이면 끔 |
| `nebulaBrightness` | 0–1 | 성운 밝기 |
| `frame` | PNG 이름 | 위젯 틀(9-slice) 이미지 |
| `frameSlice` | 1–128 | 틀 모서리 크기. `frame`을 쓰면 필수이고, 이미지 가로·세로는 이 값의 2배보다 커야 합니다. |
| `framePadding` | 0–32 | 틀 안쪽 여백 |
| `panelMarkLength` | 0–32 | 패널 모서리 표시 길이 |
| `panelCornerRadius` | 0–24 | 패널 모서리 반경 |
| `tabCornerRadius` | 0–24 | 탭 막대 모서리 반경 |
| `voyage` | true/false | 우주 항해 배경 |
| `hudWidgets` | true/false | 위젯을 HUD 형태로 표시 |
| `world` | `{ "page": "world.html", "scenes": [...] }` | 스킨에 포함한 HTML 페이지를 실시간 배경으로 띄웁니다. `scenes`는 페이지에 그대로 전달되며 배경을 누를 때마다 다음으로 넘어갑니다. 배경을 누르면 `{ next: true, x, y }`가 전달되고, `x`·`y`는 화면 기준 0~1 비율의 누른 위치입니다. |
| `fonts` | `{ "body": { "file", "family" }, "heading": {...} }` | 스킨에 포함한 글꼴. `file`은 `.ttf`·`.otf` 이름, `family`는 글꼴 안의 이름입니다. |
| `hudStyle` | `tactical`, `soft` | HUD 위젯 모양. `soft`는 둥근 패널 형태입니다. `hudWidgets`와 함께 씁니다. |
| `widgets` | 형식 이름 목록, 32개까지 | 월드 페이지가 직접 그리는 위젯 상태 형식. `world`가 있어야 합니다. 아래 [위젯 레이어](#위젯-레이어)를 봅니다. |
| `options` | 목록 | 설정에 나타날 선택 항목. `id`(소문자·숫자·`-`, 32자까지), `name`이 필수이고 `description`, `default`를 쓸 수 있습니다. |

이미지는 스킨 폴더 바로 아래의 PNG만 씁니다. 이름은 영문 소문자·숫자·`_`·`-`이고, 한 변은 8192픽셀까지입니다.

## 3D 월드 페이지

페이지는 파일 하나이고 1.9MB까지입니다. 인터넷, 외부 파일, 사용자 기록에 접근할 수 없습니다. 앱이 잠금을 강제합니다.

- 받는 메시지: `chrome.webview`의 `message` 이벤트로 `{ scenes }`, `{ weather }`(`clear`, `partly`, `cloud`, `rain`, `snow`, `fog`), `{ visible }`, `{ next: true, x, y }`, 위젯 레이어를 쓰면 `{ widgets }`, `{ pointer }`, `{ actionResult }`
- 보내는 메시지: 시작할 때 `{ type: "loaded" }`, 준비되면 `{ type: "ready" }`, 실패하면 `{ type: "failed", message }`, 위젯 동작은 `{ type: "action", id, name, args }`
- 시각은 페이지에서 `Date`로 읽습니다.

월드 페이지는 코드이므로 이런 스킨은 바로 공개되지 않고 관리자 검토를 거칩니다.

## 위젯 레이어

월드 페이지는 배경뿐 아니라 위젯과 탭 막대도 그릴 수 있습니다(ADR 0109). 플러그인은 위젯 상태를 이름과 버전이 붙은 형식의 JSON으로 내보내고, 스킨은 `theme.widgets`에 자기가 그릴 형식을 적습니다.

```json
"theme": {
  "world": { "page": "world.html", "scenes": ["room"] },
  "widgets": ["facet.app/tabs@1", "facet.clock/time@1", "facet.weather/now@1"]
}
```

- 형식 이름은 `<pluginId>/<widget>@<version>`입니다. 정규식 `^[a-z0-9][a-z0-9._-]{0,63}/[a-z0-9][a-z0-9._-]{0,31}@[1-9][0-9]{0,3}$`, 중복 없이 32개까지이고 `world`가 없으면 스킨 전체가 거부됩니다. 상태 모양이 바뀌면 플러그인이 버전을 올리므로, 스킨은 아는 버전만 적습니다.
- 목록에 있는 형식을 내보내는 위젯은 페이지가 그립니다. 앱은 그 자리에 보이지 않는 타일을 두고(패널 없음), 위젯의 WPF 화면은 상태를 위해 살아 있지만 숨깁니다. 목록에 없는 위젯, 상태를 내보내지 않는 예전 플러그인은 기본 WPF 모양 그대로입니다. 월드 페이지가 실패하면 모든 위젯이 기본 모양으로 돌아옵니다.
- 페이지 위젯은 오버레이 창 아래에 있으므로 WPF 위젯과 겹치면 WPF 위젯이 위에 보입니다. 편집 틀, 모달, 아이콘 메뉴는 항상 앱이 그립니다. 동작이 여는 편집기도 앱의 모달입니다.
- 사용자 기록(일정 제목, 컨디션, 수면 시간 등)이 페이지로 넘어갑니다. 네트워크는 여전히 막혀 있고, 이런 스킨은 관리자 검토를 거칩니다.

### 받는 메시지

모든 좌표는 오버레이 창(= 월드 창) 기준 CSS 픽셀입니다. 같은 내용은 다시 보내지 않고, 준비(`ready`) 직후에 최신 목록을 한 번 보냅니다.

```json
{ "widgets": [
  { "id": "plugin-1a2b", "type": "facet.clock/time@1",
    "x": 120.5, "y": 80, "width": 240, "height": 120,
    "baseWidth": 192, "baseHeight": 192,
    "state": { "time": "09:41", "...": "..." } }
] }
```

- 지금 보이는 탭의 페이지 위젯 전체 목록입니다. 목록에 없는 위젯은 지웁니다. 탭 전환, 배치·이동·크기 변경(편집 중 끄는 동안 포함), 이동(pan)·확대, 창 크기, 상태 변경 때 다시 옵니다(최대 약 30회/초).
- `id`는 배치마다 다른 불투명한 문자열이고, 탭 막대는 언제나 `builtin.tabs`입니다.
- `baseWidth`·`baseHeight`는 위젯의 기본 크기입니다. 기본 크기로 그린 뒤 `width`·`height`에 맞춰 가로·세로를 따로 늘이면 WPF 타일과 같은 규칙이 됩니다(ADR 0108).
- `state`는 형식별 JSON 객체이고, 플러그인이 잘못된 값을 내면 `null`입니다.

```json
{ "pointer": { "id": "plugin-1a2b", "kind": "down", "x": 96.2, "y": 40, "delta": 0, "button": 0 } }
```

- 페이지 위젯 타일 위의 포인터입니다. `kind`는 `move`, `down`, `up`, `wheel`, `leave`입니다.
- `x`·`y`는 위젯 기본 크기 좌표(0~`baseWidth`, 0~`baseHeight`, 누른 채 끌면 벗어날 수 있음), `delta`는 휠 값(한 칸 120, 위로 양수), `button`은 0 왼쪽, 1 가운데, 2 오른쪽입니다.
- 편집 모드에서는 오지 않습니다. 페이지 위젯을 누르면 `{ next }`는 오지 않습니다.

```json
{ "actionResult": { "id": "plugin-1a2b", "name": "refresh", "ok": true, "result": null } }
```

- 보낸 동작마다 하나씩 옵니다. `ok`가 `false`면 `error`가 붙습니다: `unknown-widget`(지금 탭의 페이지 위젯이 아님, 편집 중), `bad-name`, `bad-args`, `bad-message`, `refused`(플러그인이 인자를 거부), `failed`.

### 보내는 메시지

```js
chrome.webview.postMessage({ type: "action", id: "plugin-1a2b", name: "record", args: { kind: "good" } });
```

- `id`는 마지막으로 받은 `widgets`에 있는 것이어야 합니다. `name`은 `^[a-z][A-Za-z0-9_-]{0,31}$`, `args`는 JSON 객체(생략하면 `{}`, 8KB까지)입니다. 검사와 저장은 플러그인이 합니다.

### 형식별 상태와 동작

시각은 모두 로컬 시각 `HH:mm`, 날짜는 `yyyy-MM-dd`입니다. 값이 없으면 `null`입니다.

| 형식 | 상태 | 동작 |
|---|---|---|
| `facet.app/tabs@1` | `{ tabs: [{ id, name, icon, selected }] }` — 숨기지 않은 탭을 순서대로. `icon`은 아이콘 이름(예: `Home`, `Calendar`), `selected`는 하나만 `true` | `select { id }` → `true` |
| `facet.clock/time@1` (시계), `facet.clock/weekday@1` (요일), `facet.clock/date-time@1` (날짜와 시계) | `{ time: "09:41", hour, minute, seconds, weekday: "화요일", weekdayIndex: 0~6 (0 일요일), date: "2026-11-17", dateLabel: "11월 17일", mode, style }` — 1초마다 바뀝니다. `mode`(`analog`·`digital`)와 `style`은 사용자가 고른 모양이고 날짜와 시계는 `null` | 시계·요일: `select { mode, style }` → `true`/`false` |
| `facet.weather/now@1` | `{ condition, label, temperature, humidity, place, status, loading, observedAt }` — `condition`은 `clear`, `partly`, `cloud`, `fog`, `rain`, `snow`, `night`(맑은 밤) 또는 아직 모르면 `null`, `label`은 "맑음" 같은 한국어, `temperature`는 섭씨 소수 첫째 자리, `humidity`는 0~100, `status`는 "09:30 기준" 같은 줄, `loading`은 불러오는 중 | `refresh {}` → `null` (끝나면 새 상태가 옵니다) |
| `facet.schedule/agenda@1` | `{ previous, current, next }` — 각각 `null` 또는 `{ id, title, start, end, minutesLeft, ending, memo }`. `minutesLeft`는 진행 중일 때 종료까지 남은 분(아니면 `null`), `ending`은 10분 이내 종료 | 없음 |
| `facet.condition/current@1` | `{ kinds: [{ id, label, color }], current, currentLabel }` — `kinds`는 `good`, `okay`, `tired`, `sleepy`, `unwell`, `stressed` 순서, `color`는 `#RRGGBB` | `record { kind }` → `true`/`false` |
| `facet.condition/history@1` | `{ days: [{ date, label, level, kind, isToday }], spans: [{ kind, from, to }] }` — 오늘까지 7일. `level`은 그날 가장 길었던 컨디션의 `kinds` 순서 + 1(1~6), 기록이 없으면 0. `spans`는 7일 전체를 0~1로 본 구간 | `edit { date }` → 그날 마지막 기록의 편집기를 포인터 위치에 엽니다. 기록이 없으면 `false` |
| `facet.sleep/week@1` | `{ days: [{ date, label, hours, recorded, isToday }] }` — 오늘(기상일)까지 7일. `hours`는 소수 첫째 자리, 계산할 수 없으면 `null` | `edit { date }` → 수면 기록 모달을 엽니다. 미래 날짜는 `false` |

## 미리 확인

폴더를 `%LOCALAPPDATA%\MaterialStone\DailyDock\skins\` 아래에 넣고 Facet 설정의 스킨 목록에서 선택합니다.

## 올리기

1. Facet 설정 → 플러그인 → **스킨·플러그인 받기** → **올리기**에서 스킨 폴더를 고릅니다.
2. zip이 만들어지고 [제출 양식](https://github.com/material-stone/facet/issues/new?template=submit.yml)이 열립니다. GitHub에 로그인합니다.
3. zip 하나를 양식에 끌어다 놓고 제출합니다.

검사를 통과하면 이슈가 닫히며 공개됩니다. 실패하면 이슈 댓글의 실행 기록에서 이유를 볼 수 있습니다. zip을 직접 만든다면 `skin.json`이 zip의 맨 위에 있어야 합니다.

## 업데이트

`version`을 올려 같은 방법으로 제출합니다. 같은 `id`는 처음 올린 계정만 갱신할 수 있고, 이미 공개한 버전은 다시 올릴 수 없습니다.
