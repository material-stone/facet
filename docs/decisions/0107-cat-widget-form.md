# ADR 0107: 고양이 위젯 형태 (`hudStyle: cat`)

> **대체됨:** [0109](0109-skin-widget-layer.md)가 대체한다. 위젯 외형은 스킨의 월드 페이지가 그린다. `hudStyle: cat`, 고양이 그리기 도구(`CatParts`)와 플러그인의 고양이 분기는 지웠고, `WidgetTheme.IsCatHud`·`CatHudKey`·`WidgetDescriptor.HasCatPanel`은 예전 플러그인 호환을 위해 `[Obsolete]`로 남아 아무 효과가 없다.

2026-11-16: 고양이 방 스킨([0106](0106-world-click-position-and-cat-room.md))의 위젯을 방과 어울리는 소품 모양으로 그린다.

## 결정

- 테마 슬롯 `hudStyle`에 `cat`을 더한다. 앱은 리소스 `Facet.CatHudWidgets`를 켜고, 플러그인은 `WidgetTheme.IsCatHud`로 고양이 형태를 고른다.
- `cat`은 소프트 형태의 한 종류다. `IsSoftHud`도 참이라, 고양이 형태가 없는 위젯(타사 플러그인, 예전 버전)은 소프트 형태로 보인다. 그래서 설치된 예전 플러그인과 호환된다.
- 고양이 형태는 각 플러그인의 C# 코드다. 시계는 고양이 얼굴과 꼬리 시계추, 날씨는 매달린 나무 팻말, 일정은 코르크 보드와 포스트잇, 컨디션은 고양이 얼굴 토큰과 츄르 튜브, 수면은 방석 위에서 자는 고양이다. 데이터와 동작은 그대로이고 모양만 바뀐다.
- 그리기 도구(`Facet.Widgets.Cat`, `CatTag`, `CatClock`, `CatSign`)는 `Facet.Plugins`에 둔다. `Hud.Header`는 고양이 형태에서 크림색 이름표가 된다. 애니메이션은 보이는 동안만 도는 `DispatcherTimer`다.
- `WidgetDescriptor.HasCatPanel`(기본 `true`)이 `false`인 위젯은 고양이 형태에서 `HudPanel`을 받지 않는다. 자기 소품을 그리는 1st-party 위젯은 모두 `false`이고, 나머지는 크림색 판에 굵은 갈색 외곽선과 그림자를 받는다.
- 고양이 형태의 색은 이 형태 전용 고정 팔레트다. 스킨 토큰은 글자와 패널 등 공용 부분에만 쓴다.

## 결과

- 고양이 방 스킨을 고르면 위젯이 방의 소품처럼 보인다. 다른 스킨에는 영향이 없다.
- 형태를 바꾼 플러그인은 버전을 올려 다시 배포해야 사용자에게 보인다.
