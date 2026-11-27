#ifndef MTD_HEADER_SHEEN_INCLUDED
#define MTD_HEADER_SHEEN_INCLUDED

// Use board coordinates so the same band crosses titles and live type in slot order.
// The Columns canvas fits its centred 3840 x 2160 board on either screen orientation.
float MtdSheenCoordinate(float4 clipPosition)
{
    float4 projected = ComputeScreenPos(clipPosition);
    float2 uv = projected.xy / projected.w;
    float scale = min(_ScreenParams.x / 3840.0, _ScreenParams.y / 2160.0);
    float2 board = float2(1920,1080) + (uv - .5) * _ScreenParams.xy * float2(1,-1) / scale;
    return board.x - .46 * board.y;
}

half3 MtdGoldSheen(half3 baseColour, float coordinate)
{
    float distance = coordinate - (fmod(_Time.y,6.1) * 858 - 355);
    float band = exp2(-distance*distance / 5100);
    half ink = saturate(baseColour.r * 2.5);
    return baseColour + (1 - baseColour) * half3(.94,.91,.34) * (.85 * ink * band);
}
#endif
