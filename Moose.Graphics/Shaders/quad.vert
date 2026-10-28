#version 450

layout(location = 0) in vec4 inRect;
layout(location = 1) in vec4 inColor;
layout(location = 2) in vec4 inBorderColor;
layout(location = 3) in vec2 inParams;
layout(location = 4) in vec4 inUv;

layout(push_constant) uniform Push { vec2 viewport; } push;

layout(location = 0) out vec2 outLocal;
layout(location = 1) flat out vec2 outHalfSize;
layout(location = 2) flat out vec4 outColor;
layout(location = 3) flat out vec4 outBorderColor;
layout(location = 4) flat out vec2 outParams;
layout(location = 5) out vec2 outUv;
layout(location = 6) flat out float outGlyph;

void main() {
    vec2 corner = vec2(gl_VertexIndex & 1, gl_VertexIndex >> 1);
    vec2 pos = inRect.xy + corner * inRect.zw;
    gl_Position = vec4(pos / push.viewport * 2.0 - 1.0, 0.0, 1.0);
    outLocal = (corner - 0.5) * inRect.zw;
    outHalfSize = inRect.zw * 0.5;
    outColor = inColor;
    outBorderColor = inBorderColor;
    outParams = inParams;
    outUv = inUv.xy + corner * inUv.zw;
    outGlyph = inUv.z > 0.0 ? 1.0 : 0.0;
}
