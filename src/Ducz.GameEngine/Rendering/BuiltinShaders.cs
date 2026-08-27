namespace Ducz.Rendering;

/// <summary>GLSL sources for the engine's built-in shaders.</summary>
internal static class BuiltinShaders
{
    public const int MaxBones = 128;
    public const int MaxPointLights = 16;
    public const int MaxSpotLights = 8;

    // ------------------------------------------------------------------
    // Standard lit shader (Blinn-Phong, shadows, fog). Compile once plain
    // and once with the SKINNED define.
    // ------------------------------------------------------------------

    public const string StandardVertex = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aUV;
layout (location = 3) in vec4 aColor;
#ifdef SKINNED
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;
uniform mat4 uBones[128];
#endif

uniform mat4 uModel;
uniform mat4 uNormalMatrix;
uniform mat4 uViewProj;
uniform mat4 uLightSpace;
uniform vec2 uUvScale;
uniform vec2 uUvOffset;

out vec3 vWorldPos;
out vec3 vNormal;
out vec2 vUV;
out vec4 vColor;
out vec4 vLightSpacePos;

void main()
{
    vec4 localPos = vec4(aPos, 1.0);
    vec3 localNormal = aNormal;

#ifdef SKINNED
    mat4 skin = aWeights.x * uBones[int(aJoints.x)] +
                aWeights.y * uBones[int(aJoints.y)] +
                aWeights.z * uBones[int(aJoints.z)] +
                aWeights.w * uBones[int(aJoints.w)];
    localPos = skin * localPos;
    localNormal = mat3(skin) * localNormal;
#endif

    vec4 worldPos = uModel * localPos;
    vWorldPos = worldPos.xyz;
    vNormal = normalize(mat3(uNormalMatrix) * localNormal);
    vUV = aUV * uUvScale + uUvOffset;
    vColor = aColor;
    vLightSpacePos = uLightSpace * worldPos;
    gl_Position = uViewProj * worldPos;
}";

    public const string StandardFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vUV;
in vec4 vColor;
in vec4 vLightSpacePos;

out vec4 FragColor;

uniform sampler2D uAlbedoTex;
uniform sampler2D uShadowMap;
uniform sampler2D uNormalMap;      // tangent-space normal map (optional)
uniform sampler2D uRoughnessMap;   // grayscale, white = matte (optional)
uniform bool uHasNormalMap;
uniform bool uHasRoughnessMap;
uniform float uNormalStrength;

uniform vec4 uAlbedo;
uniform float uSpecularStrength;
uniform float uShininess;
uniform vec3 uEmission;
uniform bool uUnshaded;
uniform float uAlphaCutout;
uniform bool uReceiveShadows;

uniform vec3 uCameraPos;
uniform vec3 uAmbientColor;

// Directional light
uniform bool uDirLightEnabled;
uniform vec3 uDirLightDir;
uniform vec3 uDirLightColor;
uniform bool uShadowsEnabled;

// Point lights
uniform int uPointLightCount;
uniform vec3 uPointLightPos[16];
uniform vec3 uPointLightColor[16];
uniform float uPointLightRange[16];

// Spot lights
uniform int uSpotLightCount;
uniform vec3 uSpotLightPos[8];
uniform vec3 uSpotLightDir[8];
uniform vec3 uSpotLightColor[8];
uniform float uSpotLightRange[8];
uniform float uSpotLightAngleCos[8];
uniform float uSpotLightSoftness[8];

// Fog
uniform bool uFogEnabled;
uniform vec3 uFogColor;
uniform float uFogStart;
uniform float uFogEnd;

float SampleShadow(vec3 normal)
{
    if (!uShadowsEnabled || !uReceiveShadows)
        return 0.0;

    vec3 proj = vLightSpacePos.xyz / vLightSpacePos.w;
    proj = proj * 0.5 + 0.5;
    if (proj.z > 1.0 || proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0)
        return 0.0;

    float bias = max(0.003 * (1.0 - dot(normal, -uDirLightDir)), 0.0008);
    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float depth = texture(uShadowMap, proj.xy + vec2(x, y) * texel).r;
            shadow += proj.z - bias > depth ? 1.0 : 0.0;
        }
    }
    return shadow / 9.0;
}

