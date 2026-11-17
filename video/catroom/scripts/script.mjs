// The video script: no narration, so every caption line has a fixed on-screen time.
// `shot` names the recorded clip that plays under the line (capture/shots.mjs); consecutive lines
// with the same shot share one continuous clip. Scene ids match SCENES in src/Video.tsx.
const line = (sub, seconds, shot) => ({ sub, seconds, shot });

export function buildScenes() {
  return [
    { id: "opening", lines: [line("햇살 드는 작은 방이 있어요", 2.0, "open"), line("Facet 배경 스킨 · 고양이 방", 2.1, "open")] },
    { id: "day", lines: [line("치즈 고양이가 살아요", 2.0, "walk"), line("꼼꼼하게 세수하고", 2.0, "groom"), line("밥도 냠냠 먹어요", 2.0, "eat")] },
    { id: "play", lines: [line("방울이 흔들리면 톡톡!", 2.4, "pom"), line("털실을 보면 사냥 시작", 3.0, "hunt")] },
    { id: "nap", lines: [line("졸리면 도넛 침대에서 쿨쿨", 3.2, "nap")] },
    { id: "window", lines: [line("창밖은 실제 날씨를 따라가요", 2.3, "rain"), line("눈 오는 날도 있고", 1.8, "snow"), line("밤에는 전구가 반짝", 2.2, "night")] },
    { id: "widgets", lines: [line("위젯도 방에 어울리게 바뀌어요", 2.2, "widgets"), line("시계 · 날씨 · 할 일이 벽에 쏙", 2.0, "widgets"), line("기분은 고양이 얼굴로 콕", 2.0, "widgets")] },
    { id: "touch", lines: [line("쓰다듬으면 골골골", 1.9, "pet"), line("바닥을 누르면 털실이 톡", 2.0, "drop"), line("방울을 누르면 달려와요", 2.1, "call")] },
    { id: "ending", credits: true, lines: [line("Facet 설정 → 스킨에서 ‘고양이 방’", 2.8, "end"), line("오늘도 고양이와 함께해요", 1.9, "end")] },
  ];
}
