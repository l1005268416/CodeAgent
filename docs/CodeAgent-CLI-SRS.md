# CodeAgent CLI — 软件需求规格说明书（SRS）

| 项目信息 | |
|---|---|
| **项目名称** | CodeAgent CLI |
| **文档版本** | v1.0.0-draft |
| **目标平台** | .NET 8 LTS / 跨平台（Windows、macOS、Linux） |
| **文档日期** | 2026-04-16 |
| **文档状态** | 草案（Draft） |

---

## 1. 引言

### 1.1 项目背景

随着大语言模型（LLM）能力的快速提升，开发者越来越需要一个**本地化、可扩展、命令行驱动的 AI 编程助手**。现有方案（如 Cursor、GitHub Copilot CLI）多为云端绑定或闭源，缺乏对本地模型、自定义工具链和灵活技能编排的支持。

CodeAgent CLI 旨在填补这一空白，提供一个**开源、可扩展、面向本地模型优先**的命令行 AI Agent 框架。

### 1.2 项目目标

1. 构建一个基于 .NET 8 的命令行 AI Agent 工具，支持多轮对话式编程辅助。
2. 实现完整的 **MCP（Model Context Protocol）工具注册与调用**机制，标准化外部工具集成。
3. 提供 **Skill（技能）系统**，通过 Prompt 模板 + 工具链的组合实现可复用的工作流。
4. 支持灵活的 **LLM 后端切换**，优先支持 Ollama 等本地模型，同时兼容任何 OpenAI API 兼容接口。
5. 设计可扩展的架构，便于社区贡献插件和技能。

### 1.3 术语定义

| 术语 | 定义 |
|---|---|
| **Agent** | 具备自主决策能力的 AI 实体，能理解指令并调用工具完成任务 |
| **Session** | 一次完整的用户与 Agent 的交互会话，包含对话历史和上下文 |
| **MCP** | Model Context Protocol，模型上下文协议，用于标准化 AI 应用与外部系统的集成 |
| **Tool** | Agent 可调用的外部功能单元，如文件读写、代码执行、搜索等 |
| **Skill** | 预定义的 Prompt 模板与工具链组合，封装为可复用的工作流单元 |
| **Context** | 对话上下文，包含消息历史、系统提示、工具调用记录等 |
| **Host** | MCP 架构中的宿主应用，即 CodeAgent CLI 本身 |
| **MCP Server** | 通过 MCP 协议向 Host 暴露工具、资源和提示词的外部服务 |

### 1.4 参考文档

