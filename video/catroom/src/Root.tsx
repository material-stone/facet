import React from "react";
import { Composition, Still, staticFile, type CalculateMetadataFunction } from "remotion";
import { Thumbnail, VerticalCover } from "./Thumbnail";
import { CatRoomVideo } from "./Video";
import type { SfxEvents, Timeline, VideoProps } from "./types";

// Timeline (fixed caption durations) and the sounds logged during capture come from `npm run prepare-all`.
const calculateMetadata: CalculateMetadataFunction<VideoProps> = async ({ props }) => {
  const [timeline, sfx] = await Promise.all([
    fetch(staticFile("data/timeline.json")).then((r) => r.json() as Promise<Timeline>),
    fetch(staticFile("data/sfx-events.json")).then((r) => r.json() as Promise<SfxEvents>),
  ]);
  return { durationInFrames: timeline.totalFrames, fps: timeline.fps, props: { ...props, timeline, sfx } };
};

export const Root: React.FC = () => (
  <>
    <Composition id="Main" component={CatRoomVideo} width={1920} height={1080} fps={30} durationInFrames={300} defaultProps={{ timeline: null, sfx: {} } as VideoProps} calculateMetadata={calculateMetadata} />
    {/* Reels / Shorts: same clips and timing, footage reframed per shot for 9:16. */}
    <Composition id="Vertical" component={CatRoomVideo} width={1080} height={1920} fps={30} durationInFrames={300} defaultProps={{ timeline: null, sfx: {}, layout: "tall" } as VideoProps} calculateMetadata={calculateMetadata} />
    <Still id="Thumbnail" component={Thumbnail} width={1920} height={1080} />
    <Still id="VerticalCover" component={VerticalCover} width={1080} height={1920} />
  </>
);
