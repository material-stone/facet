# Graphify 색인 복구와 검증

`graphify-out/`은 재생성 가능한 로컬 색인이다. 앱 소스와 문서가 근거이며 색인이 소스를 대신하지 않는다.

## 명령

저장된 Graphify Python 환경을 사용한다. 별도 API 키나 외부 호출은 필요하지 않다.

```powershell
$graphifyPython = (Get-Content -Raw graphify-out/.graphify_python).Trim()
& $graphifyPython scripts/graphify-maintenance.py diagnose
& $graphifyPython scripts/graphify-maintenance.py refresh
& $graphifyPython scripts/graphify-maintenance.py verify
& $graphifyPython scripts/test-graphify-maintenance.py
graphify query "OverlayNoteMarkdown" --budget 1200
```

`diagnose`는 새 캐시로 전체 구조를 추출하고 원본/복구 추출 및 integrity report를 쓰지만 최종 graph.json을 교체하지 않는다. `refresh`는 구조 전체와 현재 hash에 맞는 의미 추출을 합쳐 JSON·HTML·보고서·manifest를 재생성한다. `verify`는 저장된 canonical graph의 endpoint·edge key·hyperedge 연결을 확인한다.

## 의미 추출

`graphify-out/semantic-extractions.json`은 문서·이미지별 내용 MD5, extraction prompt SHA256, 실제 노드·관계를 보관한다. 비어 있는 cache entry는 완료로 취급하지 않는다. 변경되거나 아직 처리되지 않은 문서가 있으면 일반 `refresh`는 중단하며 누락 목록을 표시한다.

Graphify 스킬의 로컬 semantic agent에 해당 파일을 맡기고, 읽기 전 내용 MD5와 구조화된 JSON을 `graphify-out/`에 저장한다. 소스 경로는 agent에게 전달된 절대 경로 그대로 사용한다. 다음 명령은 내용이 바뀌지 않았고 실제 노드가 있는 파일만 받아들인다.

```powershell
& $graphifyPython scripts/graphify-maintenance.py import-semantic `
  --fragment graphify-out/semantic-fragment.json `
  --hashes graphify-out/semantic-source-hashes.json
```

hash JSON은 `{"절대 파일 경로":"읽기 전 MD5"}` 형식이다. 누락·변경 파일은 agent로 다시 추출해야 한다. 의도적으로 부분 색인을 만들 때만 `refresh --allow-partial`을 사용하며 미처리 파일은 `.needs_update`와 보고서에 남는다.

## 원인과 보존 방식

- C# `using System.*` 등의 외부 import는 추출기가 edge만 만들고 target node는 만들지 않았다. source line을 확인한 외부 namespace/dependency 노드를 추가하며 정의가 저장소 안에 있다는 주장은 하지 않는다.
- NuGet import는 해당 C# 소스에서 가장 가까운 실제 `.csproj`의 `PackageReference`로 증명한다. 다른 프로젝트에 같은 패키지가 있어도 중복으로 오판하거나 그 참조를 근거로 사용하지 않는다. 빌드 중 WPF가 원래 프로젝트 옆에 만드는 `*_wpftmp.csproj`와 `obj`/`bin` 프로젝트는 corpus·패키지·Windows SDK·extern alias 근거에서 제외한다. 임시 프로젝트에 복사된 Markdig 참조가 둘 이상으로 집계돼 갱신이 실패하던 문제를 방지하며 확인할 수 없는 외부 참조는 계속 거절한다.
- `Launcher::Facet.Deployment.ReleaseManifest`는 실제 `extern alias`, 프로젝트의 alias reference와 로컬 선언을 함께 확인해 로컬 타입에 연결한다.
- `Brush()`·`Action()` 호출이 동명 타입 stub에 잘못 연결되면 같은 파일의 유일한 callable과 실제 호출 line을 확인해 수정한다. 증거가 충분하지 않은 target은 만들어내지 않고 복구를 실패시킨다.
- XAML 추출 cache가 변경된 code-behind의 옛 위치를 재사용할 수 있으므로 매번 비어 있는 임시 cache에서 전체 구조를 추출한다. 임시 cache는 `graphify-out/` 내부에서 생성·정리한다.
- 기존 단순 무방향 그래프는 서로 다른 source line·relation·반대 방향을 같은 edge로 덮어썼다. canonical graph.json은 방향 있는 MultiDiGraph로 저장하며 모든 evidence에 안정적인 key를 부여한다. 같은 evidence의 중복 생산만 `extraction_occurrences`로 합산한다. 커뮤니티 계산용 단순 projection은 저장 원본을 대체하지 않는다.

## 확인할 산출물

- `raw-extraction.json`: 수정 전 추출 증거
- `repaired-extraction.json`: source 검증에 따른 endpoint 수정과 evidence key
- `integrity-report.json`: 수정 건수, 원래 단순 그래프의 손실 후보, 실제 보존 건수와 semantic coverage
- `graph.json`, `graph.html`, `GRAPH_REPORT.md`: 질의·시각화·감사 결과

Graphify upstream의 일반 simple-graph 재빌드는 evidence를 다시 축약할 수 있으므로 이 프로젝트에서는 위 유지보수 명령으로 색인을 생성한다. `graphify query`와 HTML 읽기는 canonical multigraph를 지원한다. 자동으로 완전한 C# 컴파일러 해석을 수행하는 것은 아니며, source 검증으로 확인할 수 없는 import나 불명확한 ID 충돌은 수동 점검이 필요하다.
