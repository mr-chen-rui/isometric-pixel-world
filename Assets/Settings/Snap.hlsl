//UNITY_SHADER_NO_UPGRADE
#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED

void Snap_float(float Factor, float Increment, out float Output)
{
    Output = round(Factor / Increment) * Increment;
}

#endif //MYHLSLINCLUDE_INCLUDED