- [MCP Specification (2024-11-05)](https://modelcontextprotocol.io/specification/2024-11-05)
- [Semantic Kernel Agent Architecture](https://learn.microsoft.com/en-us/semantic-kernel/Frameworks/agent/agent-architecture)
- [OpenAI API Compatibility](https://platform.openai.com/docs/api-reference)

---

## 2. 总体描述

### 2.1 产品视角

CodeAgent CLI 是一个**终端原生**的 AI 编程助手。用户通过命令行启动交互式会话，以自然语言描述需求，Agent 理解意图后自动规划执行步骤，调用注册的工具和技能完成任务。

```
┌─────────────────────────────────────────────────────────┐
│                    CodeAgent CLI                         │
│                                                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐             │
│  │ Session  │  │  Skill   │  │ Context  │             │
│  │ Manager  │  │  Engine  │  │ Manager  │             │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘             │
│       │              │              │                   │
│  ┌────┴──────────────┴──────────────┴─────┐            │
│  │            Agent Core (Orchestrator)    │            │
│  └────┬──────────────┬──────────────┬─────┘            │
│       │              │              │                   │
│  ┌────┴─────┐  ┌─────┴─────┐  ┌────┴─────┐           │
│  │   LLM    │  │   MCP     │  │  Tool    │           │
│  │ Provider │  │  Client   │  │ Registry │           │
│  └──────────┘  └───────────┘  └──────────┘           │
│       │              │              │                   │
│  ┌────┴──────────────┴──────────────┴─────┐            │
│  │          Transport Layer               │            │
│  │  (stdio / SSE / HTTP for MCP)          │            │
│  └────────────────────────────────────────┘            │
└─────────────────────────────────────────────────────────┘
```

### 2.2 用户类与特征

| 用户角色 | 特征描述 |
|---|---|
| **个人开发者** | 熟悉命令行操作，希望使用本地模型进行编程辅助，关注隐私和成本 |
| **框架扩展者** | C# 开发者，希望为 CodeAgent 贡献新的 Tool、Skill 或 MCP Server 集成 |
| **团队管理员** | 配置团队共享的 Skill 库和 MCP Server，管理 Agent 行为策略 |

### 2.3 运行环境

| 项目 | 要求 |
|---|---|
| **运行时** | .NET 8.0 SDK 及以上 |
| **操作系统** | Windows 10+、macOS 12+、Ubuntu 20.04+ / 其他主流 Linux 发行版 |
| **LLM 后端** | Ollama（推荐）或任何 OpenAI API 兼容服务 |
| **依赖** | Git（可选，用于版本管理相关 Skill） |

### 2.4 约束与假设

**约束：**
- 必须以命令行为主要交互界面（不包含 GUI）
- LLM 推理依赖外部服务（本地或远程），CodeAgent 本身不内置模型
- MCP 通信遵循 JSON-RPC 2.0 协议规范

**假设：**
- 用户已安装并配置好至少一个 LLM 后端（Ollama 或 OpenAI 兼容 API）
- 用户具备基本的命令行操作能力
- MCP Server 由用户自行安装和配置

---

## 3. 功能需求

### 3.1 F01 — 会话管理（Session Management）

#### 3.1.1 功能描述

管理用户与 Agent 之间的交互会话，包括会话的创建、恢复、切换、列表和删除。

#### 3.1.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F01-01 | 支持创建新的交互会话，自动生成唯一 Session ID | P0 |
| F01-02 | 支持为会话设置自定义名称（`--name` 参数） | P1 |
| F01-03 | 支持列出所有历史会话（`session list`），显示 ID、名称、创建时间、最后活跃时间 | P0 |
| F01-04 | 支持恢复指定会话（`session resume <id>`），加载完整对话上下文 | P0 |
| F01-05 | 支持删除指定会话（`session delete <id>`） | P1 |
| F01-06 | 会话数据持久化存储到本地文件系统（`~/.codeagent/sessions/`） | P0 |
| F01-07 | 支持会话导出为 Markdown / JSON 格式（`session export <id> --format md\|json`） | P2 |
| F01-08 | 支持配置会话级别的系统提示词（System Prompt） | P1 |
| F01-09 | 支持会话超时自动保存（默认 30 分钟无操作） | P2 |

#### 3.1.3 数据模型

```csharp
public class Session
{
    public string Id { get; set; }              // GUID
    public string Name { get; set; }            // 用户自定义名称
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string SystemPrompt { get; set; }    // 会话级系统提示词
    public List<Message> Messages { get; set; }  // 对话消息列表
    public Dictionary<string, object> Metadata { get; set; } // 扩展元数据
}
```

---

### 3.2 F02 — MCP 工具注册（MCP Tool Registration）

#### 3.2.1 功能描述

基于 MCP（Model Context Protocol）协议，实现与外部 MCP Server 的连接、工具发现、注册和生命周期管理。

#### 3.2.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F02-01 | 支持 **stdio** 传输方式连接 MCP Server（启动子进程并通过标准输入/输出通信） | P0 |
| F02-02 | 支持 **SSE（Server-Sent Events）** 传输方式连接远程 MCP Server | P1 |
| F02-03 | 自动发现 MCP Server 暴露的所有工具（`tools/list`） | P0 |
| F02-04 | 自动发现 MCP Server 暴露的资源（`resources/list`） | P1 |
| F02-05 | 自动发现 MCP Server 暴露的提示词模板（`prompts/list`） | P1 |
| F02-06 | 将发现的工具注册到统一的 Tool Registry 中，供 Agent 调用 | P0 |
| F02-07 | 支持通过配置文件（`~/.codeagent/mcp.json`）声明 MCP Server 连接信息 | P0 |
| F02-08 | 支持 MCP Server 的动态添加和移除（运行时 `mcp add` / `mcp remove`） | P1 |
| F02-09 | 支持 MCP Server 连接状态监控和健康检查 | P2 |
| F02-10 | 支持 MCP Server 启动失败时的优雅降级和错误提示 | P1 |

#### 3.2.3 MCP 配置文件格式

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@anthropic/mcp-filesystem", "/path/to/dir"],
      "transport": "stdio",
      "enabled": true
    },
    "web-search": {
      "url": "http://localhost:3001/mcp",
      "transport": "sse",
      "headers": {
        "Authorization": "Bearer ${MCP_API_KEY}"
      },
      "enabled": true
    }
  }
}
```

#### 3.2.4 MCP 工具调用流程

```
用户输入 → Agent Core → 判断需要调用工具
                              │
                    ┌─────────┴──────────┐
                    │  查询 Tool Registry │
                    └─────────┬──────────┘
                              │
                    ┌─────────┴──────────┐
                    │  路由到 MCP Client  │
                    └─────────┬──────────┘
                              │
                    ┌─────────┴──────────┐
                    │  MCP Client 发送    │
                    │  tools/call 请求    │
                    │  (JSON-RPC 2.0)    │
                    └─────────┬──────────┘
                              │
                    ┌─────────┴──────────┐
                    │  MCP Server 执行    │
                    │  并返回结果         │
                    └─────────┬──────────┘
                              │
                    ┌─────────┴──────────┐
                    │  结果回注上下文      │
                    │  Agent 继续推理     │
                    └────────────────────┘
