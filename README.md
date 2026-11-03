# Moose - a pretty fast, feature-rich, cross-platform (Linux only) :), GPU-based, thanks Claude, terminal emulator

The terminal is inspired by the Zed editor and its looks. Also, the main theme is based on [gruvbox-jd](https://github.com/jdinhify/vscode-theme-gruvbox), as I like to call it.

This project doesn't aim to replace your standard terminal emulator, but rather to fix some of my problems and also to learn something new about computer software. Part of it is vibe coded, part of it is coded manually, and part of it is copy-pasted from [guides](https://github.com/stymee/SilkVulkanTutorial) about Vulkan for Silk.NET.

If you like the project, feel free to add your own garbage code to it, or not garbage, I don't know your level of coding. As for me, I specialize in web dev and CRM systems, so yeah, you can guess the Vulkan part is almost entirely vibe coded and copied from guides.

Yeah, about the features of this piece of code.

## Save sessions

By adding these lines to your config, your session will be saved and restored on start. The config is stored at `$HOME/.config/moose`, or at `$XDG_CONFIG_HOME/moose` if `$XDG_CONFIG_HOME` is set.
```toml
[session]
save_on_exit = true
restore_on_start = true
```

You can also skip `restore_on_start` and restore stuff only when you actually want it:

```sh
moose --restore          # reopen the tabs that were open when Moose last closed
moose --no-restore       # start with one tab even if restore_on_start is on
moose --session work     # open a session saved under that name (or a layout file)
```

A layout is just a TOML file with the tabs you want to open, so you can keep one per project and stop typing `cd` and `dotnet watch` every morning:

```toml
active = 0               # index of the tab to focus

[[tabs]]
title = "server"         # optional, replaces the title the shell sets
working_directory = "~/project"
program = "fish"         # optional, [shell] program by default
args = ["-c", "dotnet watch"]
```

Named sessions live in `sessions/` next to your config. The `save_session`, `save_session_as` and `restore_session` actions do the same from inside the terminal.

## Command palette

`ctrl+shift+p` opens the command palette, like in Zed or VS Code. It has fuzzy search over all the actions, and you can switch tabs from it too, so you don't have to remember every keybinding (I don't).

## Tabs

`ctrl+shift+t` opens a tab, `ctrl+shift+w` closes it, `ctrl+shift+n` opens a new window. Tabs can be renamed with `rename_tab`, and the name sticks even if the shell tries to change the title.

## Vim-style navigation

Because reaching for the mouse is pain:

| keys | action |
|------|--------|
| `alt+h` / `alt+l` | previous / next tab |
| `alt+j` / `alt+k` | scroll a line down / up |
| `alt+d` / `alt+u` | scroll a page down / up |
| `alt+g` / `alt+shift+g` | scroll to top / bottom |

If some of these break your favorite TUI app, bind them to `none` and the chord goes back to the shell:

```toml
[[keybindings]]
keys = "alt+h"
action = "none"
```

## Keybindings

Built-in bindings always apply, and you can add your own on top to override them. The last binding for a chord wins.

```toml
[[keybindings]]
keys = "ctrl+c"
action = "copy_or_interrupt"   # copies when text is selected, otherwise ctrl+c goes to the shell
```

Available actions: `command_palette`, `copy`, `paste`, `copy_or_interrupt`, `new_tab`, `close_tab`, `rename_tab`, `next_tab`, `previous_tab`, `new_window`, `scroll_line_up`, `scroll_line_down`, `scroll_page_up`, `scroll_page_down`, `scroll_to_top`, `scroll_to_bottom`, `font_size_increase`, `font_size_decrease`, `font_size_reset`, `reload_config`, `save_session`, `save_session_as`, `restore_session`, `none`.

## Fonts and ligatures

Text is shaped with HarfBuzz, so programming ligatures work if your font has them. Characters your font doesn't have are taken from fallback fonts, and fontconfig picks the rest, so emoji and CJK don't turn into boxes. Box drawing characters are drawn by Moose itself, so powerline separators actually touch the cell edges.

```toml
[font]
family = "JetBrainsMonoNL Nerd Font"
size = 11.0                      # points, like kitty/alacritty
line_height = 1.0
ligatures = true
fallback = ["Noto Sans CJK JP"]
```

## Colors

The default colors match the kitty gruvbox palette. Colors can be written as `"#rgb"`, `"#rrggbb"`, `"#rrggbbaa"`, `"rgb(r, g, b)"`, `"rgba(r, g, b, a)"` or `[r, g, b(, a)]`, whatever you feel like today.

```toml
[colors]
foreground = "#ebdbb2"
background = "#282828"

[ui.colors]                      # tab bar and the rest of the UI
border = "#3c3836"
```

## Images

Moose speaks the [kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/), so `kitten icat`, yazi, chafa, timg and friends show real images instead of colored blocks:

```sh
kitten icat cat.png
```

Pretty much all of it works: PNG and raw RGB(A) data, zlib compression, chunked uploads, files, temp files and shared memory, placements with sizes, offsets and z-index (under the text, under the backgrounds or on top), relative placements, all the delete modes, animations (so GIFs from `icat` play) and unicode placeholders (`icat --unicode-placeholder`, which is what tmux and Neovim plugins use). Images scroll with the text and go into the scrollback with it.

What's missing: placeholders don't look at the underline color, so with several virtual placements of one image the first one is used.

## Config includes

If one config file is too long for you, split it:

```toml
include = ["keybindings.toml", "conf.d/*.toml"]
```

Paths are relative to the config, `*` matches file names, and missing files are skipped. The main file overrides included ones key by key, and `[[keybindings]]` from all files are combined. The config reloads when you save it, or with `ctrl+shift+f5`.

## Other small stuff

- frames are rendered only when something changes, so an idle terminal doesn't eat your GPU
- smooth scrolling and scrollbars
- drop files onto the window to paste their paths
- native AOT build, so it starts fast

## Installation

Every push to `main` builds an AppImage and a Flatpak in [GitHub Actions](https://github.com/Mih4n/Moose/actions), and tagged versions go to [releases](https://github.com/Mih4n/Moose/releases).

On Nix:

```sh
nix run github:Mih4n/Moose
```

From source (needs the .NET 10 SDK and `glslc`):

```sh
dotnet publish Moose.UI --configuration Release --runtime linux-x64 --output publish
./publish/Moose.UI
```

## License

MIT, do whatever you want.
