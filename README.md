# CodeAgent CLI

A local-first, extensible command-line AI programming assistant built with .NET 8.

## Features

- **Interactive REPL** - Multi-turn conversational AI assistance
- **MCP Integration** - Connect to Model Context Protocol servers for extended tools
- **Skill System** - Define reusable prompt templates and tool chains as YAML skills
- **Session Management** - Create, resume, and persist conversation sessions
- **Built-in Tools** - file_read, file_write, shell_exec, glob, grep
- **Local LLM Support** - Works with Ollama or any OpenAI API compatible endpoint

## Requirements

- .NET 8.0 SDK
- Ollama (recommended) or any OpenAI API compatible LLM service

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

Create `~/.codeagent/config.yaml`:

```yaml
llm:
  providers:
    ollama:
      type: openai_compatible
      base_url: "http://localhost:11434/v1"
      api_key: "ollama"
      model: "qwen2.5-coder:7b"
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