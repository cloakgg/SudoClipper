# OpenFrog - The Free, Friendly, Frog-Powered AI Agent

## Overview

**OpenFrog** is a free and open-source AI agent CLI for Windows and Linux, delivered as a simple, polished command-line application with a friendly frog mascot. Built with free and open-source tools, the goal is an agent that is easy to install, easy to use, and able to actually do things on your computer when asked.

## Core Principles

- **Simple and reliable** - The CLI is clean, minimal, and consistent
- **Cross-platform** - Everything works on both Windows and Linux
- **Free-first** - Start using OpenFrog with free providers and a free API key, no paid account required
- **Safe by default** - Anything risky asks for confirmation first

## Features

### Terminal Interface

- **Green and black theme** - Consistent colors for readability
- **Frog mascot** - Shows emotions: idle, thinking, working, success, error, confirm
- **Clean layout** - Header with title, conversation area, prompt at the bottom
- **Status indicators** - Current provider, model, connection state, errors in plain language

### Slash Commands

| Command | Description |
|---------|-------------|
| `/help` | Show this help screen |
| `/clear` | Clear the screen |
| `/exit` | Exit OpenFrog |
| `/settings` | Open settings menu |
| `/model` | Select AI model |
| `/quality` | Select quality mode (Fast/Balanced/Best) |
| `/keys` | Manage API keys |
| `/free` | Show free model options |
| `/skills` | List available skills |
| `/frog` | Change frog mascot mood |
| `/doctor` | Run diagnostics check |
| `/version` | Show version info |
| `/init` | Initialize project |

### Frog Mascot Moods

- `idle` - Frog is idle and happy (`██`)
- `thinking` - Frog is thinking (`(o o)`)
- `working` - Frog is working (`( )`)
- `success` - Frog succeeded! (`• •`)
- `error` - Frog encountered an error (`xx`)
- `confirm` - Frog asks for confirmation (`• •`)
- `sleepy` - Frog is sleepy (`_ _`)

### Skills Library

| Skill | Description |
|-------|-------------|
| `read-file` | Read and display file contents |
| `write-file` | Write text to a file |
| `edit-code` | Edit code across a project |
| `run-cmd` | Run shell commands |
| `git-status` | Check git status and show changes |
| `summarize` | Summarize text/documents |
| `web-search` | Search the web for information |
| `system-info` | Display system information (CPU, memory, disk) |

### Provider Support

- **OpenAI-compatible** endpoints (api.openai.com, openrouter.ai, litellm.com, 9router.top)
- **Anthropic-compatible** endpoints
- **Google Gemini** endpoints
- **Local models** (Ollama, LM Studio)
- **Free providers** that require no API key

### Configuration

- Global config file (`.openfrogrc`) with model, quality, provider, and API key
- Project settings override global settings
- Configurable frog default mood
- Skill enable/disable settings

## Installation

1. Clone or download this repository
2. Make the main script executable: `chmod +x openfrog/openfrog`
3. Run: `./openfrog/openfrog`
4. First run will guide you through provider setup

Or install via the provided install script (when available).

## Quick Start

```bash
# 1. Initialize a new OpenFrog project
openfrog/openfrog /init

# 2. Set your model and provider
openfrog/openfrog /model gpt-4o
openfrog/openfrog /keys

# 3. Use skills
openfrog/openfrog read-file ./package.json
openfrog/openfrog git-status
openfrog/openfrog summarize "Your text here"

# 4. Chat with OpenFrog
# Just type your request at the prompt:
# - "Hello!" - Friendly greeting
# - "Read this file" - Read a file
# - "Summarize this text" - Get a summary

# 5. Change frog mood
openfrog/openfrog /frog working
```

## Theme

- **Primary colors**: Near-black background (`#0A0F0A`), frog green accents (`#39D353`, `#2ECC40`)
- **Text**: Soft off-white or light green
- **Warning**: Amber (`#FFA500`)
- **Error**: Soft red (`#FF5555`)
- **High-contrast mode** available for terminals with limited color support

## Philosophy

OpenFrog matches the basics expected in 2026 CLI agents but wins on:
- True free-first philosophy (no paid account required for basic use)
- Real sandboxing and safety guardrails
- Visual frog mascot with personality that means something
- User memory that belongs to the user (plain text, git-able)
- Verified work, not just confident work

## Roadmap (Planned Features)

- Pond view: Visual multi-agent control room
- Time Machine: Automatic snapshots before every action
- Sandbox mode: Container-isolated command execution
- Skill marketplace: Community-shared skills and integrations
- Remote control: Check and approve actions from phone/Telgram/Discord
- Multi-model teamwork: Different models for different roles

## License

Permissive open-source license - free to use, modify, and distribute.

## Credits

Built with Bash, using ASCII art for the frog mascot. Inspired by existing coding CLIs and the free-first philosophy.