```

---

### 3.3 F03 — Skill 技能调用（Skill Invocation）

#### 3.3.1 功能描述

Skill 是预定义的 **Prompt 模板 + 工具链组合**，封装为可复用的工作流单元。Agent 在对话中根据用户意图自动匹配和调用合适的 Skill，用户也可显式指定。

#### 3.3.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F03-01 | 支持通过 YAML 文件定义 Skill（包含名称、描述、Prompt 模板、关联工具列表） | P0 |
| F03-02 | 支持从默认 Skill 目录（`~/.codeagent/skills/`）自动加载所有 Skill | P0 |
| F03-03 | 支持通过命令行列出所有可用 Skill（`skill list`），显示名称、描述和状态 | P0 |
| F03-04 | Agent 根据用户输入自动匹配最相关的 Skill（基于描述相似度） | P1 |
| F03-05 | 支持用户显式调用指定 Skill（`/skill <name>` 或 `use <name>`） | P0 |
| F03-06 | Skill 的 Prompt 模板支持变量插值（`{{variable}}` 语法） | P0 |
| F03-07 | Skill 支持声明前置条件（required tools），缺少时给出明确提示 | P1 |
| F03-08 | 支持社区 Skill 的安装和管理（`skill install <url>` / `skill uninstall <name>`） | P2 |
| F03-09 | Skill 执行过程中支持多轮工具调用和结果聚合 | P1 |
| F03-10 | 支持 Skill 执行日志记录和调试输出 | P2 |

#### 3.3.3 Skill 定义规范（YAML）

```yaml
# ~/.codeagent/skills/code-review.yaml
name: code-review
version: "1.0.0"
description: "对代码文件进行审查，给出改进建议"
author: "community"

# 系统提示词模板
system_prompt: |
  你是一个资深代码审查专家。请对用户提供的代码进行审查，
  重点关注：代码质量、潜在Bug、性能问题、安全风险。
  以 Markdown 格式输出审查报告。

# 用户提示词模板（支持变量插值）
prompt_template: |
  请审查以下 {{language}} 代码文件 {{file_path}} 的内容：
  ```
  {{code_content}}
  ```

# 关联的工具列表（工具名称）
required_tools:
  - file_read
  - file_write

# 参数定义
parameters:
  - name: language
    type: string
    required: true
    description: "编程语言"
  - name: file_path
    type: string
    required: true
    description: "文件路径"
  - name: code_content
    type: string
    required: false
    description: "代码内容（如未提供则通过 file_read 工具读取）"

# 标签（用于分类和搜索）
tags:
  - code-quality
  - review
  - best-practices
```

#### 3.3.4 Skill 执行流程

```
用户输入 /skill code-review --file main.cs
    │
    ▼
Skill Engine 加载 Skill 定义
    │
    ▼
验证 required_tools 是否可用
    │
    ├── 不可用 → 提示用户缺少必要工具，列出缺失项
    │
    ▼ 可用
解析参数（显式参数 + 从上下文推断）
    │
    ▼
渲染 Prompt 模板（变量插值）
    │
    ▼
构建增强上下文（System Prompt + 渲染后的 User Prompt + 工具定义）
    │
    ▼
