import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname } from "node:path";

const [starCountArgument, historyPath, svgPath] = process.argv.slice(2);
const stars = Number.parseInt(starCountArgument, 10);

if (!Number.isSafeInteger(stars) || stars < 0 || !historyPath || !svgPath) {
  throw new Error("Usage: node scripts/update-star-history.mjs <star-count> <history-json> <output-svg>");
}

const today = new Date().toISOString().slice(0, 10);
let history = { schemaVersion: 1, points: [] };

try {
  history = JSON.parse(await readFile(historyPath, "utf8"));
} catch (error) {
  if (error.code !== "ENOENT") throw error;
}

const points = Array.isArray(history.points) ? history.points : [];
const lastPoint = points.at(-1);
if (lastPoint?.date === today) {
  lastPoint.stars = stars;
} else {
  points.push({ date: today, stars });
}

history = { schemaVersion: 1, points };
await mkdir(dirname(historyPath), { recursive: true });
await writeFile(historyPath, `${JSON.stringify(history, null, 2)}\n`, "utf8");

const width = 900;
const height = 320;
const padding = { left: 58, right: 48, top: 112, bottom: 60 };
const plotWidth = width - padding.left - padding.right;
const plotHeight = height - padding.top - padding.bottom;
const values = points.map((point) => point.stars);
const minimum = Math.min(...values);
const maximum = Math.max(...values);
const range = Math.max(1, maximum - minimum);
const xFor = (index) => padding.left + (points.length === 1 ? plotWidth : (index * plotWidth) / (points.length - 1));
const yFor = (value) => padding.top + plotHeight - ((value - minimum) / range) * plotHeight;
const path = points.map((point, index) => `${index === 0 ? "M" : "L"}${xFor(index).toFixed(1)} ${yFor(point.stars).toFixed(1)}`).join(" ");
const firstDate = points[0].date;
const lastDate = points.at(-1).date;
const lastX = xFor(points.length - 1).toFixed(1);
const lastY = yFor(stars).toFixed(1);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="title description">
  <title id="title">RobocopyGUI Star 历史</title>
  <desc id="description">截至 ${today}，仓库共有 ${stars} 个 Star。</desc>
  <rect width="${width}" height="${height}" rx="16" fill="#0d1117"/>
  <text x="${padding.left}" y="52" fill="#f0f6fc" font-family="Segoe UI, sans-serif" font-size="26" font-weight="600">RobocopyGUI Star 历史</text>
  <text x="${padding.left}" y="82" fill="#8b949e" font-family="Segoe UI, sans-serif" font-size="15">每日自动记录 · 最近更新：${today}</text>
  <text x="${width - padding.right}" y="52" fill="#58a6ff" font-family="Segoe UI, sans-serif" font-size="24" font-weight="600" text-anchor="end">★ ${stars}</text>
  <path d="M${padding.left} ${padding.top}H${width - padding.right} M${padding.left} ${padding.top + plotHeight / 2}H${width - padding.right} M${padding.left} ${padding.top + plotHeight}H${width - padding.right}" stroke="#30363d" stroke-width="1"/>
  <path d="${path}" fill="none" stroke="#58a6ff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="${lastX}" cy="${lastY}" r="5" fill="#58a6ff" stroke="#0d1117" stroke-width="3"/>
  <text x="${padding.left}" y="${height - 26}" fill="#8b949e" font-family="Segoe UI, sans-serif" font-size="14">${firstDate}</text>
  <text x="${width - padding.right}" y="${height - 26}" fill="#8b949e" font-family="Segoe UI, sans-serif" font-size="14" text-anchor="end">${lastDate}</text>
</svg>\n`;

await mkdir(dirname(svgPath), { recursive: true });
await writeFile(svgPath, svg, "utf8");
