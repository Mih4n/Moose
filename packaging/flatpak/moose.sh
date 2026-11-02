#!/bin/sh
export SHELL=/app/bin/moose-host-shell
export XDG_CONFIG_HOME="$HOME/.config"
export LD_LIBRARY_PATH="/app/lib/moose${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec /app/lib/moose/Moose.UI "$@"
