# 浏览器验收

`full-course.cjs` 用 Playwright 驱动无头浏览器，验收整套课程的真实学习流程：

- 课程首页统计、6 个课时卡片。
- 每个课时的 10 张词汇卡、5 张句子卡、翻译切换、词块提示与语法信息。
- 90 个音频按钮全部可用，并实际播放词汇与整句音频。
- 单课 10 题与完整题库 60 题的逐题判分、解析展示、选项锁定。
- 作答记录、6 课完成状态、刷新后的进度恢复。
- 不存在的课时、练习与未知路由的错误页。
- `localStorage` 不可用时的降级提示。
- 390px 移动端无横向滚动。

## 前置条件

先启动 API 与学习界面（见仓库 README），然后：

```bash
npm install --prefix tests/browser --ignore-scripts
node tests/browser/full-course.cjs
```

## 可选环境变量

- `BEAFSOUP_WEB_URL`：学习界面地址，默认 `http://localhost:5287`。
- `BEAFSOUP_API_URL`：API 地址，默认 `http://localhost:5049`。
- `BEAFSOUP_BROWSER_PATH`：浏览器可执行文件，默认 Windows 上的 Edge 路径；其他平台指向本机 Chromium 或 Chrome。

截图输出到 `tests/browser/screenshots`，失败时额外生成 `full-course-failure.png`。脚本要求控制台零未预期错误，并忽略导航取消（`net::ERR_ABORTED`）与故意触发的 404。
