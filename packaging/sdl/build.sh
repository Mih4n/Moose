#!/usr/bin/env bash
set -euo pipefail

out=$(realpath "$1")
work=$(mktemp -d)

git clone --depth 1 --branch release-3.4.18 https://github.com/libsdl-org/SDL.git "$work/sdl3"
git clone --depth 1 --branch release-2.32.74 https://github.com/libsdl-org/sdl2-compat.git "$work/sdl2-compat"

cmake -S "$work/sdl3" -B "$work/sdl3-build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$work/prefix" \
  -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_AUDIO=OFF -DSDL_CAMERA=OFF -DSDL_GPU=OFF -DSDL_RENDER=OFF
cmake --build "$work/sdl3-build"
cmake --install "$work/sdl3-build"

cmake -S "$work/sdl2-compat" -B "$work/sdl2-compat-build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_PREFIX_PATH="$work/prefix" -DSDL2COMPAT_TESTS=OFF
cmake --build "$work/sdl2-compat-build"

cp -L "$work/prefix/lib/libSDL3.so.0" "$out/libSDL3.so.0"
cp -L "$work/sdl2-compat-build/libSDL2-2.0.so.0" "$out/libSDL2-2.0.so"
