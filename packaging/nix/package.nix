{
  lib,
  stdenv,
  buildDotnetModule,
  dotnetCorePackages,
  autoPatchelfHook,
  shaderc,
  fontconfig,
  glfw,
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
    glfw
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
  version = "0.1.0";

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
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  nativeBuildInputs = [
    autoPatchelfHook
    shaderc
  ];

  buildInputs = [ stdenv.cc.cc.lib ];

  inherit runtimeDeps;
  passthru = { inherit runtimeDeps; };

  executables = [ "Moose.UI" ];

  makeWrapperArgs = [ "--prefix PATH : ${lib.makeBinPath [ fontconfig.bin ]}" ];

  postInstall = ''
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
