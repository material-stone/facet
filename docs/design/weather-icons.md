# 대시보드 날씨 아이콘

2026-10-24. 생성 방식은 **내장 image_gen/imagegen**이며 CLI/API fallback은 사용하지 않았다. 실제 alpha가 있는 정사각 PNG를 생성하고 원본 PNG를 그대로 프로젝트에 복사했다. 앱은 화면 표시용으로만 작게 디코딩하며 생성 파일을 다시 가공하지 않는다.

파일은 모두 `impls/Facet/DashboardWeather/Icons/`에 있고 같은 module의 `WeatherIcons`만 로드한다. WPF Resource로 배포하므로 생성 기본 폴더에 의존하지 않는다. 현재 날씨의 WMO code와 is_day로 선택한다.

| 파일 | 표시 상태 |
| --- | --- |
| sunny.png | 맑음·대체로 맑음, 낮 |
| clear-night.png | 맑음·대체로 맑음, 밤 |
| partly-cloudy.png | 구름 조금, 낮 |
| partly-cloudy-night.png | 구름 조금, 밤 |
| cloudy.png | 흐림 |
| fog.png | 안개 |
| rain.png | 비·이슬비·어는 비·소나기 |
| snow.png | 눈·눈 소나기 |
| thunder.png | 뇌우·우박 동반 뇌우 |

알 수 없는 상태는 아이콘을 표시하지 않는다. 아이콘과 함께 날씨 설명·섭씨 온도·상대 습도·자료 기준 시각을 표시한다. 출처는 관련 ADR에 유지하며 날씨 행에는 표시하지 않는다. 습도는 공통 벡터 Icon의 Droplet과 퍼센트를 함께 사용한다.

## 공통 프롬프트

아래 템플릿의 Subject만 표에 따라 바꾸어 각 이미지를 별도로 생성했다. 모두 `transparent_background=true`를 사용했다.

```text
Use case: stylized-concept. Asset type: a single production-ready 2D weather UI icon for a Korean desktop dashboard, readable at 56px. Subject: {subject}. Style: simple and intuitive cute rounded weather mascot, clean pastel vector-like bitmap, softly shaded chunky forms, crisp smooth edges, tiny charcoal dot eyes and a very small curved mouth, understated adorable expression, no outer sticker border. Palette: warm pastel yellow, ivory, pastel sky blue, soft blue-gray, tiny charcoal face details. Designed to remain readable on dark charcoal teal. Actual background must be fully transparent. Square canvas, complete icon perfectly centered, whole symbol fits within central 72% of canvas with generous transparent padding. No text, no letters, no numbers, no watermark, no scene, no extra decorations, no grid or contact sheet. One isolated cohesive weather icon only.
```

| 파일 | Subject |
| --- | --- |
| partly-cloudy-night.png | a pale golden crescent moon with a tiny peaceful smiling face and two small warm stars; clearly night-time weather |
| partly-cloudy.png | a warm pastel yellow sun peeking from behind one small soft ivory cloud with a tiny cheerful face; clearly partly cloudy weather |
| cloudy.png | one soft rounded ivory and pale blue cloud, three rounded lobes, a tiny friendly face; clearly overcast weather |
| fog.png | one soft rounded pale gray-blue cloud with a tiny gentle face and three short rounded horizontal mist bars beneath it; clearly foggy weather |
| rain.png | one soft rounded ivory and pale blue cloud with a tiny calm face and three large simple blue raindrops beneath; clearly rainy weather |
| snow.png | one soft rounded ivory and pale blue cloud with a tiny friendly face and two simple large six-point pastel blue snowflakes beneath; clearly snowy weather |
| thunder.png | one soft rounded muted blue-gray cloud with a tiny determined but cute face and one chunky rounded golden lightning bolt beneath, one little blue raindrop; clearly thunderstorm weather |

밤의 첫 생성물에 구름이 포함되어 구름 조금의 밤 아이콘으로 보존했다. 맑은 밤은 아래 편집 프롬프트로 구름을 제거해 별도 PNG로 저장했다.

## 맑은 낮 프롬프트

```text
Use case: stylized-concept. Asset type: a single production-ready 2D weather UI icon for a Korean desktop dashboard, readable at 56px. Subject: SUNNY — a warm golden yellow circular sun with 8 short chunky rounded rays, tiny dark dot eyes and a subtle cheerful curved smile, very simple and intuitive, adorable but understated. Style: clean flat pastel vector-like bitmap, soft round silhouettes, very restrained shading, crisp smooth edges, no outer sticker border, no text, no letters, no numbers, no watermark, no scene, no decorations. Palette: warm pastel yellow and amber, tiny charcoal face details, readable on a dark charcoal teal background. Actual background must be fully transparent. Square canvas, sun perfectly centered, whole symbol fits within the central 72% of canvas with generous transparent padding, consistent rounded shape language. Only one isolated sun icon; no grid or contact sheet.
```

## 맑은 밤 편집 프롬프트

참조 대상은 위 밤 아이콘의 첫 생성물이며 투명 배경을 보존했다.

```text
Edit this weather UI icon for CLEAR NIGHT: remove every cloud entirely. Keep only the same adorable pale golden crescent moon with its tiny peaceful smiling face and the same two small golden stars. Preserve the moon's color, soft simple rounded style, face and crisp silhouette. Recenter the complete moon and stars together with generous transparent padding. No clouds, no background, no text, no extra elements. Actual fully transparent background. Single square icon, readable at 56 pixels.
```