调用 LLM → Agent 推理 → 按需调用工具 → 返回结果
```

---

### 3.4 F04 — 上下文管理（Context Management）

#### 3.4.1 功能描述

管理对话上下文，包括消息历史、系统提示词、工具调用记录等，确保 Agent 在多轮对话中保持连贯性，同时有效控制 Token 消耗。

#### 3.4.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F04-01 | 维护完整的对话消息历史（user / assistant / tool 消息） | P0 |
| F04-02 | 支持设置全局默认系统提示词（`~/.codeagent/config.yaml`） | P0 |
| F04-03 | 支持会话级系统提示词覆盖全局配置 | P1 |
| F04-04 | 实现**上下文窗口管理策略**，当消息超出模型 Token 限制时自动裁剪 | P0 |
| F04-05 | 上下文裁剪策略支持可配置：`truncate_oldest`（截断最早消息）/ `summarize`（摘要压缩） | P1 |
| F04-06 | 工具调用结果自动注入上下文（作为 tool message） | P0 |
| F04-07 | 支持上下文中的**文件附件**（自动读取文件内容并注入） | P1 |
| F04-08 | 支持**上下文标签/分区**，将不同类型的上下文信息分类管理 | P2 |
| F04-09 | 显示当前上下文 Token 用量（`/context stats`） | P2 |
| F04-10 | 支持手动清除上下文（`/context clear`） | P1 |

#### 3.4.3 上下文窗口管理策略

```
┌─────────────────────────────────────────────┐
│              Context Window                  │
│                                             │
│  ┌───────────────────────────────────────┐  │
│  │  System Prompt (固定，始终保留)        │  │
│  ├───────────────────────────────────────┤  │
│  │  Skill Prompt (如有，始终保留)         │  │
│  ├───────────────────────────────────────┤  │
│  │  Recent Messages (最近 N 条，优先保留) │  │
│  ├───────────────────────────────────────┤  │
│  │  Tool Definitions (当前可用工具)       │  │
│  ├───────────────────────────────────────┤  │
│  │  Older Messages (超出限制时裁剪)       │  │
│  └───────────────────────────────────────┘  │
│                                             │
│  Token Budget: ━━━━━━━━━━━━━░░░░ 75%        │
└─────────────────────────────────────────────┘
```

#### 3.4.4 消息类型定义

```csharp
public enum MessageRole
{
    System,     // 系统提示词
    User,       // 用户输入
    Assistant,  // Agent 回复
    Tool        // 工具调用结果
}

public class Message
{
    public string Id { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; }
    public List<ToolCall> ToolCalls { get; set; }    // Assistant 消息中的工具调用
    public string ToolCallId { get; set; }            // Tool 消息关联的调用 ID
    public DateTime Timestamp { get; set; }
    public int TokenCount { get; set; }               // 估算的 Token 数
    public Dictionary<string, object> Metadata { get; set; }
}
```

---

### 3.5 F05 — 工具调用（Tool Invocation）

#### 3.5.1 功能描述

Agent 核心能力之一——根据用户意图和 LLM 的推理结果，自动调用已注册的工具完成任务。支持内置工具和 MCP 外部工具的统一调用。

#### 3.5.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F05-01 | 支持内置工具：`file_read`（读取文件）、`file_write`（写入文件）、`shell_exec`（执行命令） | P0 |
| F05-02 | 支持内置工具：`glob`（文件模式匹配）、`grep`（内容搜索） | P1 |
| F05-03 | 内置工具与 MCP 工具使用统一的 `ITool` 接口抽象 | P0 |
| F05-04 | LLM 返回 tool_call 时，Agent 自动解析参数并路由到对应工具执行 | P0 |
| F05-05 | 工具执行结果自动格式化并回注到对话上下文 | P0 |
| F05-06 | 支持**工具调用链**（Agent 可在一次回复中连续调用多个工具） | P0 |
| F05-07 | 支持配置工具调用的最大迭代次数（防止无限循环，默认 10 次） | P0 |
| F05-08 | 工具执行超时机制（可配置超时时间，默认 30 秒） | P1 |
| F05-09 | 工具执行错误处理：捕获异常、记录日志、向 Agent 返回友好错误信息 | P0 |
| F05-10 | 支持**工具权限控制**：`allow` / `deny` / `confirm` 三种策略 | P1 |
| F05-11 | `shell_exec` 工具执行前需用户确认（安全策略） | P0 |
| F05-12 | 支持工具调用的流式输出（工具执行过程中实时显示进度） | P2 |

#### 3.5.3 工具权限策略

| 策略 | 行为 |
|---|---|
| `allow` | 自动执行，无需确认 |
| `deny` | 拒绝执行，返回提示信息 |
| `confirm` | 暂停执行，等待用户在终端确认（默认策略） |

```yaml
# ~/.codeagent/config.yaml 中的工具权限配置
tools:
  permissions:
    file_read: allow
    file_write: confirm
    shell_exec: confirm
    glob: allow
    grep: allow
    # MCP 工具默认使用 confirm 策略
    mcp_default: confirm
