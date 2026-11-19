// Wire's Upscalers: probe. Does Unity hand this DLL its interfaces (UnityPluginLoad) when the mod
// loads it from its own folder? And which graphics API is the game on?
#include "unity/IUnityInterface.h"
#include "unity/IUnityGraphics.h"

static IUnityInterfaces* s_unity = nullptr;
static int s_loadCalls = 0;

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unity)
{
    s_unity = unity;
    s_loadCalls++;
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() { s_unity = nullptr; }

// -1: Unity never called UnityPluginLoad; else the renderer (UnityGfxRenderer: 2 D3D11, 18 D3D12, 21 Vulkan)
extern "C" int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API WU_Probe(int* loadCalls)
{
    *loadCalls = s_loadCalls;
    if (!s_unity) return -1;
    IUnityGraphics* g = s_unity->Get<IUnityGraphics>();
    return g ? (int)g->GetRenderer() : -2;
}
