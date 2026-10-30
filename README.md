# LearnALanguage.BeafSoup

面向中文学习者的德语 A0–A1 入门课程 MVP。课程源文件与程序分离，包含 6 课、60 个核心词汇、30 个实用句子、60 道四选一单选题，以及 3 段双人多音色场景对话。

当前版本提供：

- 课程首页、课时导航和本机学习进度。
- 词汇卡、词形说明、可点击的句子词块和中文翻译切换。
- 完整题库或单课练习、服务器判分、中文解析和成绩汇总。
- 三段场景对话：每个角色使用不同音色和语气，支持整段顺序播放与跟读预留时间。
- 独立的 Azure AI Speech / NVIDIA TTS 音频生成工具，支持逐句多音色、预览、单条生成、断点续跑和强制重建。
- 浏览器仅接收公开 DTO；正确答案和解析保留在服务器内容模型中，提交后才返回。

游客作答和已完成课时同时写入服务器（SQLite 开发、PostgreSQL 可切换）和浏览器 `localStorage`，离线时以本机记录为准，联网后自动合并。此里程碑仍不依赖账号系统或管理后台。

## 技术栈与目录

- .NET 11 / ASP.NET Core Minimal API
- Blazor WebAssembly（独立托管）
- EF Core 10（SQLite 默认，PostgreSQL 可切换）
- xUnit
- JSON 课程内容和本地音频文件

```text
content/
  courses/de-DE/a1-foundations/  # 课程、词汇、课时、题目、场景对话源文件
  audio/                         # 生成的音频和 manifest
src/
  BeafSoup.Contracts/            # 浏览器安全的公开 DTO
  BeafSoup.Content/              # 内容模型、加载器、校验器、音频指纹
  BeafSoup.Data/                 # EF Core 持久化（SQLite / PostgreSQL）
  BeafSoup.Api/                  # 课程 API、判分、对话、进度同步、音频 URL 解析
  BeafSoup.Web/                  # Blazor WebAssembly 学习界面
  BeafSoup.AudioGenerator/       # Azure / NVIDIA TTS 命令行工具
tests/BeafSoup.Tests/            # 内容、API、持久化、安全和音频生成测试
tests/browser/                   # Playwright 浏览器验收
```

Blazor 项目只引用 `BeafSoup.Contracts`，不引用包含答案的服务器内容项目。课程 JSON 不在 `wwwroot` 中，只有 `content/audio` 通过 API 的 `/audio` 路径公开。

## 环境要求

需要 .NET 11 SDK 和 `Microsoft.AspNetCore.App` 11.x 运行时。所有 6 个项目均使用 `net11.0`，Blazor WebAssembly 与 DevServer 包使用 11.0 RC 3。

当前开发版本为 `11.0.100-rc.3.26522.148`，属于发布候选版本而非稳定版；生产部署前可升级到 .NET 11 正式 SDK。无需再设置 `DOTNET_ROLL_FORWARD`。

本次工作区的 SDK 安装在已被 Git 忽略的 `artifacts/dotnet-11` 目录。若该 SDK 尚未加入 `PATH`，可将以下命令中的 `dotnet` 替换为 `artifacts/dotnet-11/dotnet.exe`。

## 启动

在仓库根目录执行：

```bash
dotnet restore LearnALanguage.BeafSoup.slnx
dotnet build LearnALanguage.BeafSoup.slnx --configuration Release --no-restore
```

启动 API：

```bash
dotnet run --project src/BeafSoup.Api --configuration Release --urls http://localhost:5049
```

另开终端启动学习界面：

```bash
dotnet run --project src/BeafSoup.Web --configuration Release --urls http://localhost:5287
```

打开 <http://localhost:5287>。Web 默认从 `http://localhost:5049/` 读取数据；部署到其他地址时修改 `src/BeafSoup.Web/wwwroot/appsettings.json`。

主要 API：