```

#### 3.5.4 工具注册接口

```csharp
public interface ITool
{
    string Name { get; }                          // 工具唯一名称
    string Description { get; }                   // 工具描述（供 LLM 理解）
    JsonElement InputSchema { get; }              // JSON Schema 定义参数
    Task<ToolResult> ExecuteAsync(                // 执行工具
        JsonElement parameters,
        CancellationToken cancellationToken = default);
}

public class ToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; }           // 文本结果
    public object? StructuredData { get; set; }   // 结构化数据（可选）
    public string? Error { get; set; }            // 错误信息（如有）
}
```

---

### 3.6 F06 — LLM Provider 管理

#### 3.6.1 功能描述

管理 LLM 后端连接，支持 Ollama 本地模型和任何 OpenAI API 兼容接口。

#### 3.6.2 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F06-01 | 支持 **Ollama** 作为 LLM 后端（通过 Ollama OpenAI 兼容 API） | P0 |
| F06-02 | 支持任何 **OpenAI API 兼容接口**（自定义 Base URL + API Key） | P0 |
| F06-03 | 支持配置多个 LLM Provider，可运行时切换（`/model <name>`） | P1 |
| F06-04 | 支持 **流式输出**（Streaming），Agent 回复实时显示 | P0 |
| F06-05 | 支持 **Function Calling / Tool Use** 协议（OpenAI 格式） | P0 |
| F06-06 | 支持配置模型参数：`temperature`、`top_p`、`max_tokens` 等 | P1 |
| F06-07 | 支持连接健康检查和自动重连 | P2 |

#### 3.6.3 LLM Provider 配置

```yaml
# ~/.codeagent/config.yaml
llm:
  default_provider: ollama
  providers:
    ollama:
      type: openai_compatible
      base_url: "http://localhost:11434/v1"
      api_key: "ollama"                    # Ollama 不需要真实 key
      model: "qwen2.5-coder:7b"
      temperature: 0.7
      max_tokens: 4096
      supports_streaming: true
      supports_tool_use: true

    remote:
      type: openai_compatible
      base_url: "https://api.example.com/v1"
      api_key: "${OPENAI_API_KEY}"         # 支持环境变量引用
      model: "gpt-4o"
      temperature: 0.7
      max_tokens: 8192
      supports_streaming: true
      supports_tool_use: true
```

---

### 3.7 F07 — 命令行界面（CLI）

#### 3.7.1 功能需求

| 编号 | 需求描述 | 优先级 |
|---|---|---|
| F07-01 | 交互式 REPL 模式：启动后进入多轮对话 | P0 |
| F07-02 | 单次执行模式：`codeagent "你的问题"` 直接输出结果后退出 | P0 |
| F07-03 | 管道输入支持：`cat file.cs \| codeagent "解释这段代码"` | P1 |
| F07-04 | 内置斜杠命令：`/help`、`/clear`、`/context`、`/model`、`/skill`、`/session`、`/exit` | P0 |
| F07-05 | 支持 Markdown 格式化输出（代码高亮、列表等） | P1 |
| F07-06 | 支持 `--verbose` 模式显示详细调试信息 | P2 |
| F07-07 | 支持 `--config` 指定自定义配置文件路径 | P2 |

#### 3.7.2 命令行参数

```
用法: codeagent [选项] [提示词]

选项:
  -s, --session <id>       恢复指定会话
  -n, --new                创建新会话
  -m, --model <name>       指定 LLM 模型
  -c, --config <path>      指定配置文件路径
  -v, --verbose            详细输出模式
  -h, --help               显示帮助信息
  --version                显示版本信息

斜杠命令（交互模式内）:
  /help                    显示帮助
  /clear                   清除当前对话上下文
  /context stats           显示上下文 Token 用量
  /model list              列出可用模型
  /model <name>            切换模型
  /skill list              列出可用技能
  /skill <name> [args]     调用指定技能
  /session list            列出历史会话
  /session new             创建新会话
  /session export <id>     导出会话
  /mcp list                列出已注册的 MCP Server 和工具
  /mcp add <config>        添加 MCP Server
  /mcp remove <name>       移除 MCP Server
  /tools                   列出所有可用工具
  /exit                    退出
