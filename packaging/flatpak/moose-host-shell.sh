#!/bin/sh
shell="$(flatpak-spawn --host getent passwd "$(id -un)" | cut -d: -f7)"
exec flatpak-spawn --host --watch-bus \
  --env=TERM="${TERM:-xterm-256color}" \
  --env=COLORTERM="${COLORTERM:-truecolor}" \
  --directory="${PWD:-$HOME}" \
  "${shell:-/bin/sh}" -l
