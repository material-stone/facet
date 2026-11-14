# 실제 오버레이 캐러셀 드래그 성능 — 2026-10-24

보관 기록: 아래 결과는 이전 세 항목 캐러셀 구성의 측정이다. 현재 관리 화면은 달력 하나만 남아 드래그 전환을 제공하지 않으며 캐러셀 전용 성능 명령은 제거했다. 아래 재현 설명은 당시 소스를 기준으로 한다.

## 측정 범위

Release JournalChecks가 합성 할 일 20개와 마크다운 노트가 있는 실제 OverlayWindow를 주 모니터 전체에 표시했다. native 마우스 버튼을 누른 채 별도 입력 스레드에서 8ms 간격의 좌표를 보내며 각 조건을 5초 측정했다. 약 0.65Hz 왕복 드래그이며 mouse-up 스냅 시간은 표에서 제외했다. 3440×1440 물리 픽셀, DPI 150%, WPF Tier 2, 창 2293.33×960 DIP다. 선택 항목은 이전 680×425에서 1164.8×728 DIP로 커졌다. 프레임은 화면 밖 RenderTargetBitmap이 아닌 보이는 실제 창에서 수집했다.

FPS는 중복 RenderingTime을 제외한 **WPF Rendering 콜백 빈도**다. 프레임 시간은 QPC로 측정한 인접 콜백 간격이며 CPU 작업 시간이 아니다. PresentMon 2.6.0의 대상 PID 추적은 이벤트가 없었고, 전체 이벤트 추적에서도 1250개 모두 다른 PID였다. 따라서 실제 디스플레이 표시 FPS·GPU 시간·입력-to-photon 지연은 확정하지 못했다. 다른 PID의 값을 앱 성능으로 사용하지 않았다. PresentMon 원리와 지표 정의: [공식 설명](https://github.com/GameTechDev/PresentMon), [콘솔 지표](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md).

## 결과

| 조건 | UI 갱신 FPS | 평균 간격 ms | p95 ms | 최대 ms | transform 평균 ms | UI CPU ms/5초 | 프로세스 CPU ms/5초 |
|---|---:|---:|---:|---:|---:|---:|---:|
| 블러+그림자 (첫 측정) | 58.62 | 17.03 | 20.79 | 44.77 | 0.020 | 187.50 | 2296.88 |
| 그림자만 제외 | 59.14 | 16.90 | 20.84 | 44.62 | 0.017 | 218.75 | 1562.50 |
| 블러만 제외 | 58.33 | 17.14 | 20.75 | 43.40 | 0.015 | 171.88 | 1218.75 |
| 블러+그림자 (마지막 반복) | 58.56 | 17.03 | 20.29 | 46.32 | 0.016 | 140.62 | 1421.88 |

각 조건에서 레이아웃 재계산 0회, 그림자 atlas 재생성 0회, Gen0/1/2 GC 0회였다. UI 할당은 조건별 약 2.25~2.28MB이며 측정용 프레임 레코드·리스트 비용을 포함하므로 제품 단독 할당량으로 보지 않는다. 마지막 정상 반복에서 33.33ms 초과 간격은 293개 중 3개였다. 전체 프로세스 CPU는 모든 스레드의 CPU 시간 합이고 GPU 시간이 아니다.

이전 작은 레이아웃의 동일 native 드래그 기준은 58.17 FPS / 평균 17.17ms / p95 21.87ms / 최대 48.07ms였다. 확대 후에도 비슷한 갱신 빈도를 유지했다. 짧은 표본과 실행 시점 차이 때문에 작은 수치 차이를 성능 향상으로 단정하지 않는다.

## 병목 해석

- 드래그 transform은 평균 0.016ms, p95 0.024ms 수준이다(마지막 정상 반복). UI CPU는 5초 중 140.63ms로 한 코어 시간의 약 2.8%다. 레이아웃·저장·마크다운 재구성·그림자 재생성이 매 프레임 반복되는 병목은 관찰하지 않았다. 배경 캡처 worker는 첫 프레임 후 정지하므로 드래그 중 주기적 캡처가 없다.
- 프로세스 CPU는 마지막 반복에서 1421.88ms/5초, 한 코어 시간의 약 28.4%였다. UI 외 WPF/native 렌더링 등에 비용이 남는다. 첫 정상 측정 2296.88ms와 차이가 커서 준비·캐시·스케줄링 등의 순서 효과가 섞여 있다. 이 비교만으로 그림자 또는 블러의 CPU 비용을 정확히 분리할 수 없다.
- 효과를 하나씩 제외해도 갱신은 약 58~59 FPS이고 p95는 약 20~21ms였다. 특정 효과가 현재 갱신 빈도를 제한한다는 증거는 없다. 효과를 유지한다.
- 최대 약 43~46ms의 간헐 지연은 남는다. GPU/디스플레이 이벤트가 앱에 귀속되지 않아 WPF 렌더 스레드, Windows 합성, 스케줄링 중 원인을 확정하지 못했다. 지속적인 UI 포화는 관찰하지 않았지만 모든 병목이 제거됐다는 뜻은 아니다.

## 재현·원본

프로젝트의 .dotnet을 DOTNET_CLI_HOME으로 지정하고 Release JournalChecks를 빌드한 뒤 `dotnet run --project impls/Facet.JournalChecks -c Release --no-build --no-launch-profile -- --carousel-live-profile`을 실행한다. 같은 사용자 데스크톱과 ETW 접근이 필요하다. 다른 UI 검사나 빌드를 동시에 실행하지 않는다. PresentMon 서명·공식 배포 SHA256을 확인한 독립 실행 파일은 `.test-data/tools/`에 두며 앱 의존성에는 추가하지 않는다. 진단 중 제외한 효과와 커서는 finally에서 복구한다. 픽셀은 저장하지 않는다.

- 이전 기준: `.test-data/journal-721e5413a57546a887dc4caa0b02b693/carousel-live-profile.json`
- 확대 후 독립 측정: `.test-data/journal-7d31f555767f4b8587389c0954cefdfe/carousel-live-profile.json`
- 최종 4조건 반복·CPU·ETW: `.test-data/journal-0aaeead65c4d405280ee16b6b911c3ef/`의 `carousel-live-profile.json`, `presentmon.csv`, `presentmon-output.txt`
- 최종 native 검사: `.test-data/carousel-final-native.log` (20개 통과)
- 중간 한 실행은 다른 UI 검사가 포커스를 가져가 두 번째 조건 시작 시 중단됐다. 이 실행은 비교에서 제외하고 검사를 마친 뒤 독립 재실행했다.

## 구현 검증

최대 폭 1280 DIP·가용 폭 82%·이웃 86% 배율, 대칭 여백과 그림자 공간을 적용했다. 드래그·스냅에서 반대편으로 순환하는 제3항목을 숨겨 목적 위치로 옮긴 뒤 완료 시 표시한다. 방향 반전·짧은 드래그·저장 거절·전환 중단도 검증했다. 전경의 BitmapCache/Effect는 없으며 기존 그림자와 블러를 유지했다.

`scripts/build.ps1 -OverlayUi`: 배포 보존 4개, Core 57개, WPF/통합 508개 통과. 크기 1440×900 / 800×600 / 480×360의 중앙 정렬·부분 이웃·전경 픽셀·그림자 재사용과 기존 편집·포커스·단축키를 포함한다. QA `.test-data/journal-ff3d9d36365c483fb0abad694669200b/`의 큰/작은 화면 렌더를 시각 검토했다. 빌드 오류 0개, NU1900 취약성 메타데이터 조회 경고는 외부 NuGet 연결 문제로 남았다. 환경 validator PASS, architecture 명령은 null이다.

배포: `artifacts/Facet/releases/1.13.1-20261024-135418-838f731d/`. 검증에 사용한 Release Desktop DLL과 배포 DLL SHA256이 일치한다. latest.json을 원자적으로 갱신했으며 실행 중인 사용자 앱·기록은 변경하지 않았다.
