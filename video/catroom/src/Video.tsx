import React from "react";
import { AbsoluteFill, interpolate, OffthreadVideo, Sequence, staticFile, useCurrentFrame } from "remotion";
import { Audio } from "@remotion/media";
import config from "../config.json";
import { Backdrop, Caption, COLOR, FONT, loadSkinFonts, PaperTag, Title, u } from "./kit";
import type { Config, Shot, VideoProps } from "./types";

loadSkinFonts();

const SRC_W = 1920, SRC_H = 1080;

// Where the footage sits. Wide: the room framed like a photo with a bottom band for captions, so captions never
// cover the cat. Tall: a 1020x1000 window into the room (top in the Reels safe area), captions under it.
export const LAYOUT = {
  wide: { frame: { left: 134, top: 22, width: 1652, height: 929 }, caption: { top: 951, height: 129, size: 2.5, maxWidth: u(80) } },
  tall: { frame: { left: 30, top: 240, width: 1020, height: 1000 }, caption: { top: 1262, height: 216, size: 2.4, maxWidth: u(48) } },
};

// Tall reframing: the source x (px of 1920) to keep centered for each shot, or [from, to] to pan across the shot.
const FOCUS: Record<string, number | [number, number]> = {
  open: 1180, walk: 860, groom: 860, eat: 600, pom: 980, hunt: 760, nap: 560,
  rain: 960, snow: 960, night: 960, widgets: [330, 1600], pet: 870, drop: 760, call: 860, end: 820,
};

const SFX_VOLUME: Record<string, number> = {
  ding: 0.45, purr: 0.75, chirp: 0.35, thud: 0.55, thump: 0.55, mew: 0.45, crunch: 0.3, lap: 0.3, scratch: 0.3,
  glass: 0.4, pop: 0.5, chime: 0.5, knock: 0.5, flick: 0.4, creak: 0.5,
};