```

---

## 4. 非功能需求

### 4.1 性能要求

| 编号 | 需求描述 |
|---|---|
| NF-01 | CLI 启动时间 ≤ 2 秒（不含 LLM 连接） |
| NF-02 | 上下文加载（恢复会话）≤ 1 秒（10,000 条消息以内） |
| NF-03 | 工具调用延迟仅包含实际执行时间，框架开销 ≤ 50ms |
| NF-04 | 支持 LLM 流式输出，首 Token 延迟取决于 LLM 后端 |

### 4.2 可靠性要求

| 编号 | 需求描述 |
|---|---|
| NF-05 | 会话数据自动保存，进程异常退出不丢失未保存的对话 |
| NF-06 | LLM 调用失败时提供重试机制（最多 3 次，指数退避） |
| NF-07 | MCP Server 连接断开时自动重连（最多 5 次） |
| NF-08 | 工具执行超时后终止并返回超时错误 |

### 4.3 安全要求

| 编号 | 需求描述 |
|---|---|
| NF-09 | `shell_exec` 工具默认需用户确认后方可执行 |
| NF-10 | API Key 等敏感信息支持环境变量引用，不硬编码在配置文件中 |
| NF-11 | 文件写入操作默认需用户确认（可配置为 allow） |
| NF-12 | 支持工具执行沙箱（可选，通过配置限制可访问的目录范围） |

### 4.4 可扩展性要求

| 编号 | 需求描述 |
|---|---|
| NF-13 | 内置工具通过 `ITool` 接口实现，新增工具只需实现接口并注册 |
| NF-14 | Skill 通过 YAML 文件定义，无需修改代码即可添加新技能 |
| NF-15 | LLM Provider 通过配置添加，支持运行时切换 |
| NF-16 | MCP Server 通过配置文件声明式添加 |

### 4.5 可维护性要求

| 编号 | 需求描述 |
|---|---|
| NF-17 | 代码遵循 .NET 编码规范，使用 C# 12+ 语言特性 |
| NF-18 | 核心模块单元测试覆盖率 ≥ 80% |
| NF-19 | 使用依赖注入（Microsoft.Extensions.DependencyInjection）管理服务生命周期 |
| NF-20 | 使用结构化日志（Serilog 或 Microsoft.Extensions.Logging） |

---

## 5. 系统架构

### 5.1 架构分层

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│              (CLI / REPL / Command Parsing)                  │
├─────────────────────────────────────────────────────────────┤
│                    Application Layer                         │
│         (Agent Orchestrator / Skill Engine / Session Mgr)    │
├─────────────────────────────────────────────────────────────┤
│                    Domain Layer                              │
│        (Context Manager / Tool Registry / Message Model)     │
├─────────────────────────────────────────────────────────────┤
│                  Infrastructure Layer                        │
│     (LLM Provider / MCP Client / File Storage / Logging)     │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 核心模块职责

| 模块 | 职责 | 关键类/接口 |
|---|---|---|
| **AgentOrchestrator** | Agent 核心编排器，协调 LLM 调用、工具执行、上下文管理 | `IAgentOrchestrator` |
| **SessionManager** | 会话生命周期管理（CRUD、持久化） | `ISessionManager` |
| **ContextManager** | 上下文构建、Token 计算、窗口管理 | `IContextManager` |
| **SkillEngine** | Skill 加载、匹配、执行 | `ISkillEngine` |
| **ToolRegistry** | 工具注册、发现、路由 | `IToolRegistry` |
| **McpClientManager** | MCP Server 连接管理、协议通信 | `IMcpClientManager` |
| **LlmProvider** | LLM API 调用抽象（流式/非流式/Tool Use） | `ILlmProvider` |
| **CliApp** | 命令行解析、REPL 交互、输出渲染 | `ICliApp` |

### 5.3 关键依赖库（建议）

| 库 | 用途 |
|---|---|
| **Microsoft.Extensions.DependencyInjection** | 依赖注入 |
| **Microsoft.Extensions.Configuration** | 配置管理 |
| **Microsoft.Extensions.Logging + Serilog** | 结构化日志 |
| **System.Text.Json** | JSON 序列化/反序列化 |
| **YamlDotNet** | YAML 配置解析 |
| **Spectre.Console** | 终端 UI 渲染（Markdown、表格、进度条） |
| **Mcma.Core** 或自实现 MCP Client | MCP 协议通信 |

### 5.4 项目结构（建议）

```
src/
├── CodeAgent.CLI/                    # 命令行入口项目
│   ├── Program.cs
│   ├── Commands/                     # CLI 命令定义
│   └── Rendering/                    # 终端输出渲染
│
├── CodeAgent.Core/                   # 核心业务逻辑
│   ├── Agent/
│   │   ├── IAgentOrchestrator.cs
│   │   └── AgentOrchestrator.cs
│   ├── Session/
│   │   ├── ISessionManager.cs
│   │   ├── Session.cs
│   │   └── SessionStore.cs
│   ├── Context/
│   │   ├── IContextManager.cs
│   │   ├── ContextWindow.cs
│   │   └── TokenCounter.cs
│   ├── Skill/
│   │   ├── ISkillEngine.cs
│   │   ├── SkillDefinition.cs
│   │   └── SkillLoader.cs
│   ├── Tools/
│   │   ├── ITool.cs
│   │   ├── ToolRegistry.cs
│   │   ├── ToolResult.cs
│   │   └── BuiltIn/                 # 内置工具实现
│   │       ├── FileReadTool.cs
│   │       ├── FileWriteTool.cs
│   │       ├── ShellExecTool.cs
│   │       ├── GlobTool.cs
│   │       └── GrepTool.cs
│   └── Models/
│       ├── Message.cs
│       ├── ToolCall.cs
│       └── LlmResponse.cs
│
├── CodeAgent.LLM/                    # LLM Provider 抽象层
│   ├── ILlmProvider.cs
│   ├── LlmProviderFactory.cs
│   ├── OpenAICompatibleProvider.cs
│   └── Models/
│       ├── ChatRequest.cs
│       └── ChatResponse.cs
│
├── CodeAgent.MCP/                    # MCP 协议实现
│   ├── IMcpClient.cs
│   ├── IMcpClientManager.cs
│   ├── McpClientManager.cs
│   ├── Transports/
│   │   ├── StdioTransport.cs
│   │   └── SseTransport.cs
│   └── Models/
│       ├── McpServerConfig.cs
│       └── McpTool.cs
│
└── CodeAgent.Infrastructure/          # 基础设施
    ├── Storage/
    │   ├── FileSessionStore.cs
    │   └── ConfigManager.cs
    └── Logging/
        └── LoggingSetup.cs

