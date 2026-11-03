{
  lib,
  stdenv,
  buildDotnetModule,
  dotnetCorePackages,
  autoPatchelfHook,
  zlib,
  shaderc,
  fontconfig,
  SDL2,
  vulkan-loader,
  wayland,
  libxkbcommon,
  libdecor,
  libGL,
  libx11,
  libxcursor,
  libxrandr,
  libxi,
  libxext,
  libxinerama,
}:

let
  runtimeDeps = [
    SDL2
    vulkan-loader
    wayland
    libxkbcommon
    libdecor
    libGL
    libx11
    libxcursor
    libxrandr
    libxi
    libxext
    libxinerama
  ];
in
buildDotnetModule {
  pname = "moose";
  version = "0.2.0";

  src = lib.fileset.toSource {
    root = ../..;
    fileset = lib.fileset.unions [
      ../../Directory.Packages.props
      ../../global.json
      ../../Moose.slnx
      ../../Moose.Core
      ../../Moose.Graphics
      ../../Moose.UI
    ];
  };

  projectFile = "Moose.UI/Moose.UI.csproj";
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  selfContainedBuild = true;

  nativeBuildInputs = [
    autoPatchelfHook
    stdenv.cc
    shaderc
  ];

  buildInputs = [
    stdenv.cc.cc.lib
    zlib
  ];

  inherit runtimeDeps;
  passthru = { inherit runtimeDeps; };

  executables = [ "Moose.UI" ];

  makeWrapperArgs = [ "--prefix PATH : ${lib.makeBinPath [ fontconfig.bin ]}" ];

  postInstall = ''
    rm -f $out/lib/moose/*.dbg $out/lib/moose/*.pdb $out/lib/moose/libSDL2-2.0.so
    install -Dm644 ${../io.github.mih4n.Moose.desktop} $out/share/applications/io.github.mih4n.Moose.desktop
    install -Dm644 ${../io.github.mih4n.Moose.svg} $out/share/icons/hicolor/scalable/apps/io.github.mih4n.Moose.svg
  '';

  postFixup = ''
    mv $out/bin/Moose.UI $out/bin/moose
  '';

  meta = {
    description = "GPU accelerated terminal emulator";
    homepage = "https://github.com/Mih4n/Moose";
    license = lib.licenses.mit;
    mainProgram = "moose";
    platforms = [ "x86_64-linux" "aarch64-linux" ];
  };
}
