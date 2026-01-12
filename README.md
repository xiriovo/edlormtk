<p align="center">
  <img src="https://readme-typing-svg.herokuapp.com?font=Orbitron&weight=900&size=42&pause=1000&color=00D4FF&center=true&vCenter=true&width=600&height=80&lines=MultiFlash+TOOL" alt="MultiFlash TOOL"/>
</p>

<p align="center">
  <b>🚀 Multi-Platform Android Flash Tool | 多平台安卓刷机工具</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows"/>
  <img src="https://img.shields.io/badge/License-MIT-00C853?style=for-the-badge" alt="MIT License"/>
  <img src="https://img.shields.io/github/stars/xiriovo/edlormtk?style=for-the-badge&logo=github&color=FFD700" alt="Stars"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Qualcomm-EDL_9008-E4002B?style=flat-square&logo=qualcomm&logoColor=white" alt="Qualcomm"/>
  <img src="https://img.shields.io/badge/MediaTek-BROM-FF6B00?style=flat-square" alt="MTK"/>
  <img src="https://img.shields.io/badge/Unisoc-SPRD-00A8E8?style=flat-square" alt="Unisoc"/>
  <img src="https://img.shields.io/badge/ADB-Fastboot-3DDC84?style=flat-square&logo=android&logoColor=white" alt="ADB"/>
</p>

---

<p align="center">
  <a href="#-中文文档">🇨🇳 中文</a> •
  <a href="#-english-documentation">🇺🇸 English</a> •
  <a href="#-日本語ドキュメント">🇯🇵 日本語</a> •
  <a href="#-한국어-문서">🇰🇷 한국어</a> •
  <a href="#-documentación-en-español">🇪🇸 Español</a> •
  <a href="#-Документация-на-русском">🇷🇺 Русский</a>
</p>

---

## 📸 Screenshots / 截图

<p align="center">
  <i>Coming soon... / 即将推出...</i>
</p>

---

# 🇨🇳 中文文档

## ✨ 功能特性

<table>
<tr>
<td width="50%">

### 📱 高通 (Qualcomm)
| 功能 | 描述 |
|:---:|:---|
| 🔌 | **EDL 9008 模式** - Sahara + Firehose 协议 |
| 💾 | **分区管理** - 读取/写入/擦除分区 |
| 📊 | **GPT 解析** - 自动识别分区表 |
| 🎯 | **Super 分区** - 动态分区刷写 |
| ☁️ | **云端 Loader** - 自动匹配 Programmer |
| 🏷️ | **多品牌** - 小米/OPPO/一加/Realme/vivo |

</td>
<td width="50%">

### 📱 联发科 (MTK)
| 功能 | 描述 |
|:---:|:---|
| 🔧 | **BROM 模式** - Preloader 连接 |
| 📋 | **DA 代理** - 下载代理支持 |
| 📄 | **Scatter 解析** - 自动加载配置 |
| 🔓 | **Auth 绕过** - SLA/DAA 认证 |

</td>
</tr>
<tr>
<td width="50%">

### 📱 展讯 (Unisoc)
| 功能 | 描述 |
|:---:|:---|
| ⬇️ | **Download 模式** - SPRD 协议 |
| 📦 | **PAC 固件** - 自动解析提取 |
| 🚀 | **FDL 发送** - FDL1/FDL2 加载 |
| 🔓 | **RSA 绕过** - 签名验证绕过 |
| 📱 | **Diag 诊断** - IMEI/AT 命令 |

</td>
<td width="50%">

### 🔧 通用功能
| 功能 | 描述 |
|:---:|:---|
| 📲 | **ADB/Fastboot** - 标准调试工具 |
| 👁️ | **设备监听** - 自动识别连接 |
| 📈 | **实时进度** - 速度/时间显示 |
| 📝 | **详细日志** - 操作记录 |

</td>
</tr>
</table>

## 📦 安装

### 系统要求
```
✅ Windows 10/11 (x64)
✅ .NET 8.0 Runtime
✅ USB 驱动 (Qualcomm QDLoader / MTK VCOM / SPRD)
```

