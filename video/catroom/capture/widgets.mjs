// Widget frames in the host's { widgets } shape (registry/skins/UPLOAD.md "위젯 레이어"), placed only on the wall
// of a 1920x1080 window: left wall beside the picture frames, above the window, and the right wall.
// Floor, bed, rug, box, scratching post and bowls stay clear.
const DAYS = [
  ["2026-11-11", "수"], ["2026-11-12", "목"], ["2026-11-13", "금"], ["2026-11-14", "토"],
  ["2026-11-15", "일"], ["2026-11-16", "월"], ["2026-11-17", "화"],
];
const KINDS = [
  { id: "good", label: "좋음", color: "#9FD9C3" }, { id: "okay", label: "보통", color: "#FFE08A" },
  { id: "tired", label: "피곤", color: "#F0DCC4" }, { id: "sleepy", label: "졸림", color: "#BCD3E3" },
  { id: "unwell", label: "아픔", color: "#F4A7B0" }, { id: "stressed", label: "스트레스", color: "#E8798A" },
];
const moodDays = ["okay", "good", "tired", "good", "sleepy", "okay", "good"];
const sleepHours = [6.5, 7.2, 5.8, 8.4, 9.1, 6.9, 7.5];
const box = (id, type, x, y, width, height, state) => ({ id, type, x, y, width, height, baseWidth: width, baseHeight: height, state });

export function clockState(time = "14:05") {
  const [hour, minute] = time.split(":").map(Number);
  return { time, hour, minute, seconds: 0, weekday: "화요일", weekdayIndex: 2, date: "2026-11-17", dateLabel: "11월 17일", mode: "analog", style: null };
}

export function widgetFrames({ mood = "okay" } = {}) {
  const kindLabel = Object.fromEntries(KINDS.map((k) => [k.id, k.label]));
  return [
    box("builtin.tabs", "facet.app/tabs@1", 70, 22, 470, 70, {
      tabs: [
        { id: "home", name: "홈", icon: "Home", selected: true },
        { id: "plan", name: "일정", icon: "Calendar", selected: false },
        { id: "rest", name: "휴식", icon: "Moon", selected: false },
      ],
    }),
    box("w-clock", "facet.clock/time@1", 105, 115, 230, 270, clockState()),
    box("w-weather", "facet.weather/now@1", 60, 410, 320, 160, {
      condition: "clear", label: "맑음", temperature: 12.4, humidity: 41, place: "서울", status: "14:00 기준", loading: false, observedAt: "14:00",
    }),
    box("w-sleep", "facet.sleep/week@1", 1370, 20, 470, 250, {
      days: DAYS.map(([date, label], i) => ({ date, label, hours: sleepHours[i], recorded: true, isToday: i === 6 })),
    }),
    box("w-agenda", "facet.schedule/agenda@1", 640, 4, 640, 156, {
      previous: { id: "e1", title: "고양이 밥 주기", start: "12:30", end: "13:00", minutesLeft: null, ending: false, memo: null },
      current: { id: "e2", title: "디자인 리뷰", start: "13:30", end: "14:30", minutesLeft: 25, ending: false, memo: null },
      next: { id: "e3", title: "모래 갈아주기", start: "16:00", end: "16:20", minutesLeft: null, ending: false, memo: null },
    }),
    box("w-mood", "facet.condition/current@1", 1370, 290, 470, 150, { kinds: KINDS, current: mood, currentLabel: kindLabel[mood] }),
    box("w-churu", "facet.condition/history@1", 1370, 455, 470, 140, {
      days: DAYS.map(([date, label], i) => ({ date, label, level: KINDS.findIndex((k) => k.id === moodDays[i]) + 1, kind: moodDays[i], isToday: i === 6 })),
      spans: [],
    }),
  ];
}
