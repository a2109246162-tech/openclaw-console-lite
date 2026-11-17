#!/bin/bash
# MSIX 打包脚本 —— 在 build.bat 生成 build\LiteConsole.exe 之后运行
set -e
cd /d/build/oc-lite

MAKAPPX="tools/extracted/bin/10.0.26100.0/x64/makeappx.exe"
SIGNTOOL="tools/extracted/bin/10.0.26100.0/x64/signtool.exe"
PKG="msix/pkg"
OUT="msix/OpenClawConsoleLite-1.0.0.0.msix"

if [ ! -f build/LiteConsole.exe ]; then
  echo "[!] 未找到 build\\LiteConsole.exe —— 请先双击 build.bat 完成编译"
  exit 1
fi

echo "=== 1. 组装包内容 ==="
rm -rf "$PKG"
mkdir -p "$PKG/app/resources/app" "$PKG/runtime" "$PKG/assets"
cp msix/AppxManifest.xml "$PKG/"
cp msix/assets/*.png "$PKG/assets/"
cp build/LiteConsole.exe "$PKG/app/"
cp /d/build/oc-v2/app-root/resources/app/preset-config.json "$PKG/app/resources/app/"
cp -r /d/build/oc-v2/app-root/resources/app/plugins "$PKG/app/resources/app/"
cp /d/build/oc-v2/app-root/runtime/node.exe "$PKG/runtime/"
cp -r /d/build/oc-v2/app-root/runtime/openclaw "$PKG/runtime/"
echo "内容就绪：$(du -sh "$PKG" | cut -f1)"

echo "=== 2. makeappx 打包 ==="
MSYS_NO_PATHCONV=1 "$MAKAPPX" pack /d "$(cygpath -w "$PKG")" /p "$(cygpath -w "$PWD/$OUT")" /o
ls -la "$OUT"

echo "=== 3. 证书与签名 ==="
if [ ! -f msix/openclaw-lite.pfx ]; then
  powershell -NoProfile -ExecutionPolicy Bypass -File "D:\\build\\oc-lite\\tools\\make-cert.ps1"
fi
MSYS_NO_PATHCONV=1 "$SIGNTOOL" sign /fd SHA256 /a /f "$(cygpath -w "$PWD/msix/openclaw-lite.pfx")" /p openclaw-lite "$(cygpath -w "$PWD/$OUT")"

echo "=== 4. 签名校验 ==="
MSYS_NO_PATHCONV=1 "$SIGNTOOL" verify /a /pa "$(cygpath -w "$PWD/$OUT")" || true

echo ""
echo "[完成] $OUT"
echo "安装方法：先双击 msix/openclaw-lite.cer 安装证书（本地计算机 → 受信任的人），"
echo "         再双击 .msix 安装；或用 Add-AppxPackage 安装。"
