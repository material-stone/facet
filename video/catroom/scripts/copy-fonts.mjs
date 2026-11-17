// Copies the skin's own fonts (registry/skins/catroom) into public/fonts so Remotion loads them locally.
import { copyFile, mkdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const config = JSON.parse(await readFile(path.join(root, "config.json"), "utf8"));
await mkdir(path.join(root, "public", "fonts"), { recursive: true });
for (const f of config.skinFonts) await copyFile(path.resolve(root, f), path.join(root, "public", "fonts", path.basename(f)));
console.log(`글꼴 ${config.skinFonts.length}개를 복사했습니다.`);