- `GET /api/v1/health`
- `GET /api/v1/courses`
- `GET /api/v1/courses/{courseId}`
- `GET /api/v1/lessons/{lessonId}`
- `GET /api/v1/lessons/{lessonId}/questions`
- `GET /api/v1/courses/{courseId}/questions?lessonId=...`
- `POST /api/v1/questions/{questionId}/grade`
- `GET /api/v1/dialogues`
- `GET /api/v1/dialogues/{dialogueId}`
- `GET /api/v1/progress/{guestId}?courseId=...`
- `POST /api/v1/progress/sync`

## 数据库

默认使用 SQLite，不需要任何外部服务：

```bash
# 默认：Data Source=artifacts/beafsoup.db
dotnet run --project src/BeafSoup.Api --configuration Release --urls http://localhost:5049
```

切换 PostgreSQL：

```bash
export Database__Provider=postgresql
export ConnectionStrings__BeafSoup="Host=localhost;Port=5432;Database=beafsoup;Username=beafsoup;Password=beafsoup"
dotnet run --project src/BeafSoup.Api --configuration Release --urls http://localhost:5049
```

两套提供程序共用同一个 EF Core 模型，只使用双方都支持的类型（Guid 主键、有界字符串、`DateTimeOffset` 时间戳），没有 SQLite 专有写法；启动时自动创建缺失的表结构。实体目前是游客档案、作答记录和已完成课时。

进度同步是**全量、幂等**的：客户端提交本机记录，服务器按作答 ID 与课时 ID 求并集后返回权威状态，因此重复提交、离线补传或多标签页并行都不会产生重复记录。

## 测试

```bash
dotnet test LearnALanguage.BeafSoup.slnx --configuration Release
```

测试覆盖课程数量、114 个音频任务（60 词汇 + 30 句子 + 24 句对话）、公开题目不泄露答案、全部 60 题的服务器正误判分、非法输入、API 路由、90 个词汇与句子音频 URL、对话台词音频、多音色设置指纹、SQLite/PostgreSQL provider 选择、进度幂等合并、语言回退、句子与对话分词、音频指纹、Azure/NVIDIA 请求结构、错误重试分类、无效音频拦截以及生成后的断点续跑。

浏览器验收会真正启动页面完成 6 课与完整题库，见 `tests/browser/README.md`：

```bash
npm install --prefix tests/browser --ignore-scripts
node tests/browser/full-course.cjs
```

## 音频生成

默认提供方为 Azure AI Speech，默认语音为 `de-DE-KatjaNeural`，区域为 `eastus`。合成地址按区域生成为 `https://<region>.tts.speech.microsoft.com/cognitiveservices/v1`；`https://<region>.api.cognitive.microsoft.com/` 是资源管理 endpoint，不是直接合成地址。

每个音频任务都是独立文件：词汇用原型文本，句子用整句，场景对话按**每句台词**生成，并使用该说话人在内容里声明的音色、语气风格和语速。Azure 按音色的 `StyleList` 校验风格，不支持时返回 HTTP 400，因此新增角色前要先确认风格在允许列表内。

密钥只从环境变量读取，不要写入源码、JSON、命令行参数或日志：

```bash
export AZURE_SPEECH_KEY="你的密钥"
export AZURE_SPEECH_REGION="eastus"  # 可选，默认就是 eastus
```

Windows PowerShell 可使用 `$env:AZURE_SPEECH_KEY="你的密钥"` 和 `$env:AZURE_SPEECH_REGION="eastus"`。

先预览任务，不请求服务，也不需要密钥：

```bash
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --dry-run
```

先用一个词验证服务和额度：

```bash
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus --id v-hallo
```

验证一个对话角色（不同音色与语气）：

```bash
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus --id dlg-01-first-meeting-t01
```

单条成功后再生成全部词汇、句子和对话台词：

```bash
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus
```

需要重建时：

```bash
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --force
```

可选参数：

