# 플러그인 작성

플러그인은 `Facet.Plugins`만 참조하는 .NET 10 WPF 클래스 라이브러리입니다. 예제는 `impls/Facet.SamplePlugin`, 타이머를 쓰는 실사용 예는 `impls/Facet.ClockPlugin`(요일 시계), 네트워크·내장 리소스·위젯별 저장을 쓰는 예는 `impls/Facet.WeatherPlugin`(날씨)에 있습니다.

```csharp
public sealed class MyPlugin : IFacetPlugin
{
    public void Register(IWidgetRegistry registry) =>
        registry.AddWidget(new WidgetDescriptor("hello", "인사", _ => new TextBlock { Text = "안녕하세요" }));
}
```

`<데이터 폴더>/plugins/<폴더 이름>/`에 DLL과 `plugin.json`을 둡니다. `Facet.Plugins.dll`은 넣지 않습니다.

```json
{ "id": "my.plugin", "name": "내 플러그인", "version": "1.0.0", "assembly": "MyPlugin.dll" }
```

설정 → 탭·플러그인에서 켠 뒤 대시보드 편집 모드의 위젯 추가에서 배치합니다. 같은 줄의 삭제 버튼으로 지울 수 있고, **데이터까지 삭제**를 고르면 plugin-data와 설정도 함께 지워집니다.

- `id`는 영문 소문자·숫자·`.`·`_`·`-`로 64자까지, `assembly`는 같은 폴더의 파일 이름만 허용합니다.
- DLL은 메모리로 불러오므로 앱을 켠 채 교체할 수 있습니다. 켜 둔 플러그인은 앱을 시작할 때 바뀐 파일을 자동으로 불러오고, 실행 중에 바로 적용하려면 설정에서 플러그인 행의 **다시 불러오기**(갱신 아이콘)를 누릅니다. 플러그인을 끄면 이 버튼과 설정 접기/펼치기는 비활성화됩니다. 이전 어셈블리는 앱을 다시 시작할 때까지 메모리에 남습니다. `Assembly.Location`은 비어 있으니 파일 경로는 `WidgetContext.StorageDirectory`를 쓰세요.
- 위젯 뷰가 `IDisposable`이면 제거·플러그인 해제·앱 종료 때 호출합니다. 타이머는 `IsVisible`이 true일 때만 돌리세요.
- 코드로 UI를 만드세요. 별도 load context에서는 XAML의 pack URI 리소스가 해석되지 않을 수 있습니다.
- 위젯의 UI 스레드 작업은 앱을 멈출 수 있으니 오래 걸리는 작업은 백그라운드에서 처리하세요.

## 모달, 수명, 에이전트 도구

- **모달:** `context.Modal.Show(뷰, 옵션)`으로 화면 전체를 덮는 모달을 띄웁니다. 내용은 플러그인이 가지고 앱은 표시와 닫기 규칙만 맡습니다. `CanClose`가 false면 Esc·바깥 클릭·숨김·앱 종료가 멈춥니다. 실패한 저장의 초안을 지키는 예는 `impls/Facet.SleepPlugin`입니다.
- **수명:** 플러그인은 켜져 있는 동안 한 번만 등록됩니다. `Register`에서 `registry.Directory`(코드 폴더), `registry.DataDirectory`(내려받은 파일처럼 플러그인 교체·삭제 후에도 남길 파일), `registry.Settings`를 쓸 수 있고, `IDisposable`을 구현하면 끄기·다시 불러오기·앱 종료 때 호출됩니다. 위젯보다 오래 사는 일은 여기에 둡니다.
- **자체 저장:** `Settings.Save(키, 값)`은 설정 입력란 없이 플러그인이 관리하는 값을 앱 기록에 저장합니다. 값은 65,536자까지입니다.
- **전용 저장소:** `registry.Store`의 `Read()`·`Write(텍스트)`는 `<데이터 폴더>/plugin-data/<id>.dat`에 전체 텍스트를 암호화해 원자적으로 저장합니다. 크기 제한이 없고 플러그인 폴더를 지워도 남으니 계속 쌓이는 사용자 기록에 씁니다. 파일을 읽을 수 없으면 `Read`가 예외를 던지므로 덮어쓰지 말고 그대로 전파하세요. 실제 사용 예는 `impls/Facet.ConditionPlugin`([ADR 0081](../decisions/0081-condition-plugin-and-store.md))과 `impls/Facet.SleepPlugin`([ADR 0083](../decisions/0083-sleep-plugin-and-store.md))입니다.
- **에이전트 도구:** `registry.AddAgentTool(new(이름, 설명, JSON 스키마, 실행 함수))`로 등록하면 연결된 에이전트가 쓸 수 있습니다. 새 에이전트 세션부터 보입니다. 실제 사용 예는 `impls/Facet.MusicPlugin`입니다.

## 탭·위젯 조회, 데이터, 설정

