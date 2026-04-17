## 📋 CodeAgent 项目概述

**CodeAgent** 是一个基于 .NET 开发的智能助手 CLI 工具，旨在通过 LLM（大语言模型）与各种工具、技能的集成，提供代码辅助、命令执行和任务自动化能力。

---

## 🏗️ 核心架构

### 1. **项目结构**
```
CodeAgent/
├── src/
│   ├── CodeAgent.CLI/           # 命令行入口与用户交互
│   ├── CodeAgent.Core/          # 核心功能模块
│   │   ├── Agent/               # Agent 编排
│   │   ├── Skills/              # 技能引擎
│   │   ├── Tools/               # 工具注册与管理
│   │   ├── Context/             # 上下文管理
│   │   ├── Prompts/             # 提示词管理
│   │   └── Models/              # 数据模型
│   ├── CodeAgent.LLM/           # LLM 提供者适配
│   ├── CodeAgent.MCP/           # MCP 客户端管理
│   └── CodeAgent.Infrastructure/  # 基础设施（配置、存储等）
├── tests/                       # 测试
└── docs/                        # 文档
```

---

## 🎯 核心组件

### 1. **CliApp** (`Program.cs:104-592`)
- **作用**: CLI 应用主类，实现 `ICliApp` 接口
- **关键方法**:
  - `RunAsync()` - 主运行流程
  - `RunRepl()` -  REPL 模式
  - `HandleUserMessage()` - 处理用户输入
  - `RegisterBuiltInTools()` - 注册内置工具
  - `HandleSkillCommand()`, `UseSkill()` - 技能管理
  - `HandleMcpCommand()`, `ReloadMcpServers()` - MCP 服务器管理

### 2. **AgentOrchestrator** (`AgentOrchestrator.cs:25-335`)
- **作用**: 智能体编排核心，实现 `IAgentOrchestrator` 接口
- **关键方法**:
  - `ProcessAsync()` / `ProcessStreamAsync()` - 异步处理消息流
  - `CallLlmAsync()` - 调用 LLM 服务
  - `ExecuteToolCallsAsync()` - 执行工具调用
  - `ConvertToChatMessages()` - 消息转换
  - `ConvertToToolDefinitions()` - 工具定义转换

### 3. **SkillEngine** (`SkillEngine.cs:6-188`)
- **作用**: 技能引擎，实现 `ISkillEngine` 接口
- **关键方法**:
  - `LoadSkillsAsync()` - 加载技能
  - `GetAllSkills()`, `GetSkill()` - 技能查询
  - `PrepareExecutionAsync()` - 准备执行环境
  - `RenderPrompt()` / `GetSystemPrompt()` - 提示词渲染
  - `AddOrUpdateSkillAsync()`, `DeleteSkillAsync()` - 技能管理
  - `FindSimilarSkill()` - 相似技能推荐

### 4. **McpClientManager & McpClient** (`McpClientManager.cs`, `McpClient.cs`)
- **作用**: MCP (Model Context Protocol) 客户端管理
- **关键功能**:
  - 连接/断开 MCP 服务器
  - 列出可用工具 (`ListToolsAsync()`)
  - 调用工具 (`CallToolAsync()`)
  - 列出资源和提示词
  - 支持多种传输协议 (StdioTransport, SseTransport)

### 5. **OpenAICompatibleProvider** (`OpenAICompatibleProvider.cs:6-217`)
- **作用**: LLM 提供者适配层，实现 `ILlmProvider` 接口
- **关键方法**:
  - `CompleteAsync()` - 同步补全
  - `CompleteStreamAsync()` - 流式补全
- **支持**: OpenAI 兼容的 LLM 服务

---

## 🔧 工具系统

### **ITool 接口** (`ITool.cs`)
```csharp
interface ITool {
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    Task<ToolResult> ExecuteAsync(ToolCallItem call, IToolContext context);
}
```

### **内置工具示例**
- **ShellExecTool** - 命令行执行工具
  - `ExecuteAsync()` - 执行 Shell 命令

### **McpToolAdapter** - MCP 工具适配器
- 将 MCP 工具转换为 `ITool` 接口实现

---

## 📦 配置系统

### **ConfigManager** (`ConfigManager.cs`)
- **加载配置**: `LoadAsync()`
  - `AppConfig` - 应用配置
  - `LlmConfig` - LLM 配置
- **保存配置**: `SaveAsync()`
- **默认配置**: `CreateDefaultConfig()`

---

## 🔄 核心执行流程

```
用户输入 → CliApp.HandleUserMessage()
    ↓
CliApp → AgentOrchestrator.ProcessAsync()
    ↓
AgentOrchestrator.CallLlmAsync() → OpenAICompatibleProvider.CompleteAsync()
    ↓
LLM 响应 → AgentOrchestrator.ExecuteToolCallsAsync()
    ↓
工具执行 (ShellExecTool / MCP Tools / Skills)
    ↓
结果返回 → CliApp.HandleUserMessage() → 用户
```

---

## 🎨 特性亮点

1. **多协议支持**: 支持 MCP 协议的各种传输方式 (stdio, sse)
2. **技能系统**: 可插拔的技能引擎，支持技能加载、管理、推荐
3. **工具注册**: 统一的工具接口，支持内置工具和 MCP 工具
4. **LLM 兼容**: 支持 OpenAI 兼容的 LLM 服务
5. **会话管理**: Session 模型支持会话持久化
6. **CLI 交互**: 丰富的命令行交互功能 (技能、MCP、命令等)
7. **上下文管理**: ContextManager 支持会话上下文维护

---

## 📚 文档资源

- **README.md**: 项目介绍和功能说明
- **docs/CodeAgent-CLI-SRS.md**: 软件需求规格说明书 (250 行)

---
