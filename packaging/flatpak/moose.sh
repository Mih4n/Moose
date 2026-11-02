#!/bin/sh
export SHELL=/app/bin/moose-host-shell
export XDG_CONFIG_HOME="$HOME/.config"
exec /app/lib/moose/Moose.UI "$@"