const Footage: React.FC<{ shot: Shot; tall: boolean; blurFrom?: number }> = ({ shot, tall, blurFrom }) => {
  const frame = useCurrentFrame();
  const box = tall ? LAYOUT.tall.frame : LAYOUT.wide.frame;
  const s = box.height / SRC_H;
  const vw = SRC_W * s;
  let left = (box.width - vw) / 2;
  if (tall) {
    const f = FOCUS[shot.id] ?? 960;
    const fx = Array.isArray(f) ? interpolate(frame, [0, shot.frames], f, { extrapolateLeft: "clamp", extrapolateRight: "clamp" }) : f;
    left = Math.min(0, Math.max(box.width - vw, box.width / 2 - fx * s));
  }
  const blur = blurFrom === undefined ? 0 : interpolate(frame, [blurFrom, blurFrom + 18], [0, 9], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return (
    <div style={{ position: "absolute", ...box, overflow: "hidden", borderRadius: u(1.4), border: `${u(0.36)}px solid ${COLOR.brown}`, boxShadow: `4px 6px 0 ${COLOR.shadow}`, background: COLOR.blush }}>
      <OffthreadVideo src={staticFile(`clips/${shot.id}.mp4`)} muted style={{ position: "absolute", left, top: 0, width: vw, height: box.height, filter: blur ? `blur(${blur}px)` : undefined }} />
    </div>
  );
};

// Final card over the blurred last shot: where to find the skin, then a few credits.
const EndCard: React.FC<{ tall: boolean; rollAt: number; credits: Config["credits"] }> = ({ tall, rollAt, credits }) => {
  const frame = useCurrentFrame();
  const box = tall ? LAYOUT.tall.frame : LAYOUT.wide.frame;
  const fade = interpolate(frame, [rollAt, rollAt + 12], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return (
    <div style={{ position: "absolute", ...box, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: u(tall ? 2.2 : 1.6), opacity: fade }}>
      <Title text="고양이 방" size={tall ? 6 : 6.5} delay={rollAt} />
      <PaperTag size={tall ? 1.9 : 2} delay={rollAt + 6}>Facet 설정 → 스킨 → 고양이 방</PaperTag>
      <div style={{ display: "grid", gridTemplateColumns: "auto auto", columnGap: u(1.6), rowGap: u(0.5), marginTop: u(1), fontFamily: FONT.body, fontSize: u(tall ? 1.35 : 1.25), color: COLOR.ink, background: "rgba(255,247,239,.88)", border: `${u(0.2)}px solid ${COLOR.brown}`, borderRadius: u(1), padding: `${u(1)}px ${u(1.8)}px` }}>
        {credits.map((c) => (
          <React.Fragment key={c.head}>
            <div style={{ fontFamily: FONT.heading, color: COLOR.pink, textAlign: "right" }}>{c.head}</div>
            <div>{c.body.join(" · ")}</div>
          </React.Fragment>
        ))}
      </div>
    </div>
  );
};

export const CatRoomVideo: React.FC<VideoProps> = ({ timeline, sfx, layout = "wide" }) => {
  const tall = layout === "tall";
  if (!timeline) return <AbsoluteFill style={{ background: COLOR.bg }} />;
  const cfg = config as Config;
  const lay = tall ? LAYOUT.tall : LAYOUT.wide;
  const lines = timeline.scenes.flatMap((s) => s.lines);
  const ending = timeline.scenes.find((s) => s.blurAt !== undefined);
  const opening = timeline.scenes[0];
  const credits = [...cfg.credits, ...(cfg.music ? [{ head: "음악", body: [cfg.music.credit] }] : [])];
  const total = timeline.totalFrames;
  const musicVolume = (f: number) => {
    const base = ending?.blurAt !== undefined && f >= ending.blurAt ? interpolate(f, [ending.blurAt, ending.blurAt + 20], [0.3, 0.45], { extrapolateRight: "clamp" }) : 0.3;
    return base * interpolate(f, [0, 15, total - 40, total], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  };

  return (
    <AbsoluteFill style={{ background: COLOR.bg }}>
      <Backdrop />
      {timeline.shots.map((shot) => {
        const isEnd = ending && shot.from + shot.frames === ending.from + ending.frames;
        return (
          <Sequence key={shot.id} from={shot.from} durationInFrames={shot.frames} name={`shot ${shot.id}`}>
            <Footage shot={shot} tall={tall} blurFrom={isEnd && ending?.blurAt !== undefined ? ending.blurAt - shot.from : undefined} />
            {isEnd && ending?.rollAt !== undefined ? <EndCard tall={tall} rollAt={ending.rollAt - shot.from} credits={credits} /> : null}
            {(sfx[shot.id] ?? [])
              .filter((e, i, all) => SFX_VOLUME[e.k] && all.findIndex((o) => o.k === e.k && o.frame === e.frame) === i)
              .map((e, i) => (
                <Sequence key={`${e.k}${i}`} from={e.frame} layout="none" name={`sfx ${e.k}`}>
                  <Audio src={staticFile(`sfx/${e.k}.wav`)} volume={SFX_VOLUME[e.k]} />
                </Sequence>
              ))}
            {shot.id === "rain" ? (
              <Sequence from={8} layout="none" name="rain bed">
                <Audio src={staticFile("sfx/rain.wav")} volume={(f) => interpolate(f, [0, 20, shot.frames - 18, shot.frames - 8], [0, 0.5, 0.5, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })} />
              </Sequence>
            ) : null}
          </Sequence>
        );
      })}
      {/* Opening title on the empty wall above the window. */}
      <Sequence from={opening.from} durationInFrames={opening.frames} name="title">
        <div style={{ position: "absolute", left: 0, right: 0, top: tall ? lay.frame.top + u(1.6) : lay.frame.top + u(1.2), display: "flex", justifyContent: "center" }}>
          <Title text="고양이 방" size={tall ? 5.6 : 5.4} delay={4} />
        </div>
      </Sequence>
      {lines.map((line) => (
        <Sequence key={`c${line.from}`} from={line.from} durationInFrames={line.frames} name={line.sub}>
          <Caption text={line.sub} {...lay.caption} />
          <Audio src={staticFile("sfx/tag.wav")} volume={0.22} />
        </Sequence>
      ))}
      {cfg.music ? <Audio src={staticFile(cfg.music.src)} volume={musicVolume} /> : null}
    </AbsoluteFill>
  );
};
