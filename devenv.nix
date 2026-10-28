{ pkgs, lib, ... }:

let
  runtimeLibs = with pkgs; [
    glfw
    vulkan-loader
    wayland
    libxkbcommon
    libdecor
    libGL
    xorg.libX11
    xorg.libXcursor
    xorg.libXrandr
    xorg.libXi
    xorg.libXext
    xorg.libXinerama
  ];
in
{
  languages.dotnet = {
    enable = true;
    package = pkgs.dotnet-sdk_10;
  };

  packages = with pkgs; [
    vulkan-tools
    vulkan-validation-layers
    shaderc
  ];

  env.LD_LIBRARY_PATH = lib.makeLibraryPath runtimeLibs;
  env.VK_LAYER_PATH = "${pkgs.vulkan-validation-layers}/share/vulkan/explicit_layer.d";
}
