---
name: flatworld-git-commit
description: "FlatWorld 的提交范围、中文提交说明，以及拉取、冲突和推送规则。"
---

# FlatWorld Git

## 提交范围

- 提交依据当前分支、工作区、暂存区和实际 diff；只暂存本任务的明确路径，保留其他修改。`git add .`、`git add -A`、`git commit -am` 会混入无关内容，不用于本项目的范围提交。
- Unity 资源的新增、删除、移动包含对应 `.meta`；生成目录、Python 缓存和临时文件不入提交。
- 按 `AGENTS.md`，代码、资源、配置修改在任务结束时创建本地 commit。仅 Markdown 的小幅措辞、排版、少量补充或 Skill 微调可留待集中提交；完整重要文档、多章节大改或实质性批量整理需要提交。用户明确要求提交时不受文档大小限制。
- 普通“提交”仅指本地 commit；push、reset、rebase、amend、强推及历史改写需要用户明确授权。

## 提交操作

```powershell
git status --short --branch
git diff -- <本次文件>
git add -- <本次文件>
git diff --cached --check
git diff --cached --stat
git diff --cached
git commit -m "docs: 精简系统说明"
git status --short
```

暂存区应只含本次内容。提交标题用中文说明变化及结果，可带 `feat/fix/refactor/data/docs/chore` 前缀；复杂改动可附简短中文正文。结果包括短哈希、说明和剩余未提交内容。

## 拉取与冲突

拉取先 fetch，再比较本地/远端关系及所有本地修改路径，安全时 fast-forward；未提交内容不被覆盖。冲突按双方实际变化合并，整文件 `ours/theirs` 不能代替内容判断。
