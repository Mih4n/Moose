#version 450

layout(location = 0) in vec2 local;
layout(location = 1) flat in vec2 halfSize;
layout(location = 2) flat in vec4 color;
layout(location = 3) flat in vec4 borderColor;
layout(location = 4) flat in vec2 params;
layout(location = 5) in vec2 uv;
layout(location = 6) flat in float glyph;
layout(location = 7) flat in int imageSlot;

// the glyph atlas, or the image of the batch when imageSlot is set
layout(set = 0, binding = 0) uniform sampler2D atlas;

layout(location = 0) out vec4 outColor;

float roundedBox(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void main() {
    if (imageSlot != 0) {
        vec4 texel = textureLod(atlas, uv, 0.0);
        outColor = vec4(texel.rgb * texel.a, texel.a) * color.a;
        return;
    }
    if (glyph > 0.5) {
        outColor = vec4(color.rgb * color.a, color.a) * textureLod(atlas, uv, 0.0).r;
        return;
    }
    float radius = min(params.x, min(halfSize.x, halfSize.y));
    vec2 unitsPerPixel = fwidth(local);
    float pixel = max(unitsPerPixel.x, unitsPerPixel.y);
    float d = roundedBox(local, halfSize, radius) / pixel;
    float border = params.y > 0.0 ? clamp(d + params.y / pixel + 0.5, 0.0, 1.0) : 0.0;
    vec4 fill = mix(vec4(color.rgb * color.a, color.a), vec4(borderColor.rgb * borderColor.a, borderColor.a), border);
    outColor = fill * clamp(0.5 - d, 0.0, 1.0);
}
