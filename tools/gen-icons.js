// 用随包 pngjs 生成 MSIX 图标（纯 JS，无需绘图库）
const path = require('path');
const fs = require('fs');
const { PNG } = require('D:\\build\\oc-v2\\app-root\\runtime\\openclaw\\node_modules\\pngjs');

function draw(size) {
  const png = new PNG({ width: size, height: size });
  const m = Math.max(1, Math.round(size * 0.06));        // 边距
  const r = Math.round(size * 0.18);                      // 圆角半径
  const dark = [30, 34, 52], accent = [90, 130, 250], white = [245, 248, 255];
  function inRounded(x, y) {
    if (x < m || y < m || x >= size - m || y >= size - m) return false;
    const cx = Math.max(m + r, Math.min(x, size - m - r));
    const cy = Math.max(m + r, Math.min(y, size - m - r));
    const dx = x - cx, dy = y - cy;
    return dx * dx + dy * dy <= r * r || (x >= m + r && x < size - m - r) || (y >= m + r && y < size - m - r);
  }
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const idx = (size * y + x) << 2;
      let c = [0, 0, 0, 0];
      if (inRounded(x, y)) {
        // 垂直渐变深蓝底
        const t = (y - m) / (size - 2 * m);
        c = [dark[0] + Math.round(20 * t), dark[1] + Math.round(26 * t), dark[2] + Math.round(40 * t), 255];
        // 中央圆形 "机器人头"
        const ccx = size / 2, ccy = size * 0.44, cr = size * 0.22;
        const ddx = x - ccx, ddy = y - ccy;
        if (ddx * ddx + ddy * ddy <= cr * cr) c = [accent[0], accent[1], accent[2], 255];
        // 两只眼睛
        const er = size * 0.045;
        const e1 = { x: size / 2 - size * 0.075, y: size * 0.42 };
        const e2 = { x: size / 2 + size * 0.075, y: size * 0.42 };
        for (const e of [e1, e2]) {
          const ex = x - e.x, ey = y - e.y;
          if (ex * ex + ey * ey <= er * er) c = [white[0], white[1], white[2], 255];
        }
        // 天线
        if (Math.abs(x - size / 2) < size * 0.02 && y > size * 0.12 && y < size * 0.24) c = [accent[0], accent[1], accent[2], 255];
        if (Math.abs(x - size / 2) < size * 0.035 && y >= size * 0.10 && y <= size * 0.125) c = [white[0], white[1], white[2], 255];
      }
      png.data[idx] = c[0]; png.data[idx + 1] = c[1]; png.data[idx + 2] = c[2]; png.data[idx + 3] = c[3];
    }
  }
  return PNG.sync.write(png);
}

const outDir = process.argv[2];
fs.mkdirSync(outDir, { recursive: true });
for (const [name, size] of [['Square44x44Logo.png', 44], ['Square150x150Logo.png', 150], ['StoreLogo.png', 50], ['Square44x44Logo.targetsize-24_altform-unplated.png', 24]]) {
  fs.writeFileSync(path.join(outDir, name), draw(size));
  console.log('icon:', name, size + 'x' + size);
}
console.log('icons done');
