# OpenClaw Console Lite（UWP / MSIX 轻量版）

基于 **Desktop Bridge / MSIX `runFullTrust`** 的轻量控制台，Windows 10 1709+ / Windows 11。界面为 Windows 8 ~ Win10 早期 Metro 风格（纯色标题带、磁贴、扁平直角控件、Segoe UI Light），支持浅色 / 深色 / 纯黑 / 跟随系统四主题。

> 与 EXE 全功能版并存、互不干扰：独立数据目录 `.openclaw-lite-home`、独立端口 18795、只管理本程序亲手启动的网关进程。
>
> EXE 全功能版：[openclaw-console-desktop](https://github.com/a2109246162-tech/openclaw-console-desktop)

## 为什么不是纯 UWP

纯 UWP 沙箱不能启动 `node.exe`、写任意用户目录、监听本地端口。本项目使用微软官方 **Desktop Bridge（MSIX `runFullTrust`）**：保留现代安装 / 更新体验，同时具备完整桌面权限。Win10 1709+ 即可安装。

## 功能

- **模型管理**：添加 OpenAI 兼容 API（火山方舟 / DeepSeek / OpenAI / 智谱 / Moonshot 预设）、上移 / 下移调整优先级、删除；改动自动重启网关
- **微信扫码绑定**：本地生成二维码，官方插件走正式安装流程
- **网关状态 / 启停**：端口 18795，磁贴显示状态、PID、当前模型
- **今日 Token 用量**：按模型展示
- **托盘常驻**：关闭窗口只隐藏不退出，网关继续在线；右键可启停网关 / 扫码 / 退出
- **四主题**：跟随系统（监听系统主题变化实时切换）/ 浅色 / 深色 / 纯黑
- **低开销**：启动不拉起后台 node 进程；轮询 8 秒一次，只用轻量端口探测，不跑 netstat / WMI；字体、用量均有缓存
- **高 DPI**：全部像素按 `DeviceDpi/96` 缩放，250% 缩放下无重叠

> 开机自启：MSIX 需要声明 `windows.startupTask` 并由用户授权才会在「设置 → 启动应用」中显示，本项目未启用该功能，避免留下无效的死按钮。

## 目录结构

```
src/
  Core.cs             核心逻辑：配置合并、模型增删改排序、插件安装、网关、扫码、环境隔离
  UiBase.cs           DPI 度量、主题引擎、自绘控件（Metro）、缓存、开关与用量
  UiForms.cs          主窗体与对话框（标题带拖动、浮层设置面板、托盘）
  app.manifest        DPI 感知声明（PerMonitorV2）
compile.bat           一键编译 + 自检（调用 .NET Framework csc）
build.bat             备用编译脚本
pack-msix.sh          组装包内容 → makeappx → 签名 → 校验
msix/
  AppxManifest.xml    MSIX 清单（Identity / runFullTrust / 1709+）
  assets/             商店与开始菜单图标
  preset-config.example.json  预置模型配置样例（Key 为占位符）
tools/
  gen-icons.js        图标生成脚本
  make-cert.ps1       自签证书生成脚本（仅用于本地开发）
```

## 构建

### 1. 编译主程序

双击 `compile.bat`（或用 PowerShell 执行），生成 `build\LiteConsole.exe` 并运行隔离自检：

```bat
compile.bat
```

需要 .NET Framework 4.x 的 `csc.exe`（Windows 10/11 自带）。

### 2. 准备运行时

打包脚本依赖 EXE 全功能版的 `runtime/`（node.exe + openclaw）与微信插件包，二者不包含在本仓库：

```
../oc-v2/app-root/runtime/node.exe
../oc-v2/app-root/runtime/openclaw/
../oc-v2/app-root/resources/app/plugins/
```

或自行准备等价目录并修改 `pack-msix.sh` 中的路径。

### 3. 配置 API Key

把 `msix/preset-config.example.json` 复制为实际使用的 `preset-config.json`，替换：

```jsonc
"gateway": { "auth": { "token": "REPLACE_WITH_YOUR_GATEWAY_TOKEN" } },
"models": { "providers": {
  "deepseek": { "apiKey": "sk-REPLACE_WITH_YOUR_API_KEY" }
}}
```

### 4. 打包与安装

`pack-msix.sh` 需要 Windows SDK 的 `makeappx.exe` / `signtool.exe`（脚本中 `tools/extracted/` 路径按实际修改）。

```bash
bash pack-msix.sh
```

首次安装前需把生成的自签根证书导入「本地计算机 → 受信任的根证书颁发机构」，之后双击 `.msix` 或用：

```powershell
Add-AppxPackage -Path .\OpenClawConsoleLite-1.0.0.0.msix
```

## 使用

1. 从开始菜单启动「OpenClaw 控制台 Lite」
2. 点「＋ 添加 API」填入你的模型接口与 Key；或在 `preset-config.json` 中预置
3. 点「微信扫码绑定」，用手机微信扫码确认
4. 点「▶ 启动网关」，磁贴变为「运行中」即可对话
5. 点右上角「设置」切换主题、查看防休眠开关
6. 关闭窗口会隐藏到托盘；托盘图标右键 → 「退出」才真正退出

## 安全说明

- 本仓库**不含**真实 API Key、证书私钥（`.pfx` / `.key`）、`.cer` 签名证书或用户数据。
- 数据只写入 `%USERPROFILE%\.openclaw-lite-home\`，端口固定 18795，绝不访问生产版 `.openclaw` 与 18789。
- 只终止本程序亲手记录 PID 的网关进程，不按进程名批量杀、不碰外部进程。

## 许可证

本项目代码采用 [MIT License](LICENSE)。

依赖组件各自遵循其原始许可证，本项目不重新授权它们：
- [Electron / Node.js](https://github.com/electron/electron) — MIT（随 EXE 版运行时提供）
- [OpenClaw](https://github.com/openclaw/openclaw) — 按其项目许可证
- `@tencent-weixin/openclaw-weixin` — 按腾讯官方插件许可证
- Windows SDK `makeappx.exe` / `signtool.exe` — Microsoft，仅作为构建工具使用

自签证书脚本仅用于本地开发测试；正式分发请使用受信任的代码签名证书。