- **조회:** `context.Workspace`로 `Tabs`, `WidgetsInTab(탭 ID)`, `Widget(탭 ID, 배치 ID)`, `WidgetsById(WidgetIds.Of(플러그인 ID, 위젯 ID))`를 씁니다. 같은 위젯도 탭이 다르면 배치 ID와 데이터가 다른 별개의 위젯입니다. 탭 ID는 설정의 탭 행 툴팁에서 볼 수 있습니다.
- **데이터:** `UsesData = true`를 선언하면 앱이 배치마다 저장소를 만들어 `context.Data`로 줍니다. 값은 앱 기록에 암호화되어 저장되고 위젯을 지우면 함께 지워집니다. 다른 위젯의 데이터는 `PlacedWidget.Data`로 읽기만 할 수 있습니다. 쓰기마다 전체 기록을 저장하니 자주 쓰지 마세요.
- **배치 규칙:** `AllowMultiple`은 한 탭 안의 복수 배치, `AllowMultipleTabs`는 여러 탭 배치입니다.
- **패널:** `HasPanel = true`면 앱이 현재 스킨의 패널로 위젯을 감쌉니다. 기본값 false는 내용만 표시합니다.
- **설정:** `registry.AddSettings(new PluginSettingsSection([...]))`로 `SliderSetting`, `ColorSetting`, `TextSetting`, `TextListSetting`, `ChoiceSetting`, `ActionSetting`을 선언하면 설정 → 플러그인에 구역이 생깁니다. 선언하지 않으면 구역이 없습니다. 값은 `context.Settings`로 읽고 `Changed`로 변경을 받습니다. 구독은 `Dispose`에서 해제하세요.

## 앱 연결 (ADR 0084)

- **셸:** `registry.Shell`로 데이터 폴더, `Facet.Mcp.exe` 경로, 화면 공유 모드를 읽고 오버레이 숨김, 위젯이 있는 탭 열기(`ShowWidget`), 내 설정 열기, 오류 알림, 단축키 변경(`SetHotkey`)을 요청합니다.
- **런처:** `AddLauncherMode`로 입력이 일치하는 동안 입력란 위·아래에 컨트롤을 두고 Enter를 받습니다. 메뉴는 `ILauncherHost.ShowFlyout`을 쓰면 앱이 바깥 클릭과 Esc를 처리합니다.
- **단축키·트레이:** `AddHotkey`, `AddTrayCommand`. 플러그인을 끄면 함께 사라집니다. 저장한 단축키 변경은 다음 등록 때 `Shortcut`으로 넘기세요.
- **설정 필드:** `Group`으로 필드를 묶고, `ToggleSetting`, 저장 전 검사를 하는 `TextSetting.Check`, 직접 만든 뷰인 `CustomSetting`을 쓸 수 있습니다.
- **네이티브 DLL:** 플러그인 폴더에 함께 두면 찾아 줍니다. 실제 사용 예는 `impls/Facet.ChatPlugin`입니다.

플러그인은 앱과 같은 권한으로 실행되므로 신뢰하는 코드만 켜세요. 경계와 한계는 [ADR 0073](../decisions/0073-widget-grid-and-plugins.md)을 참고합니다.

## 스킨 만들기

코드 없이 `<데이터 폴더>/skins/<폴더 이름>/skin.json`만 두면 설정의 스킨 목록에 나타납니다. 쓰지 않은 색은 기본 스킨을 따릅니다.

```json
{
  "id": "my-skin", "name": "내 스킨", "version": "1.0.0", "description": "내 스킨 설명",
  "colors": { "Bg": "#06141A", "Panel": "#DE0B1F26", "Ink": "#E8FFF6", "Mint": "#6CF2C2" },
  "theme": { "background": "sky.png", "backgroundOpacity": 0.6, "stars": 120 }
}
```

- **색 토큰:** `Bg`, `OverlayBackdrop`, `ModalBackdrop`, `Panel`, `Edge`, `Ink`, `Muted`, `Mint`, `OnAccent`, `Danger`, `DangerSoft`, `Input`, `Selected`, `Hover`, `ScheduleNow`, `CalendarSunday`, `CalendarSaturday`. `#RRGGBB` 또는 `#AARRGGBB`입니다.
- **`theme` 슬롯:** `background`, `backgroundOpacity`, `stars`, `frame`, `frameSlice`, `framePadding`, `nebulaSpeed`, `nebulaBrightness`, `panelMarkLength`, `panelCornerRadius`, `tabCornerRadius`, `voyage`, `hudWidgets`, `options`. 범위는 [ThemeVisuals.cs](../../impls/Facet/Design/ThemeVisuals.cs)가 검사합니다.
- **이미지:** 같은 폴더의 PNG만 쓰며 이름은 영문 소문자·숫자·`_`·`-`, 한 변은 8192픽셀까지입니다.

## 배포

플러그인과 스킨 모두 설정 → 플러그인 → 스킨·플러그인 받기에서 처리합니다.

- **목록:** 설정을 열면 공개 목록이 보이고, **적용**을 누르면 내려받아 바로 적용합니다.
- **zip:** 파일로 설치할 수도 있습니다. 이때 플러그인은 직접 켜야 합니다.
- **올리기:** **올리기**로 폴더를 고르면 zip을 만들고 GitHub 제출 양식을 엽니다. 파일을 끌어다 놓으면 스킨은 자동 검사 후, 플러그인은 관리자 승인 후 공개됩니다. 올릴 때 `version`을 올려야 하고, 같은 ID는 처음 올린 계정만 갱신합니다.

규칙과 한계는 [ADR 0088](../decisions/0088-packages-and-registry.md)을 참고합니다.
