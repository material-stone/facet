import React from "react";
import { AbsoluteFill, OffthreadVideo, staticFile } from "remotion";
import { Backdrop, COLOR, loadSkinFonts, PaperTag, Title, u } from "./kit";

loadSkinFonts();

// A frame from the petting shot (hearts over the cat); the copy sits on the empty upper wall.
const POSTER = { clip: "clips/pet.mp4", seconds: 0.9 };

const Room: React.FC<{ left: number; top: number; height: number; width: number; focusX?: number }> = ({ left, top, height, width, focusX = 960 }) => {
  const s = height / 1080, vw = 1920 * s;
  const x = Math.min(0, Math.max(width - vw, width / 2 - focusX * s));
  return (
    <div style={{ position: "absolute", left, top, width, height, overflow: "hidden", borderRadius: u(1.4), border: `${u(0.4)}px solid ${COLOR.brown}`, boxShadow: `4px 6px 0 ${COLOR.shadow}` }}>
      <OffthreadVideo src={staticFile(POSTER.clip)} startFrom={Math.round(POSTER.seconds * 30)} muted style={{ position: "absolute", left: x, top: 0, width: vw, height }} />
    </div>
  );
};

export const Thumbnail: React.FC = () => (
  <AbsoluteFill>
    <Backdrop />
    <Room left={40} top={30} width={1840} height={1020} />
    <div style={{ position: "absolute", left: u(6), top: u(5) }}>
      <Title text="고양이 방" size={9} />
    </div>
    <div style={{ position: "absolute", left: u(7), top: u(16.5) }}>
      <PaperTag size={3} rotate={-3}>쓰다듬으면 골골골</PaperTag>
    </div>
    <div style={{ position: "absolute", right: u(5), top: u(5) }}>
      <PaperTag size={2.2} rotate={2}>Facet 배경 스킨</PaperTag>
    </div>
  </AbsoluteFill>
);

// Vertical cover (9:16). The profile grid shows the middle 3:4, so everything stays in y 12.5..87.5u.
export const VerticalCover: React.FC = () => (
  <AbsoluteFill>
    <Backdrop />
    <div style={{ position: "absolute", left: 0, right: 0, top: u(14), display: "flex", justifyContent: "center" }}>
      <Title text="고양이 방" size={9} />
    </div>
    <Room left={40} top={u(29)} width={1000} height={1000} focusX={870} />
    <div style={{ position: "absolute", left: 0, right: 0, top: u(83), display: "flex", justifyContent: "center" }}>
      <PaperTag size={2.6}>Facet 배경 스킨</PaperTag>
    </div>
  </AbsoluteFill>
);
