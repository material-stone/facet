# 고양이 방 홍보 영상

Facet 배경 스킨 "고양이 방"(`registry/skins/catroom`)의 짧은 홍보 영상(약 47초)입니다. 내레이션 없이 음악과 화면 자막만 씁니다.
화면은 실제 스킨 페이지 `world.html`을 headless Chrome에서 돌려 녹화한 것이고, 그 위에 "종이 이름표" 스타일 자막을 얹습니다.

## 준비물

- Node.js 20+
- Google Chrome (Playwright가 `channel: "chrome"`으로 씁니다. 브라우저를 따로 받지 않습니다)
- `npm install`

## 만들기

```bash
npm run prepare-all       # timeline → fonts → sfx → music → capture (녹화는 몇 분 걸림)
npm run typecheck
npm run render            # out/video.mp4      1920x1080
npm run render:vertical   # out/vertical.mp4   1080x1920
npm run thumbnail         # out/thumbnail.jpg  1280x720
npm run vertical-cover    # out/vertical-cover.jpg
```

샷 하나만 다시 녹화: `node capture/capture.mjs widgets pet`

## 구조

| 파일 | 역할 |
|---|---|
| `scripts/script.mjs` | 대본. 장면별 자막 줄, 줄마다 화면 시간(초)과 녹화 샷 이름 |
| `scripts/make-timeline.mjs` | 고정 자막 길이로 `public/data/timeline.json`(장면·줄·샷 시작 프레임)과 `out/catroom.srt` |
| `capture/shots.mjs` | 샷별 연출: 고양이 위치·행동 큐, 날씨, 클릭, 위젯 hover·클릭 시점(자막 줄 기준) |
| `capture/widgets.mjs` | `{ widgets }` 프레임(UPLOAD.md "위젯 레이어" 형식). 벽에만 배치 |
| `capture/capture.mjs` | 가상 시계(Playwright clock + CSS 애니메이션 고정)로 1/30초씩 진행하며 캡처 → `public/clips/<샷>.mp4`, 페이지가 낸 소리를 `public/data/sfx-events.json`에 기록 |
| `scripts/make-sfx.mjs` | 스킨 효과음(방울, 털실, 골골, 위젯 소리)을 코드로 합성 |
| `scripts/make-music.mjs` | 영상 길이에 맞춘 로파이 배경음악 합성 |
| `src/kit.tsx` | 스타일 킷: 종이 이름표, 제목, 배경, 스킨 글꼴(Jua·Gowun Dodum) |
| `src/Video.tsx` | 샷·자막·효과음·음악 조립. 가로는 사진틀 + 하단 자막 띠, 세로는 샷별 `FOCUS`로 재구성 |
| `src/Thumbnail.tsx` | 썸네일, 세로 커버 |
| `config.json` | 스킨 페이지·글꼴 경로, 크레딧, 음악 |

대본이나 스킨이 바뀌면 `npm run prepare-all`부터 다시 돌립니다. 자막 길이를 바꾸면 녹화 길이도 따라 바뀝니다.
생성물(`public/clips`, `public/data`, `public/sfx`, `public/music`, `public/fonts`, `out/`)은 커밋하지 않습니다.

음악·효과음은 코드로 합성해 저작권자가 없고, 글꼴은 SIL OFL입니다.