### 下载
📥 从 [**Releases**](https://github.com/xiriovo/edlormtk/releases) 页面下载最新版本

### 编译
```bash
git clone https://github.com/xiriovo/edlormtk.git
cd edlormtk
dotnet build -c Release
```

## 🚀 快速开始

<details>
<summary><b>📱 高通设备 (点击展开)</b></summary>

1. 设备进入 **EDL 9008 模式**
   - 关机状态按住音量键插入 USB
   - 或使用 ADB: `adb reboot edl`
2. 选择或自动匹配 **Programmer Loader**
3. 选择要刷写的**分区和镜像**
4. 点击 **刷写**

</details>

<details>
<summary><b>📱 联发科设备 (点击展开)</b></summary>

1. 设备**关机**
2. 选择 **Scatter 文件**
3. 按住**音量下键**插入 USB
4. 等待设备连接后开始刷写

</details>

<details>
<summary><b>📱 展讯设备 (点击展开)</b></summary>

1. 选择 **PAC 固件**
2. 设备**关机**，按住**音量下键**插入 USB
3. 等待进入 **Download 模式**
4. 点击 **刷写**

</details>

## ⚠️ 免责声明

> **本工具仅供学习和研究使用，刷机有风险，操作需谨慎！**
> 
> 作者不对因使用本工具造成的任何损失负责。

---

# 🇺🇸 English Documentation

## ✨ Features

<table>
<tr>
<td width="50%">

### 📱 Qualcomm
| Feature | Description |
|:---:|:---|
| 🔌 | **EDL 9008 Mode** - Sahara + Firehose |
| 💾 | **Partition Mgmt** - Read/Write/Erase |
| 📊 | **GPT Parsing** - Auto partition table |
| 🎯 | **Super Partition** - Dynamic partitions |
| ☁️ | **Cloud Loader** - Auto-match Programmer |
| 🏷️ | **Multi-brand** - Xiaomi/OPPO/OnePlus/Realme |

</td>
<td width="50%">

### 📱 MediaTek (MTK)
| Feature | Description |
|:---:|:---|
| 🔧 | **BROM Mode** - Preloader connection |
| 📋 | **DA Agent** - Download agent support |
| 📄 | **Scatter Parse** - Auto-load config |
| 🔓 | **Auth Bypass** - SLA/DAA auth |

</td>
</tr>
<tr>
<td width="50%">

### 📱 Unisoc (Spreadtrum)
| Feature | Description |
|:---:|:---|
| ⬇️ | **Download Mode** - SPRD protocol |
| 📦 | **PAC Firmware** - Auto parse/extract |
| 🚀 | **FDL Send** - FDL1/FDL2 loading |
| 🔓 | **RSA Bypass** - Signature bypass |
| 📱 | **Diag Mode** - IMEI/AT commands |

</td>
<td width="50%">

### 🔧 General
| Feature | Description |
|:---:|:---|
| 📲 | **ADB/Fastboot** - Standard debug |
| 👁️ | **Device Monitor** - Auto detection |
| 📈 | **Live Progress** - Speed/time display |
| 📝 | **Detailed Logs** - Operation records |

</td>
</tr>
</table>

## 📦 Installation

### Requirements
```
✅ Windows 10/11 (x64)
✅ .NET 8.0 Runtime
✅ USB Drivers (Qualcomm QDLoader / MTK VCOM / SPRD)
```

### Download
📥 Download latest from [**Releases**](https://github.com/xiriovo/edlormtk/releases)

### Build
```bash
git clone https://github.com/xiriovo/edlormtk.git
cd edlormtk
dotnet build -c Release
```

## ⚠️ Disclaimer

> **This tool is for educational and research purposes only!**
> 
> Flashing carries risks. The author is not responsible for any damage.

---

# 🇯🇵 日本語ドキュメント

## ✨ 機能

| プラットフォーム | 機能 |
|:---:|:---|
| **Qualcomm** | EDL 9008、Sahara/Firehose、GPT解析、Super分区 |
| **MediaTek** | BROM/Preloader、DAエージェント、Scatter解析 |
| **Unisoc** | SPRDプロトコル、PAC解析、RSAバイパス |
| **共通** | ADB/Fastboot、デバイス監視、リアルタイム進捗 |

## ⚠️ 免責事項

> このツールは教育・研究目的のみです。フラッシュにはリスクがあります。

---

# 🇰🇷 한국어 문서

## ✨ 기능

| 플랫폼 | 기능 |
|:---:|:---|
| **Qualcomm** | EDL 9008, Sahara/Firehose, GPT 파싱, Super 파티션 |
| **MediaTek** | BROM/Preloader, DA 에이전트, Scatter 파싱 |
| **Unisoc** | SPRD 프로토콜, PAC 파싱, RSA 우회 |
| **공통** | ADB/Fastboot, 장치 모니터링, 실시간 진행률 |

## ⚠️ 면책 조항

> 이 도구는 교육 및 연구 목적으로만 사용됩니다. 플래싱에는 위험이 따릅니다.

---

# 🇪🇸 Documentación en Español

## ✨ Características

| Plataforma | Características |
|:---:|:---|
| **Qualcomm** | EDL 9008, Sahara/Firehose, análisis GPT, Super partición |
| **MediaTek** | BROM/Preloader, agente DA, análisis Scatter |
| **Unisoc** | Protocolo SPRD, análisis PAC, bypass RSA |
| **General** | ADB/Fastboot, monitor de dispositivos, progreso en tiempo real |

## ⚠️ Descargo de responsabilidad

> Esta herramienta es solo para fines educativos. El flasheo conlleva riesgos.

---

# 🇷🇺 Документация на русском

## ✨ Возможности

| Платформа | Функции |
|:---:|:---|
| **Qualcomm** | EDL 9008, Sahara/Firehose, парсинг GPT, Super раздел |
| **MediaTek** | BROM/Preloader, DA агент, парсинг Scatter |
| **Unisoc** | Протокол SPRD, парсинг PAC, обход RSA |
| **Общие** | ADB/Fastboot, мониторинг устройств, прогресс в реальном времени |

## ⚠️ Отказ от ответственности

> Этот инструмент предназначен только для образовательных целей. Прошивка несёт риски.

---

## 📁 Project Structure / 项目结构

```
MultiFlash-TOOL/
├── 📂 Modules/
│   ├── 📂 Common/           # 🔧 Common components / 公共组件
│   ├── 📂 Qualcomm/         # 📱 Qualcomm EDL module / 高通模块
│   │   ├── SaharaProtocol   #    Sahara protocol / Sahara 协议
│   │   ├── FirehoseClient   #    Firehose client / Firehose 客户端
│   │   └── Services/        #    Service layer / 服务层
│   ├── 📂 MTK/              # 📱 MediaTek module / 联发科模块
│   ├── 📂 Unisoc/           # 📱 Unisoc module / 展讯模块
│   │   ├── Protocol/        #    SPRD protocol / SPRD 协议
│   │   ├── Firmware/        #    PAC/Sparse parser / 固件解析
│   │   └── Exploit/         #    RSA bypass / RSA 绕过
│   └── 📂 AdbFastboot/      # 📲 ADB/Fastboot / 调试工具
├── 📂 Dialogs/              # 💬 Dialog windows / 对话框
├── 📂 Utils/                # 🛠️ Utilities / 工具类
├── 📄 MainWindow.xaml       # 🖼️ Main UI / 主界面
└── 📄 App.xaml              # 🚀 Application entry / 应用入口
```

---

## 📞 Contact / 联系方式

<p align="center">
  <a href="https://github.com/xiriovo/edlormtk">
    <img src="https://img.shields.io/badge/GitHub-xiriovo/edlormtk-181717?style=for-the-badge&logo=github" alt="GitHub"/>
  </a>
  <a href="mailto:1708298587@qq.com">
    <img src="https://img.shields.io/badge/Email-1708298587@qq.com-EA4335?style=for-the-badge&logo=gmail&logoColor=white" alt="Email"/>
  </a>
  <a href="https://qm.qq.com/cgi-bin/qm/qr?k=xxx">
    <img src="https://img.shields.io/badge/QQ-1708298587-12B7F5?style=for-the-badge&logo=tencentqq&logoColor=white" alt="QQ"/>
  </a>
</p>

---

## 🤝 Contributing / 贡献

We welcome contributions! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

欢迎贡献代码！请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

---

## 📜 License / 许可证

<p align="center">
  <b>MIT License</b> - See <a href="LICENSE">LICENSE</a> for details
</p>

---

## 💖 Donate / 赞赏支持

<p align="center">
  <b>如果这个项目对你有帮助，欢迎赞赏支持！</b><br/>
  <i>If this project helps you, consider buying me a coffee!</i>
</p>

<table align="center">
<tr>
<td align="center" width="300">

### 💚 微信支付 / WeChat

<img src="Assets/donate_wechat.png" width="200" alt="WeChat Pay"/>

</td>
<td align="center" width="300">

### 💙 支付宝 / Alipay

<img src="Assets/donate_alipay.png" width="200" alt="Alipay"/>

</td>
</tr>
</table>

<p align="center">
  <i>您的支持是我持续开发的动力！</i><br/>
  <i>Your support keeps this project alive!</i>
</p>

---

## 🙏 Acknowledgments / 致谢

| Project | Description |
|:---:|:---|
| [bkerler/edl](https://github.com/bkerler/edl) | Qualcomm EDL reference |
| [bkerler/mtkclient](https://github.com/bkerler/mtkclient) | MTK client reference |
| [HandyControl](https://github.com/HandyOrg/HandyControl) | UI components |

---

<p align="center">
  <img src="https://readme-typing-svg.herokuapp.com?font=Fira+Code&pause=1000&color=00D4FF&center=true&vCenter=true&width=435&lines=Made+with+%E2%9D%A4%EF%B8%8F+for+Android+Community" alt="Made with love"/>
</p>

<p align="center">
  <b>⭐ Star this project if you find it useful! ⭐</b>
</p>
