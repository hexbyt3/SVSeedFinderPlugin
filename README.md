![GitHub Release](https://img.shields.io/github/v/release/hexbyt3/Svseedfinderplugin)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/hexbyt3/svseedfinderplugin/total?color=violet)

# SV Seed Finder Plugin for PKHeX

A high-performance seed finding plugin for PKHeX that helps you search for specific Generation 9 Tera Raid encounters in Pokemon Scarlet & Violet. Now with **multi-core processing** and **advanced scale validation**!

## About

This plugin started as part of my custom [ALM](https://github.com/hexbyt3/ALM4SysBot)(AutoLegalityMod) project and has been extracted into a standalone tool for users who just need seed finding functionality. It searches through raid seeds to find encounters that match your exact criteria - whether you're looking for a specific shiny, perfect IVs, or a particular nature.

The tool supports all raid types including standard Tera Raids, event distributions, and 7-star Unrivaled raids.

**Author:** [@hexbyt3](https://github.com/hexbyt3)

## What's New

### Latest Update
- **🚀 Multi-Core Parallel Processing** - Searches now use all available CPU cores for massive speed improvements (3-14x faster!)
- **📏 Advanced Scale Validation** - Smart detection and validation of fixed scales for Mighty Raids and size-constrained encounters
- **✨ Enhanced UI Feedback** - Real-time constraint highlighting and detailed warning messages
- **📊 Performance Monitoring** - Status bar shows active core count and real-time processing speed

## Screenshots
<img width="752" height="512" alt="image" src="https://github.com/user-attachments/assets/1ab80f84-9c92-425a-b0d7-7abc40acff77" />
<img width="400" height="412" alt="image" src="https://github.com/user-attachments/assets/2024ccca-a032-4f20-bdce-164f09cd94e7" />

## Features

- **Ultra-fast multi-core processing** - Utilizes all CPU cores for blazing fast searches
- Search through millions of seeds with customizable ranges
- Support for all Gen 9 raid types (Paldea, Kitakami, Blueberry, Events, and 7-star raids)
- Real-time validation that prevents impossible search combinations
- Quick species search with type-ahead filtering
- Detailed search criteria:
  - IV ranges for all stats
  - Nature selection
  - Ability preferences
  - Gender requirements
  - Shiny status (including square/star specific)
  - **Scale/size filtering (0-255) with validation**
- **Smart scale validation for Mighty Raids and fixed-scale encounters**
- Automatic Met Date correction for 7-star Mighty raids
- Export results to CSV for spreadsheet analysis
- Dark theme compatible with highlighted shiny results
- **Live preview panel showing full Pokemon details**

## Requirements

- PKHeX (latest version recommended)
- Windows 10 or 11
- .NET 9.0 runtime (usually already installed if you can run PKHeX)

## Installation

1. Download the latest `SVSeedFinderPlugin.dll` from the releases page
2. Place it in PKHeX's `plugins` folder (create the folder if it doesn't exist)
3. Restart PKHeX
4. You'll find "SV Seed Finder" in the Tools menu

## Usage

### Basic Search
1. Open the plugin from Tools > SV Seed Finder
2. Select your target Pokemon species
3. Choose the form if applicable
4. Set your search criteria
5. Click Search

### Understanding Encounter Constraints

Some encounters have fixed properties that can't be changed. The plugin will highlight incompatible criteria in red and show warnings in the status bar. For example:
- 7-star raids always have 6 perfect IVs
- Event raids might have fixed natures or abilities
- Some raids are shiny-locked
- **Mighty Raids and some events have fixed scale values**
- **Certain encounters have scale type constraints (XS, S, Average, L, XL)**

When you select an encounter with constraints, the plugin will:
- Highlight invalid selections in pink/red
- Show warning messages in the status bar
- Prevent searches that would never find results

### Search Tips

- Start with broader criteria and narrow down
- Use specific seed ranges if you're checking a particular set
- The "Any Encounter" option searches all possible raids for that species
- Double-click any result to load it directly into PKHeX to quickly export it as a file or load onto your switch via LiveHex.

### Performance

**The plugin now uses multi-core parallel processing for maximum speed!**

Performance scales with your CPU:
- **Single-core**: ~10,000 seeds/second (legacy mode)
- **4-core CPU**: ~35,000-40,000 seeds/second
- **8-core CPU**: ~70,000-80,000 seeds/second
- **16-core CPU**: ~140,000-160,000 seeds/second

The plugin automatically detects and uses all available CPU cores. With modern processors, you can search millions of seeds in minutes rather than hours!

Even with these improvements, searching the entire seed range (4.3 billion seeds) takes time:
- 8-core CPU: ~15 hours for full range
- 16-core CPU: ~7-8 hours for full range

Tips for optimal performance:
- Set reasonable result limits (stop after finding what you need)
- Use specific seed ranges when possible
- The status bar shows core count and real-time speed
- You can still cancel searches at any time

## Building from Source

If you want to build the plugin yourself:

1. Clone this repository
2. Open `SVSeedFinderPlugin.sln` in Visual Studio 2022 or later
4. Build in Release mode
5. The compiled DLL will be in `bin/Release/net9.0-windows/`

## Technical Details

This plugin uses PKHeX.Core's raid generation system to ensure all found seeds produce legal Pokemon. 

### Search Algorithm
- **Parallel processing** across all CPU cores using thread-safe collections
- Validates each seed against the selected encounter's constraints
- Generates the full Pokemon data to check all properties
- Ensures proper correlation between seed and Pokemon data
- **Smart chunking** to minimize thread overhead while maximizing throughput

### Scale Validation System
- Detects fixed scale values for Mighty Raids using `SizeType9.VALUE`
- Validates scale ranges for size-constrained encounters (XS, S, M, L, XL)
- Real-time visual feedback for invalid scale selections
- Prevents searches with impossible scale criteria

### Optimizations
- Thread-safe `ConcurrentBag` for result collection
- Atomic operations for progress tracking
- Batched UI updates to reduce overhead
- Early termination when max results are found

## Credits

This plugin wouldn't exist without the incredible work of the PKHeX team:
- **Kurt (@kwsch)** - For creating and maintaining PKHeX, and for the comprehensive encounter database that makes this possible
- **Manu (@manu098vm)** - For the raid seed research and RNG implementation that powers the search functionality
- All PKHeX contributors who have helped build the foundation this relies on

## License

This project is licensed under the MIT License. See the LICENSE file for details.

## Contributing

Issues and pull requests are welcome. If you find a bug or have a feature request, please check existing issues first.

## Disclaimer

This tool is for educational purposes. Please respect the game and other players when using modifications.

---

For more information about PKHeX, visit the [official repository](https://github.com/kwsch/PKHeX).