// Cotangent frame from screen-space derivatives, so normal maps work without a per-vertex
// tangent attribute (Schuler's Normal Mapping Without Precomputed Tangents).
mat3 CotangentFrame(vec3 N, vec3 p, vec2 uv)
{
    vec3 dp1 = dFdx(p);
    vec3 dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);
    vec3 dp2perp = cross(dp2, N);
    vec3 dp1perp = cross(N, dp1);
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = inversesqrt(max(dot(T, T), dot(B, B)) + 1e-8);
    return mat3(T * invmax, B * invmax, N);
}

// Global set once per fragment so every light uses the same surface response.
float gSpecular;
float gShininess;

vec3 BlinnPhong(vec3 lightDir, vec3 lightColor, vec3 normal, vec3 viewDir, vec3 albedo)
{
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 halfway = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfway), 0.0), gShininess) * gSpecular;
    return (albedo * diff + vec3(spec)) * lightColor;
}

void main()
{
    vec4 texColor = texture(uAlbedoTex, vUV);
    vec4 baseColor = texColor * uAlbedo * vColor;

    if (uAlphaCutout > 0.0 && baseColor.a < uAlphaCutout)
        discard;

    gSpecular = uSpecularStrength;
    gShininess = uShininess;
    if (uHasRoughnessMap)
    {
        // Roughness map: white = rough/matte, black = smooth/glossy.
        float rough = clamp(texture(uRoughnessMap, vUV).r, 0.02, 1.0);
        gShininess = max(1.0, uShininess * (1.0 - rough) * 2.0);
        gSpecular = uSpecularStrength * (1.0 - rough * 0.85);
    }

    if (uUnshaded)
    {
        vec3 unlit = baseColor.rgb + uEmission;
        if (uFogEnabled)
        {
            float dist = length(vWorldPos - uCameraPos);
            float fog = clamp((dist - uFogStart) / (uFogEnd - uFogStart), 0.0, 1.0);
            unlit = mix(unlit, uFogColor, fog);
        }
        FragColor = vec4(unlit, baseColor.a);
        return;
    }

    vec3 normal = normalize(vNormal);
    if (!gl_FrontFacing)
        normal = -normal;
    vec3 viewDir = normalize(uCameraPos - vWorldPos);

    if (uHasNormalMap)
    {
        vec3 sampled = texture(uNormalMap, vUV).rgb * 2.0 - 1.0;
        sampled.xy *= uNormalStrength;
        normal = normalize(CotangentFrame(normal, vWorldPos, vUV) * normalize(sampled));
    }

    vec3 result = uAmbientColor * baseColor.rgb;

    if (uDirLightEnabled)
    {
        float shadow = SampleShadow(normal);
        result += (1.0 - shadow) * BlinnPhong(-uDirLightDir, uDirLightColor, normal, viewDir, baseColor.rgb);
    }

    for (int i = 0; i < uPointLightCount; i++)
    {
        vec3 toLight = uPointLightPos[i] - vWorldPos;
        float dist = length(toLight);
        if (dist > uPointLightRange[i]) continue;
        float atten = 1.0 - dist / uPointLightRange[i];
        atten *= atten;
        result += BlinnPhong(toLight / dist, uPointLightColor[i], normal, viewDir, baseColor.rgb) * atten;
    }

    for (int i = 0; i < uSpotLightCount; i++)
    {
        vec3 toLight = uSpotLightPos[i] - vWorldPos;
        float dist = length(toLight);
        if (dist > uSpotLightRange[i]) continue;
        vec3 lightDir = toLight / dist;
        float cosAngle = dot(-lightDir, uSpotLightDir[i]);
        if (cosAngle < uSpotLightAngleCos[i]) continue;
        float atten = 1.0 - dist / uSpotLightRange[i];
        atten *= atten;
        float cone = clamp((cosAngle - uSpotLightAngleCos[i]) / max(uSpotLightSoftness[i], 0.001), 0.0, 1.0);
        result += BlinnPhong(lightDir, uSpotLightColor[i], normal, viewDir, baseColor.rgb) * atten * cone;
    }

    result += uEmission;

    if (uFogEnabled)
    {
        float dist = length(vWorldPos - uCameraPos);
        float fog = clamp((dist - uFogStart) / (uFogEnd - uFogStart), 0.0, 1.0);
        result = mix(result, uFogColor, fog);
    }

    FragColor = vec4(result, baseColor.a);
}";

    // ------------------------------------------------------------------
    // Depth-only shader for the shadow map.
    // ------------------------------------------------------------------

    public const string DepthVertex = @"#version 330 core
layout (location = 0) in vec3 aPos;
#ifdef SKINNED
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;
uniform mat4 uBones[128];
#endif
uniform mat4 uModel;
uniform mat4 uLightSpace;

