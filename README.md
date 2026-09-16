# 拆包助手 / Unpack Assistant

**Windows 自动多层解压工具 / Recursive archive extraction for Windows**

把压缩包放进去，添加可能用到的密码，逐层解压并整理最终文件。支持简体中文和英文。

Add archives and candidate passwords, extract nested layers, and find the final files in one place. Available in Simplified Chinese and English.

**当前版本 / Current version: 0.4.1 预览版 / Preview**

[下载 / Download](https://github.com/VanishQAQ/unpack-assistant/releases/tag/v0.4.1) · [所有版本 / All releases](https://github.com/VanishQAQ/unpack-assistant/releases)

## 中文说明

### 下载和使用

1. 打开上方下载页，下载 **unpack-assistant-0.4.1-windows-x64.zip**。
2. 解开 ZIP，运行里面的 **拆包助手.exe**，无需安装。不要下载 `Source code`，除非你要查看或编译源码。
3. 首次启动选择语言，之后可在 **设置 → 语言** 修改，选择会自动保存。
4. 添加压缩包、候选密码和输出目录，点击 **开始解压**。
5. 完成后在 **设置 → 打开结果** 查看文件。

需要 **64 位 Windows、.NET Framework 4.8**，以及下列任意一种解压软件。引擎不随本工具捆绑。

| 解压软件 | 在“设置 → 高级选项”选择 | 支持格式 |
| --- | --- | --- |
| [Bandizip](https://www.bandisoft.com/bandizip/) | `bz.exe` | ZIP、7z、RAR |
| [7-Zip](https://www.7-zip.org/) | `7z.exe`，保留同目录的 `7z.dll` | ZIP、7z、RAR |
| [WinRAR](https://www.rarlab.com/) | `WinRAR.exe` 或同目录的 `Rar.exe` | 仅 RAR |

遇到不同格式嵌套时，使用 Bandizip 或 7-Zip。WinRAR 的命令行引擎仅支持 RAR。

### 主要功能

- **多层解压**：压缩包里还有压缩包时继续处理，支持部分伪装后缀的归档。
- **分卷识别**：支持常见 7z、ZIP、RAR 分卷，外层解开后出现分卷也能继续。
- **候选密码**：支持中文密码，不同层可以使用不同密码；只尝试你提供的密码。
- **保存设置**：保存输出目录、选项和语言；候选密码使用 Windows 当前用户加密保存。
- **恢复任务**：关闭或取消后载入任务记录，验证并复用已完成的层。
- **结果管理**：任务全部成功后检查占用并清理中间文件，保留原始输入和最终结果。
- **Defender 扫描**：解压前和每层解压后扫描，进度栏显示“未发现威胁”“有威胁”或失败原因。

### 分卷与恢复任务

同一组分卷放在 **同一个文件夹**，保留原名。可以添加全部分卷，也可以只添加一卷；缺卷时先补齐。

恢复时点击 **恢复任务**，选择上次任务目录中的 **任务状态.json**，补充必要的密码，再点 **继续**。保留原压缩包和整个任务文件夹；中途打断的那一层通常需要重新解压。多个任务按顺序处理，不是同时解压。

### 扫描与密码隐私

当前只接入 **Microsoft Defender**，不接入火绒或卡巴斯基。若其他杀毒软件接管防护，Defender 可能被停用；程序会显示原因并询问是否继续，不会伪报扫描通过，也不会修改系统防护设置。加密内容需要先解开再扫描，未发现威胁不等于绝对安全。系统杀毒软件仍可能隔离文件。

**分享程序或本仓库的发行 ZIP，不会分享你的密码。** 密码保存在当前电脑、当前 Windows 用户的本地配置中，不在程序、发行包或报告里，不能直接复制给另一台电脑使用。

界面文字随语言切换；文件名、路径、密码保持原样，Windows 原生文件选择窗口跟随系统语言。

### 使用限制与反馈

这是预览版，请使用可信文件。ZIP64 暂不支持；并非所有压缩算法、旧版本和恶意归档都已验证。资源限制通过轮询检查，恶意链接防护尚未完成生产级安全验收。

反馈时提供软件版本、格式和错误提示即可；**不要公开上传私人文件、密码或包含隐私的任务报告**。

## English guide

### Download and start

1. Open the download link above and get **unpack-assistant-0.4.1-windows-x64.zip**.
2. Unzip it and run **拆包助手.exe**. No installer is needed. `Source code` downloads are for developers, not the ready-to-run app.
3. Choose a language on first launch. Change it later under **Settings → Language**; your choice is saved.
4. Add archives, candidate passwords and an output folder, then click **Extract**.
5. Find your files under **Settings → Open results**.

Requires **64-bit Windows, .NET Framework 4.8**, and one of these separately installed archive engines:

| Engine | Select under Settings → Advanced options | Formats |
| --- | --- | --- |
| [Bandizip](https://www.bandisoft.com/bandizip/) | `bz.exe` | ZIP, 7z, RAR |
| [7-Zip](https://www.7-zip.org/) | `7z.exe`, keeping `7z.dll` beside it | ZIP, 7z, RAR |
| [WinRAR](https://www.rarlab.com/) | `WinRAR.exe` or `Rar.exe` in the same folder | RAR only |

Use Bandizip or 7-Zip for nested archives with mixed formats. WinRAR's command-line engine supports RAR only.

### Features

- **Nested extraction:** processes archives inside archives, including some disguised extensions.
- **Split archives:** recognizes common 7z, ZIP and RAR volumes, including sets found inside an outer archive.
- **Candidate passwords:** supports Chinese passwords and different passwords for different layers. Only supplied candidates are tried.
- **Saved preferences:** remembers output folder, options and language; passwords are encrypted for the current Windows user.
- **Task recovery:** reloads saved tasks, verifies existing files and reuses completed layers.
- **Result management:** measures storage and cleans tracked intermediates after full success, preserving original inputs and final results.
- **Defender scans:** scans before extraction and after each layer, showing no threats found, detected threats or the failure reason in task progress.

### Split archives and recovery

Keep all volumes from a set in **one folder**, with their original names. Add all volumes or just one; complete any missing volumes first.

To resume, click **Restore task**, select **任务状态.json** from the previous task folder, add passwords if needed, and click **Continue**. Keep the source archives and the entire task folder. An interrupted layer usually needs to be extracted again. Multiple tasks run sequentially, not in parallel.

### Scanning and password privacy

Only **Microsoft Defender** is integrated; Huorong and Kaspersky are not available as scanners. Another antivirus may disable Defender. The app reports that failure and asks whether to continue; it never treats an unavailable scan as clean or changes system protection settings. Encrypted content must be extracted first. No threats found is not a guarantee of safety, and system antivirus may still quarantine files.

**Sharing the app or this repository's release ZIP does not share your passwords.** Passwords stay in local settings encrypted for the current Windows user, outside the executable, release package and reports. They cannot simply be copied to another computer.

App text follows the selected language. Filenames, paths and passwords remain unchanged; native Windows file dialogs follow the OS language.

### Limitations and feedback

This is a preview; use trusted files. ZIP64 is not supported yet. Not every algorithm, older version or malicious archive has been validated. Resource limits are polled, and malicious-link protection has not completed production security validation.

For issues, include the app version, archive format and error message. **Do not post private files, passwords or task reports containing personal information.**

## 从源码构建 / Build from source

在 Windows PowerShell 中进入项目目录并运行以下命令。输出目录为 `便携版-0.4.1`。

From the project folder in Windows PowerShell, run the command below. Output is in `便携版-0.4.1`.

```powershell
.\build.ps1
```

使用系统自带的 .NET Framework C# 编译器，无需 NuGet。测试位于 `tests`；部分测试需要已安装的解压引擎。

Uses the Windows .NET Framework C# compiler; no NuGet packages are required. Tests are under `tests`; some require installed archive engines.

## 开源协议 / License

[MIT License](LICENSE)。外部解压软件遵循各自的授权条款。

[MIT License](LICENSE). External archive engines retain their own license terms.