- `--provider azure|nvidia`：语音服务，默认 `azure`。
- `--id <资源ID>`：只处理一个词汇或句子。
- `--format mp3|wav`：请求格式，默认 `mp3`。
- `--region <区域>`：Azure 区域，也可通过 `AZURE_SPEECH_REGION` 设置。
- `--voice <语音>`：语音名称，Azure 默认 `de-DE-KatjaNeural`。
- `--content-root <路径>`：指定其他课程目录。
- `--audio-root <路径>`：指定音频输出目录。
- `--base-url <地址>`：指定自定义合成接口地址。
- `--dry-run`：只列出任务。
- `--force`：忽略有效缓存并重新生成。

工具按稳定 ID 和文本/设置 SHA-256 指纹识别缓存，逐条更新 `content/audio/de-DE/manifest.json`，并以临时文件原子替换目标。非成功 HTTP、JSON/文本错误响应、过短数据和未知文件签名均不会标记为可用音频。

**当前服务状态：** Azure `eastus` 已完成整套课程音频生成，60 个词汇和 30 个句子共 90 条均为 `ready`。工具对临时限流和文件写入冲突提供重试，失败项可通过再次运行断点续跑补齐。NVIDIA 的 OpenAI 风格托管接口此前实测返回 HTTP 404，现有 NVIDIA provider 不能视为已适配 Magpie 所需的 Riva 协议。

## 场景对话

三段双人对话，每段 8 句，直接复用课文词汇：

| 对话 | 主题 | 角色与音色 | 语气 |
| --- | --- | --- | --- |
| `dlg-01-first-meeting` | 初次见面 | Anna / Lukas | `happy` / `excited` |
| `dlg-02-cafe-order` | 在咖啡馆点餐 | Bedienung / Gast | `happy` / `softvoice` |
| `dlg-03-making-a-plan` | 约定见面时间 | Nina / Tom | `joyful` / 默认语气 |

播放有两种模式，都在对话页顶部切换：

- **连续播放**：按台词顺序逐句播放，句间只有很短的停顿，用来整体感受语速和语调。
- **跟读模式**：每句播完后暂停，留出时间让学习者重复。停顿来自内容里的 `pauseAfterMs`，并按「短 / 标准 / 长」缩放为 0.75 / 1 / 1.5 倍。

`pauseAfterMs` 的默认值按实测语速推算：Azure 德语神经音色在语速 1.0 时约每字符 120 毫秒，因此预留 180 毫秒/字符，约等于语音时长的 1.5 倍，并限制在 3–12 秒之间。当前三段对话的台词音长在 3.0–5.2 秒之间，跟读时间随之落在 3.6–7.6 秒。

每句台词单独成文件，因此换音色只需要重新生成那几句；设置指纹包含音色、风格、语速，改配音会自动触发重建。

## 新增课时和后续课程

每课是独立 JSON 文件，词汇和题目也可持续扩展。基本流程：

1. 在 `vocabulary.json` 添加具有稳定 ID 的词条。
2. 新建课时文件，引用词汇 ID，添加句子和精确分词。
3. 在 `questions.json` 添加四选一题目，并把题目 ID 加入课时。
4. 将新课时 ID 按教学顺序加入 `course.json` 的 `lessonIds`。
5. 增加课程 `revision`，运行构建和测试。
6. 用 `--dry-run` 检查新增音频任务，再单条验证并批量生成。

新增场景对话：

1. 在 `dialogues/` 新建 `dlg-NN-theme.json`，声明 `speakers`（含 `voice`、`style`、`rate`）与 `turns`。
2. 把对话 ID 加入 `course.json` 的 `dialogueIds`，并增加 `revision`。
3. 每句台词的 `segments[].text` 拼接后必须与 `text` 逐字一致；不给 `pauseAfterMs` 时按字符数自动推算跟读时间。
4. 音色与风格的组合要先单条验证，确认返回 200 再批量生成。

详细字段和 ID 约束见 `content/courses/de-DE/a1-foundations/README.md`。不要复用或重命名已经发布的稳定 ID。
