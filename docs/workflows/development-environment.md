# Development Environment Workflow

## Initial setup

project root에서 `$bootstrap-development-environment`를 실행합니다. 생성된 contract를 검토하고 `TBD` 값을 채우며 중요한 대안은 ADR로 기록합니다.

### Native commands

- `setup`: `dotnet restore impls/Facet/Facet.csproj`
- `test`: `./scripts/check.ps1` — 기본 검사는 화면을 띄우지 않는다. 추가 옵션은 [변경 범위별 검증](testing.md)을 따른다.
- `build/publish`: `./scripts/build.ps1`
- `lint`: 별도 도구 없이 C# 컴파일러 진단 사용
- `architecture`: Core가 WPF/WinForms를 참조하지 않는 프로젝트 경계와 Graphify 소스 인덱스로 검토

Graphify가 설치되어 있으면 다음 순서로 Codex에 등록하고 initial graph를 만듭니다.

```text
graphify extract impls --code-only --out . --force --max-workers 2
```

좁은 탐색에는 `graphify query "<question>" --budget 1200`을 사용합니다. 현재 그래프는 구현 소스만 포함하고 사업·건강 문서는 포함하지 않습니다. 구현 변경 후 위 명령으로 갱신합니다. 전역 스킬 재설치는 필요하지 않습니다.

## New feature

1. `AGENTS.md`, docs index, architecture index, contract, related ADR을 읽습니다.
2. 영향받는 boundary와 impact path를 Graphify로 좁게 query합니다.
3. 새 boundary를 만들기 전에 기존 interface를 확장하거나 contract를 갱신합니다.
4. 가장 작은 호환 변경을 구현하고 validation, security, error, accessibility를 보존합니다.
5. 선언된 native test와 architecture command를 실행합니다.
6. behavior 또는 structure가 바뀌면 docs/ADR/contract를 갱신하고 Graphify를 update합니다.

## Debugging

먼저 failure를 재현하거나 격리합니다. boundary를 바꾸기 전에 실제 call/data path를 추적합니다. 원인이 있는 가장 작은 component를 수정하고 가능한 경우 regression test를 추가합니다.

## Asset changes

asset은 loading과 validation을 소유한 feature 또는 module과 함께 둡니다. manifest 또는 loader interface가 있으면 갱신합니다. 명시적인 contract와 rationale 없이 global asset registry를 만들지 않습니다.
