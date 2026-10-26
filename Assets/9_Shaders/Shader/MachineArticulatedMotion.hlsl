#ifndef FLATWORLD_MACHINE_ARTICULATED_MOTION_INCLUDED
#define FLATWORLD_MACHINE_ARTICULATED_MOTION_INCLUDED

// 与CPU关节解一致：固定滑杆轴线、固定连杆长度、共用曲柄销。
void ResolveMachineArticulatedMotion(float mode, float phase, float radius, float3 parameters,
    out float2 translation, out float rotation)
{
    translation = 0;
    rotation = 0;
    if (mode > 9.5)
    {
        rotation = sin(phase * 2) * radius;
        return;
    }
    float sine, cosine;
    sincos(phase, sine, cosine);
    float2 crank = float2(cosine, sine) * radius;
    float dx = parameters.x - crank.x;
    float dy = sqrt(max(0, parameters.y * parameters.y - dx * dx));
    if (mode < 8.5)
        translation.y = crank.y + dy;
    else
    {
        translation = crank;
        rotation = atan2(dy, dx) - parameters.z;
    }
}

#endif