tests/
├── CodeAgent.Core.Tests/
├── CodeAgent.LLM.Tests/
└── CodeAgent.MCP.Tests/
```

---

## 6. 数据存储

### 6.1 存储结构

```
~/.codeagent/
├── config.yaml              # 全局配置文件
├── mcp.json                 # MCP Server 配置
├── sessions/                # 会话数据目录
│   ├── {session-id}.json    # 单个会话文件
│   └── ...
├── skills/                  # Skill 定义目录
│   ├── code-review.yaml
│   ├── code-generate.yaml
│   ├── git-assist.yaml
│   └── ...
└── logs/                    # 日志文件目录
    └── codeagent.log
```

### 6.2 配置文件完整示例

```yaml
# ~/.codeagent/config.yaml

# LLM 配置
llm:
  default_provider: ollama
  providers:
    ollama:
      type: openai_compatible
      base_url: "http://localhost:11434/v1"
      api_key: "ollama"
      model: "qwen2.5-coder:7b"
      temperature: 0.7
      max_tokens: 4096

# 上下文管理
context:
  max_tokens: 128000              # 上下文窗口最大 Token 数
  truncation_strategy: truncate_oldest  # truncate_oldest | summarize
  reserve_tokens: 4096            # 为回复预留的 Token 数

# 工具配置
tools:
  max_iterations: 10              # 单次回复最大工具调用次数
  default_timeout: 30             # 工具执行默认超时（秒）
  permissions:
    file_read: allow
    file_write: confirm
    shell_exec: confirm
    glob: allow
    grep: allow
    mcp_default: confirm

# 会话配置
session:
  auto_save_interval: 60          # 自动保存间隔（秒）
  idle_timeout: 1800              # 空闲超时（秒），0 为不超时
  max_sessions: 100               # 最大保存会话数

# 全局系统提示词
system_prompt: |
  你是一个专业的编程助手。你可以帮助用户编写代码、调试问题、
  解释概念和管理项目。请使用中文回复。

# 日志配置
logging:
  level: Information              # Trace | Debug | Information | Warning | Error
  file: logs/codeagent.log
  max_file_size_mb: 10
  retained_files: 5