void main()
{
    vec4 localPos = vec4(aPos, 1.0);
#ifdef SKINNED
    mat4 skin = aWeights.x * uBones[int(aJoints.x)] +
                aWeights.y * uBones[int(aJoints.y)] +
                aWeights.z * uBones[int(aJoints.z)] +
                aWeights.w * uBones[int(aJoints.w)];
    localPos = skin * localPos;
#endif
    gl_Position = uLightSpace * uModel * localPos;
}";

    public const string DepthFragment = @"#version 330 core
void main() { }";

    // ------------------------------------------------------------------
    // Procedural gradient sky, drawn as a fullscreen triangle.
    // ------------------------------------------------------------------

    public const string SkyVertex = @"#version 330 core
out vec2 vNdc;
void main()
{
    // Fullscreen triangle from gl_VertexID, no buffers needed.
    vec2 pos = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2) * 2.0 - 1.0;
    vNdc = pos;
    gl_Position = vec4(pos, 1.0, 1.0);
}";

    public const string SkyFragment = @"#version 330 core
in vec2 vNdc;
out vec4 FragColor;

uniform mat4 uInvViewProj;
uniform vec3 uTopColor;
uniform vec3 uHorizonColor;
uniform vec3 uGroundColor;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform bool uSunEnabled;

void main()
{
    vec4 nearPoint = uInvViewProj * vec4(vNdc, -1.0, 1.0);
    vec4 farPoint  = uInvViewProj * vec4(vNdc,  1.0, 1.0);
    vec3 dir = normalize(farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w);

    vec3 color;
    if (dir.y >= 0.0)
    {
        float t = pow(clamp(dir.y, 0.0, 1.0), 0.6);
        color = mix(uHorizonColor, uTopColor, t);
    }
    else
    {
        float t = pow(clamp(-dir.y, 0.0, 1.0), 0.4);
        color = mix(uHorizonColor, uGroundColor, t);
    }

    if (uSunEnabled)
    {
        float sun = max(dot(dir, -uSunDir), 0.0);
        color += uSunColor * pow(sun, 512.0) * 4.0;
        color += uSunColor * pow(sun, 8.0) * 0.15;
    }

    FragColor = vec4(color, 1.0);
}";

    // ------------------------------------------------------------------
    // Debug lines.
    // ------------------------------------------------------------------

    public const string LineVertex = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec4 aColor;
uniform mat4 uViewProj;
out vec4 vColor;
void main()
{
    vColor = aColor;
    gl_Position = uViewProj * vec4(aPos, 1.0);
}";

    public const string LineFragment = @"#version 330 core
in vec4 vColor;
out vec4 FragColor;
void main() { FragColor = vColor; }";

    // ------------------------------------------------------------------
    // 2D sprites / UI.
    // ------------------------------------------------------------------

    public const string SpriteVertex = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;
layout (location = 2) in vec4 aColor;
uniform mat4 uProjection;
out vec2 vUV;
out vec4 vColor;
void main()
{
    vUV = aUV;
    vColor = aColor;
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
}";

    public const string SpriteFragment = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
out vec4 FragColor;
uniform sampler2D uTexture;
void main()
{
    FragColor = texture(uTexture, vUV) * vColor;
}";

    // ------------------------------------------------------------------
    // Billboard particles.
    // ------------------------------------------------------------------

    public const string ParticleVertex = @"#version 330 core
layout (location = 0) in vec3 aCenter;
layout (location = 1) in vec2 aCorner;   // -0.5..0.5 quad corner
layout (location = 2) in vec2 aUV;
layout (location = 3) in vec4 aColor;
layout (location = 4) in float aSize;
layout (location = 5) in float aRotation;

uniform mat4 uViewProj;
uniform vec3 uCameraRight;
uniform vec3 uCameraUp;

out vec2 vUV;
out vec4 vColor;

void main()
{
    float c = cos(aRotation);
    float s = sin(aRotation);
    vec2 corner = vec2(aCorner.x * c - aCorner.y * s, aCorner.x * s + aCorner.y * c);
    vec3 worldPos = aCenter + (uCameraRight * corner.x + uCameraUp * corner.y) * aSize;
    vUV = aUV;
    vColor = aColor;
    gl_Position = uViewProj * vec4(worldPos, 1.0);
}";

    public const string ParticleFragment = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
out vec4 FragColor;
uniform sampler2D uTexture;
void main()
{
    FragColor = texture(uTexture, vUV) * vColor;
}";
}
