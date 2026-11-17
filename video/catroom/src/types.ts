export type Line = { sub: string; shot: string; from: number; frames: number };

export type Scene = { id: string; from: number; frames: number; lines: Line[]; blurAt?: number; rollAt?: number; rollFrames?: number };

// A recorded clip (public/clips/<id>.mp4). `lines` are the caption starts inside the shot, in frames.
export type Shot = { id: string; from: number; frames: number; lines: number[] };

export type Timeline = { fps: number; totalFrames: number; scenes: Scene[]; shots: Shot[] };

// Sounds the skin page played during each shot, by frame inside the shot.
export type SfxEvents = Record<string, { frame: number; k: string; a: number | boolean | null }[]>;

export type Credit = { head: string; body: string[] };

export type Config = { slug: string; title: string; music: { src: string; credit: string } | null; credits: Credit[] };

export type VideoProps = { timeline: Timeline | null; sfx: SfxEvents; layout?: "wide" | "tall" };
