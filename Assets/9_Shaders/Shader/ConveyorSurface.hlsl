#ifndef FLATWORLD_CONVEYOR_SURFACE_INCLUDED
#define FLATWORLD_CONVEYOR_SURFACE_INCLUDED
// 原图边框保持静止，中间带条以世界输送距离滚动；圆弧与运输路径共用半径。
float2 ConveyorSurfaceUv(float2 uv, float4 region, float4 animation, float4 surface, float time)
{
    if (animation.x < 5.5 || animation.x > 7.5) return uv;
    float2 size = max(region.zw - region.xy, float2(.000001, .000001));
    float2 local = (uv - region.xy) / size;
    float progress = local.x;
    if (animation.x > 6.5)
    {
        float2 radial = float2(local.x, local.y - 1);
        float angle = atan2(radial.x, -radial.y);
        local.y = .5 - (length(radial) - .5) / max(.001, animation.w);
        clip(local.y); clip(1 - local.y);
        local.x = angle / 1.57079632679;
        progress = angle * .5;
    }
    bool tread = local.y >= surface.x && local.y <= surface.y;
    float u = tread ? frac(progress - animation.z - time * animation.y) : local.x;
    local.x = lerp(surface.z, 1 - surface.z, u);
    return region.xy + local * size;
}
#endif