```

---

## 7. 接口定义

### 7.1 Agent 编排器接口

```csharp
public interface IAgentOrchestrator
{
    /// <summary>
    /// 处理用户输入，返回 Agent 回复（流式）
    /// </summary>
    IAsyncEnumerable<string> ProcessStreamAsync(
        string userMessage,
        Session session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理用户输入，返回 Agent 回复（非流式）
    /// </summary>
    Task<string> ProcessAsync(
        string userMessage,
        Session session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用指定 Skill 处理用户输入
    /// </summary>
    IAsyncEnumerable<string> ProcessWithSkillAsync(
        string skillName,
        Dictionary<string, string> parameters,
        Session session,
        CancellationToken cancellationToken = default);
}
```

### 7.2 LLM Provider 接口

```csharp
public interface ILlmProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    /// <summary>
    /// 非流式 Chat Completion
    /// </summary>
    Task<ChatResponse> CompleteAsync(
        List<Message> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式 Chat Completion
    /// </summary>
    IAsyncEnumerable<ChatChunk> CompleteStreamAsync(
        List<Message> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);
}
```

### 7.3 MCP Client 接口

```csharp
public interface IMcpClient : IDisposable
{
    string ServerName { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// 调用指定工具
    /// </summary>
    Task<McpToolResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken ct = default);

    /// <summary>
    /// 获取所有可用资源
    /// </summary>
    Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取所有可用提示词模板
    /// </summary>
    Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(CancellationToken ct = default);
}
```

---

## 8. 错误处理

### 8.1 错误分类

| 错误类型 | 处理策略 |
|---|---|
| **LLM 调用失败** | 指数退避重试（最多 3 次），最终失败后向用户展示友好错误信息 |
| **MCP Server 连接失败** | 标记 Server 为不可用，不影响其他工具使用，后台自动重连 |
| **工具执行超时** | 终止执行，向 Agent 返回超时错误信息 |
| **工具执行异常** | 捕获异常，记录日志，向 Agent 返回错误详情 |
| **上下文超限** | 按配置策略自动裁剪，保留系统提示和最近消息 |
| **配置文件错误** | 启动时校验，给出明确的错误定位和修复建议 |
| **用户中断**（Ctrl+C） | 优雅终止当前操作，保存会话状态后退出 |

---

## 9. 版本规划

### Phase 1 — MVP（最小可行产品）

- [x] F01: 会话管理（基础 CRUD + 持久化）
- [x] F04: 上下文管理（消息历史 + Token 裁剪）
- [x] F05: 工具调用（内置工具 file_read / file_write / shell_exec）
- [x] F06: LLM Provider（Ollama + OpenAI 兼容 API）
- [x] F07: CLI 基础（REPL + 单次执行 + 斜杠命令）

### Phase 2 — MCP 与 Skill

- [x] F02: MCP 工具注册（stdio 传输 + 工具发现 + 配置文件）
- [x] F03: Skill 技能调用（YAML 定义 + 加载 + 执行）
- [x] F04: 上下文管理增强（摘要策略 + 文件附件 + 统计）
- [ ] F07: CLI 增强（Markdown 渲染 + 管道输入）

### Phase 3 — 企业级增强

- [ ] F02: MCP SSE 传输 + 健康检查
- [ ] F03: Skill 社区安装 + 多轮工具链
- [ ] F05: 工具权限精细化 + 沙箱
- [ ] F06: 多 Provider 管理 + 运行时切换
- [ ] NF: 安全加固 + 审计日志

---

## 10. 验收标准

| 场景 | 验收条件 |
|---|---|
| **基本对话** | 启动 CLI，输入编程问题，Agent 正确回复 |
| **会话持久化** | 创建会话 → 退出 → 恢复会话 → 对话历史完整 |
| **工具调用** | 要求 Agent 读取文件 → Agent 调用 file_read → 返回正确内容 |
| **MCP 集成** | 配置 filesystem MCP Server → Agent 发现并调用其工具 |
| **Skill 执行** | 调用 code-review Skill → Agent 按模板执行并输出审查报告 |
| **上下文裁剪** | 长对话超出 Token 限制 → 自动裁剪旧消息 → 继续正常对话 |
| **模型切换** | 配置多个 Provider → 运行时切换 → 新模型正常工作 |
| **错误恢复** | LLM 调用失败 → 自动重试 → 最终失败时友好提示 |
