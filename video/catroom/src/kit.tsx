import React from "react";
import { continueRender, delayRender, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig } from "remotion";

// Style A "종이 이름표": cream paper tags with a thick brown outline, Jua headings, a slight tilt and a hard shadow.
export const COLOR = {
  cream: "#fff7ef",
  brown: "#5a4238",
  ink: "#4a3a33",
  pink: "#e8798a",
  blush: "#f9d5c6",
  bg: "#fbdfd0",
  shadow: "rgba(90,66,56,.25)",
};
export const FONT = { heading: '"Jua", sans-serif', body: '"Gowun Dodum", sans-serif' };

// 1u = 1% of 1920 px (wide is 100u x 56.25u, tall is 56.25u x 100u).
export const u = (n: number) => n * 19.2;

// The skin's own fonts, copied into public/fonts by `npm run fonts`.
let fontsLoading = false;
export function loadSkinFonts() {
  if (fontsLoading || typeof document === "undefined") return;
  fontsLoading = true;
  const handle = delayRender("skin fonts");
  const faces = [new FontFace("Jua", `url(${staticFile("fonts/Jua-Regular.ttf")})`), new FontFace("Gowun Dodum", `url(${staticFile("fonts/GowunDodum-Regular.ttf")})`)];
  Promise.all(faces.map((f) => f.load().then((loaded) => document.fonts.add(loaded))))
    .then(() => continueRender(handle))
    .catch((err) => { console.error(err); continueRender(handle); });
}

const pop = (frame: number, fps: number, delay = 0) => spring({ frame: frame - delay, fps, config: { damping: 11, stiffness: 170, mass: 0.6 } });

// Paper name tag: the caption look. Pops in, sits slightly tilted.
export const PaperTag: React.FC<{ children: React.ReactNode; size?: number; rotate?: number; delay?: number; style?: React.CSSProperties }> = ({ children, size = 2.5, rotate = -1.5, delay = 0, style }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = pop(frame, fps, delay);
  return (
    <div
      style={{
        display: "inline-block",
        background: COLOR.cream,
        border: `${u(0.32)}px solid ${COLOR.brown}`,
        borderRadius: u(1),
        padding: `${u(size * 0.18)}px ${u(size * 0.55)}px ${u(size * 0.14)}px`,
        boxShadow: `4px 6px 0 ${COLOR.shadow}`,
        fontFamily: FONT.heading,
        fontSize: u(size),
        lineHeight: 1.25,
        color: COLOR.ink,
        whiteSpace: "nowrap",
        transform: `rotate(${rotate}deg) scale(${interpolate(p, [0, 1], [0.7, 1])})`,
        opacity: interpolate(p, [0, 0.3], [0, 1], { extrapolateRight: "clamp" }),
        ...style,
      }}
    >
      {children}
    </div>
  );
};

// Caption: a paper tag centered in the given band. Stays inside its band (bottom band in wide, under the photo in tall).
export const Caption: React.FC<{ text: string; top: number; height: number; size: number; maxWidth: number }> = ({ text, top, height, size, maxWidth }) => (
  <div style={{ position: "absolute", left: 0, right: 0, top, height, display: "flex", alignItems: "center", justifyContent: "center" }}>
    <PaperTag size={size} style={{ maxWidth, whiteSpace: "normal", textAlign: "center", wordBreak: "keep-all", textWrap: "balance" } as React.CSSProperties}>{text}</PaperTag>
  </div>
);

// Big title: cream letters with a thick brown stroke and the same hard shadow.
export const Title: React.FC<{ text: string; size: number; delay?: number; style?: React.CSSProperties }> = ({ text, size, delay = 0, style }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = pop(frame, fps, delay);
  return (
    <div
      style={{
        fontFamily: FONT.heading,
        fontSize: u(size),
        lineHeight: 1,
        color: COLOR.cream,
        WebkitTextStroke: `${u(size * 0.075)}px ${COLOR.brown}`,
        paintOrder: "stroke fill",
        textShadow: `4px 6px 0 ${COLOR.shadow}`,
        transform: `rotate(-1.5deg) scale(${interpolate(p, [0, 1], [0.6, 1])})`,
        opacity: interpolate(p, [0, 0.3], [0, 1], { extrapolateRight: "clamp" }),
        whiteSpace: "nowrap",
        ...style,
      }}
    >
      {text}
    </div>
  );
};

// Soft pink backdrop with faint paw-dot pattern, behind the framed footage.
export const Backdrop: React.FC = () => (
  <div
    style={{
      position: "absolute",
      inset: 0,
      background: `radial-gradient(circle at 25% 25%, rgba(255,247,239,.55) 0 ${u(0.35)}px, transparent ${u(0.4)}px) 0 0 / ${u(4)}px ${u(4)}px, ${COLOR.bg}`,
    }}
  />
);
