# CodeAgent CLI

A local-first, extensible command-line AI programming assistant built with .NET 8.

## Features

- **Interactive REPL** - Multi-turn conversational AI assistance
- **MCP Integration** - Connect to Model Context Protocol servers for extended tools
- **Skill System** - Define reusable prompt templates and tool chains as YAML skills
- **Session Management** - Create, resume, and persist conversation sessions
- **Built-in Tools** - file_read, file_write, shell_exec, glob, grep
  - **Windows Note**: `shell_exec` tool runs commands via WSL (Windows Subsystem for Linux)
- **Local LLM Support** - Works with Ollama or any OpenAI API compatible endpoint

## Requirements

- .NET 8.0 SDK
- Ollama (recommended) or any OpenAI API compatible LLM service
- **Windows**: WSL (Windows Subsystem for Linux) required for shell script execution

### Windows: WSL Installation

If you are on Windows, you need WSL to use the `shell_exec` tool:

```powershell
# Run as Administrator in PowerShell
wsl --install

# Restart your computer after installation completes

# Verify WSL is installed
wsl --status

# Install a Linux distribution (e.g., Ubuntu)
wsl --install -d Ubuntu

# Set default user
```

## Quick Start

```bash
# Clone and build
dotnet build src/CodeAgent.CLI/CodeAgent.CLI.csproj -c Release

# Run the CLI
dotnet run src/CodeAgent.CLI/CodeAgent.CLI.csproj

# Or install globally
dotnet tool install -g src/CodeAgent.CLI/CodeAgent.CLI.csproj
codeagent "Hello, help me write a hello world program"
```

## Configuration

All configuration files are stored in `~/.codeagent/`.

### LLM Provider Configuration

Create `~/.codeagent/config.yaml`:

```yaml
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

    gpt4o:
      type: openai_compatible
      base_url: "https://api.openai.com/v1"
      api_key: "${OPENAI_API_KEY}"
      model: "gpt-4o"
```

You can switch models with `/model <name>` in interactive mode.

### MCP Server Configuration

Create `~/.codeagent/mcp.json`:

#### stdio Transport

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@anthropic/mcp-filesystem", "/path/to/dir"],
      "transport": "stdio",
      "enabled": true
    }
  }
}
```

#### SSE Transport

```json
{
  "mcpServers": {
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

Supported transports: `stdio`, `sse`.

List MCP servers with `/mcp list`.

### Tool Permissions

Configure tool execution behavior in `config.yaml`:

```yaml
tools:
  max_iterations: 10
  default_timeout: 30
  permissions:
    file_read: allow
    file_write: confirm
    shell_exec: confirm
    glob: allow
    grep: allow
    mcp_default: confirm
```

Permission options:
- `allow` - Execute without confirmation
- `deny` - Refuse to execute
- `confirm` - Ask for user confirmation (default)

### Context Management

```yaml
context:
  max_tokens: 128000
  truncation_strategy: truncate_oldest
  reserve_tokens: 4096
```

### System Prompt

```yaml
system_prompt: |
  You are a professional programming assistant.
  Help users write code, debug issues, and explain concepts.
  Reply in Chinese when the user communicates in Chinese.
```

## Architecture

```
CodeAgent.CLI
├── CodeAgent.Core      # Agent orchestrator, tools, context, sessions
├── CodeAgent.LLM       # LLM provider abstraction
├── CodeAgent.MCP       # MCP client and transport layer
└── CodeAgent.Infrastructure  # Config, storage, logging
```

## Commands

```
/help        Show help
/clear       Clear context
/context     Show token stats
/model       Switch LLM model
/skill       Use a skill
/session     Manage sessions
/mcp         Manage MCP servers
/tools       List available tools
/exit        Exit
```

## License

MIT