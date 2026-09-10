# MajdataX-Desktop（MajdataEdit-Neo）归档说明

本仓库为 MajdataX 桌面端编辑器 **MajdataEdit-Neo** 的完整源码（基于 re-poem/MajdataEdit-Neo，
含本项目全部改动：Simai 解析对齐 AstroDX/SimaiSharp、滑条头星与启动拍分离、SimaiVisualizer 时间轴等）。

## 内容
- WPF 编辑器全部源码（Models / ViewModels / Views / Types / Controls / Modules / Assets）
- 与渲染器 MajdataViewX（Unity 工程，见姊妹仓库 MajdataX-Mobile）通过 WS + 共享内存（MemoryPack）通信

## 构建
- 环境：Visual Studio 2022 + .NET 8（`MajdataEdit-Neo.csproj`）
- 依赖：nuget-local 本地包源（含 MajSimai 2.2.3、MemoryPack 等），首次构建自动还原
- 产物：`bin\Release\net8.0-windows\MajdataEdit-Neo.exe`

## 运行时配合
- 渲染器：MajdataViewX（Unity 6000.3.19f1，工程源码在姊妹仓库 MajdataX-Mobile 中，
  同一工程 `BuildScript.BuildWindows64` 出桌面渲染器）
- 桌面端部署目录：本机 `D:\Workspace\MajdataX`（渲染器）与本编辑器同目录运行
- E2E 自检：`D:\Workspace\tests\ViewXE2E`（dotnet，期望输出 E2E-OK）

## 成品
- 桌面渲染器成品包见本仓库 Releases（MajdataX-Desktop-Release.zip：MajdataViewX.exe + 依赖，直接解压运行）
