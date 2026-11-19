// Wire's Upscalers: the native side. Unity loads this from valheim_Data\Plugins\x86_64 (only there does
// it hand over its device and, on Direct3D 12, a way to run our command lists in its frame). The
// vendors' DLLs stay in the mod's own folder and are loaded from there:
//   NVIDIA DLSS  - NGX (static library) loading nvngx_dlss.dll from the mod folder; D3D11 and D3D12
//   AMD FSR 3/4  - amd_fidelityfx_loader_dx12.dll (+ upscaler); D3D12 only
//   Intel XeSS   - libxess.dll (D3D12), libxess_dx11.dll (D3D11, Intel Arc only)
// C# fills a WUParams each frame and issues it as a render event; everything here runs on Unity's
// render thread.
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_5.h>
#include <string>
#include <cstdio>
#include <cstring>
#include <cstdarg>

#include "unity/IUnityInterface.h"
#include "unity/IUnityGraphics.h"
#include "unity/IUnityGraphicsD3D11.h"
#include "unity/IUnityGraphicsD3D12.h"

#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"
#include "ffx_api.h"
#include "dx12/ffx_api_dx12.h"
#include "ffx_upscale.h"
#include "xess/xess.h"
#include "xess/xess_d3d12.h"
#include "xess/xess_d3d11.h"

// ---- what C# sends (must match Native.cs) ----
enum Backend { BackendNone = 0, BackendDLSS = 1, BackendFSR3 = 2, BackendFSR4 = 3, BackendXeSS = 4 };
#pragma pack(push, 8)
struct WUParams
{
    int backend, model;                 // model: DLSS render preset
    int inW, inH, outW, outH;
    void* color; void* depth; void* motion; void* output;
    float jitterX, jitterY, mvScaleX, mvScaleY;
    float sharpness, frameTimeMs, cameraNear, cameraFar, fovY;
    int reset, hdr, depthInverted;
};
#pragma pack(pop)

enum { AvailDLSS = 1, AvailFSR3 = 2, AvailFSR4 = 4, AvailXeSS = 8, Ready = 1 << 16 };

static IUnityInterfaces* s_unity;
static IUnityGraphics* s_graphics;
static UnityGfxRenderer s_renderer = kUnityGfxRendererNull;
static ID3D11Device* s_d11;
static ID3D11DeviceContext* s_d11ctx;
static ID3D12Device* s_d12;
static IUnityGraphicsD3D12v7* s_u12;
static std::wstring s_modDir;
static int s_avail;
static char s_message[512] = "";

static void Note(const char* fmt, ...)
{
    va_list a; va_start(a, fmt); vsnprintf(s_message, sizeof(s_message), fmt, a); va_end(a);
}

static HMODULE LoadFromMod(const wchar_t* name)
{
    std::wstring p = s_modDir + name;
    return LoadLibraryExW(p.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
}

// ---- Direct3D 12: our own command lists, run inside Unity's frame (it inserts the barriers) ----
struct D12List
{
    ID3D12CommandAllocator* alloc[3] = {};
    UINT64 fence[3] = {};
    ID3D12GraphicsCommandList* list = nullptr;
    int at = 0;
};
static D12List s_list;

static ID3D12GraphicsCommandList* D12Begin()
{
    if (!s_list.list)
    {
        for (auto& a : s_list.alloc)
            if (FAILED(s_d12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&a)))) return nullptr;
        if (FAILED(s_d12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, s_list.alloc[0], nullptr, IID_PPV_ARGS(&s_list.list)))) return nullptr;
        s_list.list->Close();
    }
    s_list.at = (s_list.at + 1) % 3;
    ID3D12Fence* f = s_u12->GetFrameFence();
    if (f && f->GetCompletedValue() < s_list.fence[s_list.at])
    {
        HANDLE e = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        f->SetEventOnCompletion(s_list.fence[s_list.at], e);
        WaitForSingleObject(e, 1000);
        CloseHandle(e);
    }
    s_list.alloc[s_list.at]->Reset();
    s_list.list->Reset(s_list.alloc[s_list.at], nullptr);
    return s_list.list;
}

// inputs read by compute, output written as UAV; Unity moves them there before and knows where they are after
static void D12End(const WUParams& p)
{
    s_list.list->Close();
    UnityGraphicsD3D12ResourceState st[4] = {
        { (ID3D12Resource*)p.color,  D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE },
        { (ID3D12Resource*)p.depth,  D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE },
        { (ID3D12Resource*)p.motion, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE },
        { (ID3D12Resource*)p.output, D3D12_RESOURCE_STATE_UNORDERED_ACCESS,         D3D12_RESOURCE_STATE_UNORDERED_ACCESS } };
    s_list.fence[s_list.at] = s_u12->ExecuteCommandList(s_list.list, 4, st);
}

// ---- the one feature in use; made again when anything it was made with changes ----
struct Made { int backend = 0, model = 0, inW = 0, inH = 0, outW = 0, outH = 0, hdr = 0; };
static Made s_made;
static bool s_failed; // this combination failed: not tried again until it changes

// NVIDIA
static bool s_ngxInit;
static NVSDK_NGX_Parameter* s_ngxParams;
static NVSDK_NGX_Handle* s_ngxHandle;
// AMD
static HMODULE s_ffxDll;
static PfnFfxCreateContext ffxCreate; static PfnFfxDestroyContext ffxDestroy;
static PfnFfxQuery ffxQueryFn; static PfnFfxDispatch ffxDispatchFn;
static uint64_t s_fsrVersion[2]; // [0] a 3.x, [1] a 4.x
static ffxContext s_ffx;
// Intel
static HMODULE s_xessDll;
static xess_context_handle_t s_xess;
static decltype(&xessD3D12CreateContext) xD12Create; static decltype(&xessD3D12Init) xD12Init;
static decltype(&xessD3D12Execute) xD12Exec; static decltype(&xessD3D11CreateContext) xD11Create;
static decltype(&xessD3D11Init) xD11Init; static decltype(&xessD3D11Execute) xD11Exec;
static decltype(&xessDestroyContext) xDestroy; static decltype(&xessSetVelocityScale) xVelScale;

static void ReleaseFeature()
{
    if (s_ngxHandle) { s_renderer == kUnityGfxRendererD3D12 ? NVSDK_NGX_D3D12_ReleaseFeature(s_ngxHandle) : NVSDK_NGX_D3D11_ReleaseFeature(s_ngxHandle); s_ngxHandle = nullptr; }
    if (s_ffx) { ffxDestroy(&s_ffx, nullptr); s_ffx = nullptr; }
    if (s_xess) { xDestroy(s_xess); s_xess = nullptr; }
    s_made = Made();
}

// What this GPU and graphics API can run (asked once, on the render thread).
static void Detect()
{
    s_avail = 0;
    const bool d12 = s_renderer == kUnityGfxRendererD3D12, d11 = s_renderer == kUnityGfxRendererD3D11;
    if (!d11 && !d12) { s_avail = Ready; Note("graphics API %d: no upscalers", (int)s_renderer); return; }

    // NVIDIA DLSS: NGX finds nvngx_dlss.dll in the mod folder
    {
        const wchar_t* paths[1] = { s_modDir.c_str() };
        NVSDK_NGX_FeatureCommonInfo info = {};
        info.PathListInfo.Path = paths; info.PathListInfo.Length = 1;
        NVSDK_NGX_Result r = d12
            ? NVSDK_NGX_D3D12_Init_with_ProjectID("8f0e6c2a-4b1d-4e5a-9c3f-77a1d2b0e6c9", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", s_modDir.c_str(), s_d12, &info)
            : NVSDK_NGX_D3D11_Init_with_ProjectID("8f0e6c2a-4b1d-4e5a-9c3f-77a1d2b0e6c9", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", s_modDir.c_str(), s_d11, &info);
        if (NVSDK_NGX_SUCCEED(r))
        {
            s_ngxInit = true;
            NVSDK_NGX_Parameter* caps = nullptr;
            r = d12 ? NVSDK_NGX_D3D12_GetCapabilityParameters(&caps) : NVSDK_NGX_D3D11_GetCapabilityParameters(&caps);
            int ok = 0;
            if (NVSDK_NGX_SUCCEED(r) && caps) NVSDK_NGX_Parameter_GetI(caps, NVSDK_NGX_Parameter_SuperSampling_Available, &ok);
            if (caps) d12 ? NVSDK_NGX_D3D12_DestroyParameters(caps) : NVSDK_NGX_D3D11_DestroyParameters(caps);
            if (ok) s_avail |= AvailDLSS;
            (d12 ? NVSDK_NGX_D3D12_AllocateParameters : NVSDK_NGX_D3D11_AllocateParameters)(&s_ngxParams);
        }
    }

    // AMD FSR: the loader lists the versions this GPU can run
    if (d12 && (s_ffxDll = LoadFromMod(L"amd_fidelityfx_loader_dx12.dll")))
    {
        ffxCreate = (PfnFfxCreateContext)GetProcAddress(s_ffxDll, "ffxCreateContext");
        ffxDestroy = (PfnFfxDestroyContext)GetProcAddress(s_ffxDll, "ffxDestroyContext");
        ffxQueryFn = (PfnFfxQuery)GetProcAddress(s_ffxDll, "ffxQuery");
        ffxDispatchFn = (PfnFfxDispatch)GetProcAddress(s_ffxDll, "ffxDispatch");
        if (ffxCreate && ffxDestroy && ffxQueryFn && ffxDispatchFn)
        {
            uint64_t count = 0;
            ffxQueryDescGetVersions q = {};
            q.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
            q.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
            q.device = s_d12; q.outputCount = &count;
            ffxQueryFn(nullptr, &q.header);
            if (count > 0 && count < 64)
            {
                uint64_t ids[64]; const char* names[64] = {};
                q.versionIds = ids; q.versionNames = names;
                ffxQueryFn(nullptr, &q.header);
                for (uint64_t i = 0; i < count; i++)
                {
                    const char* n = names[i]; if (!n) continue;
                    while (*n && (*n < '0' || *n > '9')) n++; // "FSR 4.0.2" or "4.0.2"
                    if (*n == '3' && !s_fsrVersion[0]) { s_fsrVersion[0] = ids[i]; s_avail |= AvailFSR3; }
                    if (*n == '4' && !s_fsrVersion[1]) { s_fsrVersion[1] = ids[i]; s_avail |= AvailFSR4; }
                }
            }
        }
    }

    // Intel XeSS: on D3D11 only Arc runs it, so a context that can't be made means no
    if ((s_xessDll = LoadFromMod(d12 ? L"libxess.dll" : L"libxess_dx11.dll")))
    {
        xDestroy = (decltype(xDestroy))GetProcAddress(s_xessDll, "xessDestroyContext");
        xVelScale = (decltype(xVelScale))GetProcAddress(s_xessDll, "xessSetVelocityScale");
        xD12Create = (decltype(xD12Create))GetProcAddress(s_xessDll, "xessD3D12CreateContext");
        xD12Init = (decltype(xD12Init))GetProcAddress(s_xessDll, "xessD3D12Init");
        xD12Exec = (decltype(xD12Exec))GetProcAddress(s_xessDll, "xessD3D12Execute");
        xD11Create = (decltype(xD11Create))GetProcAddress(s_xessDll, "xessD3D11CreateContext");
        xD11Init = (decltype(xD11Init))GetProcAddress(s_xessDll, "xessD3D11Init");
        xD11Exec = (decltype(xD11Exec))GetProcAddress(s_xessDll, "xessD3D11Execute");
        xess_context_handle_t probe = nullptr;
        bool ok = d12 ? (xD12Create && xD12Init && xD12Exec && xD12Create(s_d12, &probe) == XESS_RESULT_SUCCESS)
                      : (xD11Create && xD11Init && xD11Exec && xD11Create(s_d11, &probe) == XESS_RESULT_SUCCESS);
        if (probe) xDestroy(probe);
        if (ok && xDestroy && xVelScale) s_avail |= AvailXeSS;
    }
    Note("ready on %s: DLSS %d, FSR3 %d, FSR4 %d, XeSS %d", d12 ? "D3D12" : "D3D11",
        !!(s_avail & AvailDLSS), !!(s_avail & AvailFSR3), !!(s_avail & AvailFSR4), !!(s_avail & AvailXeSS));
    s_avail |= Ready;
}

static int DlssQuality(float ratio)
{
    if (ratio < 1.05f) return NVSDK_NGX_PerfQuality_Value_DLAA;
    if (ratio < 1.6f) return NVSDK_NGX_PerfQuality_Value_MaxQuality;
    if (ratio < 1.8f) return NVSDK_NGX_PerfQuality_Value_Balanced;
    if (ratio < 2.5f) return NVSDK_NGX_PerfQuality_Value_MaxPerf;
    return NVSDK_NGX_PerfQuality_Value_UltraPerformance;
}

static xess_quality_settings_t XessQuality(float ratio)
{
    if (ratio < 1.05f) return XESS_QUALITY_SETTING_AA;
    if (ratio < 1.4f) return XESS_QUALITY_SETTING_ULTRA_QUALITY_PLUS;
    if (ratio < 1.6f) return XESS_QUALITY_SETTING_QUALITY;
    if (ratio < 1.85f) return XESS_QUALITY_SETTING_BALANCED;
    if (ratio < 2.5f) return XESS_QUALITY_SETTING_PERFORMANCE;
    return XESS_QUALITY_SETTING_ULTRA_PERFORMANCE;
}

// The feature for these parameters, made now if it differs from the one there.
static bool Ensure(const WUParams& p, ID3D12GraphicsCommandList* cl)
{
    if (s_made.backend == p.backend && s_made.model == p.model && s_made.inW == p.inW && s_made.inH == p.inH &&
        s_made.outW == p.outW && s_made.outH == p.outH && s_made.hdr == p.hdr)
        return !s_failed;
    ReleaseFeature();
    s_made = { p.backend, p.model, p.inW, p.inH, p.outW, p.outH, p.hdr };
    s_failed = true;
    const bool d12 = s_renderer == kUnityGfxRendererD3D12;
    float ratio = (float)p.outH / (float)(p.inH > 0 ? p.inH : 1);

    if (p.backend == BackendDLSS && (s_avail & AvailDLSS) && s_ngxParams)
    {
        const char* hints[] = { NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_DLAA, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Quality,
            NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Balanced, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Performance,
            NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraPerformance, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraQuality };
        for (const char* h : hints) NVSDK_NGX_Parameter_SetUI(s_ngxParams, h, (unsigned)p.model);
        NVSDK_NGX_DLSS_Create_Params c = {};
        c.Feature.InWidth = p.inW; c.Feature.InHeight = p.inH;
        c.Feature.InTargetWidth = p.outW; c.Feature.InTargetHeight = p.outH;
        c.Feature.InPerfQualityValue = (NVSDK_NGX_PerfQuality_Value)DlssQuality(ratio);
        c.InFeatureCreateFlags = NVSDK_NGX_DLSS_Feature_Flags_MVLowRes | NVSDK_NGX_DLSS_Feature_Flags_AutoExposure |
            (p.hdr ? NVSDK_NGX_DLSS_Feature_Flags_IsHDR : 0) | (p.depthInverted ? NVSDK_NGX_DLSS_Feature_Flags_DepthInverted : 0);
        NVSDK_NGX_Result r = d12 ? NGX_D3D12_CREATE_DLSS_EXT(cl, 1, 1, &s_ngxHandle, s_ngxParams, &c)
                                 : NGX_D3D11_CREATE_DLSS_EXT(s_d11ctx, &s_ngxHandle, s_ngxParams, &c);
        if (NVSDK_NGX_FAILED(r)) { Note("DLSS create failed 0x%08x", (unsigned)r); s_ngxHandle = nullptr; return false; }
        Note("DLSS %dx%d -> %dx%d, preset %d", p.inW, p.inH, p.outW, p.outH, p.model);
    }
    else if ((p.backend == BackendFSR3 || p.backend == BackendFSR4) && d12)
    {
        uint64_t version = s_fsrVersion[p.backend == BackendFSR4 ? 1 : 0];
        if (!version) { Note("FSR version not available"); return false; }
        ffxCreateContextDescUpscale desc = {};
        desc.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
        desc.flags = FFX_UPSCALE_ENABLE_AUTO_EXPOSURE | FFX_UPSCALE_ENABLE_DYNAMIC_RESOLUTION |
            (p.hdr ? FFX_UPSCALE_ENABLE_HIGH_DYNAMIC_RANGE : 0) | (p.depthInverted ? FFX_UPSCALE_ENABLE_DEPTH_INVERTED : 0);
        desc.maxRenderSize = { (uint32_t)p.outW, (uint32_t)p.outH };
        desc.maxUpscaleSize = { (uint32_t)p.outW, (uint32_t)p.outH };
        ffxCreateBackendDX12Desc backend = {};
        backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
        backend.device = s_d12;
        ffxOverrideVersion over = {};
        over.header.type = FFX_API_DESC_TYPE_OVERRIDE_VERSION;
        over.versionId = version;
        desc.header.pNext = &backend.header;
        backend.header.pNext = &over.header;
        ffxReturnCode_t r = ffxCreate(&s_ffx, &desc.header, nullptr);
        if (r != FFX_API_RETURN_OK) { Note("FSR create failed %u", r); s_ffx = nullptr; return false; }
        Note("FSR %d %dx%d -> %dx%d", p.backend == BackendFSR4 ? 4 : 3, p.inW, p.inH, p.outW, p.outH);
    }
    else if (p.backend == BackendXeSS && (s_avail & AvailXeSS))
    {
        uint32_t flags = XESS_INIT_FLAG_ENABLE_AUTOEXPOSURE | (p.depthInverted ? XESS_INIT_FLAG_INVERTED_DEPTH : 0) |
            (p.hdr ? 0 : XESS_INIT_FLAG_LDR_INPUT_COLOR);
        xess_result_t r;
        if (d12)
        {
            if ((r = xD12Create(s_d12, &s_xess)) == XESS_RESULT_SUCCESS)
            {
                xess_d3d12_init_params_t init = {};
                init.outputResolution = { (uint32_t)p.outW, (uint32_t)p.outH };
                init.qualitySetting = XessQuality(ratio);
                init.initFlags = flags; init.creationNodeMask = 1; init.visibleNodeMask = 1;
                r = xD12Init(s_xess, &init);
            }
        }
        else if ((r = xD11Create(s_d11, &s_xess)) == XESS_RESULT_SUCCESS)
        {
            xess_d3d11_init_params_t init = {};
            init.outputResolution = { (uint32_t)p.outW, (uint32_t)p.outH };
            init.qualitySetting = XessQuality(ratio);
            init.initFlags = flags;
            r = xD11Init(s_xess, &init);
        }
        if (r != XESS_RESULT_SUCCESS) { Note("XeSS init failed %d", (int)r); if (s_xess) xDestroy(s_xess); s_xess = nullptr; return false; }
        Note("XeSS %dx%d -> %dx%d", p.inW, p.inH, p.outW, p.outH);
    }
    else { Note("backend %d not available here", p.backend); return false; }
    s_failed = false;
    return true;
}

static void Evaluate(const WUParams& p)
{
    if (p.backend == BackendNone || !p.color || !p.depth || !p.motion || !p.output) return;
    const bool d12 = s_renderer == kUnityGfxRendererD3D12;
    ID3D12GraphicsCommandList* cl = nullptr;
    if (d12 && !(cl = D12Begin())) { Note("no D3D12 command list"); return; }
    bool ok = Ensure(p, cl);
    if (ok && p.backend == BackendDLSS)
    {
        NVSDK_NGX_Result r;
        if (d12)
        {
            NVSDK_NGX_D3D12_DLSS_Eval_Params e = {};
            e.Feature.pInColor = (ID3D12Resource*)p.color; e.Feature.pInOutput = (ID3D12Resource*)p.output;
            e.pInDepth = (ID3D12Resource*)p.depth; e.pInMotionVectors = (ID3D12Resource*)p.motion;
            e.InJitterOffsetX = p.jitterX; e.InJitterOffsetY = p.jitterY;
            e.InRenderSubrectDimensions = { (unsigned)p.inW, (unsigned)p.inH };
            e.InReset = p.reset; e.InMVScaleX = p.mvScaleX; e.InMVScaleY = p.mvScaleY;
            e.InPreExposure = 1.f; e.InExposureScale = 1.f; e.InFrameTimeDeltaInMsec = p.frameTimeMs;
            r = NGX_D3D12_EVALUATE_DLSS_EXT(cl, s_ngxHandle, s_ngxParams, &e);
        }
        else
        {
            NVSDK_NGX_D3D11_DLSS_Eval_Params e = {};
            e.Feature.pInColor = (ID3D11Resource*)p.color; e.Feature.pInOutput = (ID3D11Resource*)p.output;
            e.pInDepth = (ID3D11Resource*)p.depth; e.pInMotionVectors = (ID3D11Resource*)p.motion;
            e.InJitterOffsetX = p.jitterX; e.InJitterOffsetY = p.jitterY;
            e.InRenderSubrectDimensions = { (unsigned)p.inW, (unsigned)p.inH };
            e.InReset = p.reset; e.InMVScaleX = p.mvScaleX; e.InMVScaleY = p.mvScaleY;
            e.InPreExposure = 1.f; e.InExposureScale = 1.f; e.InFrameTimeDeltaInMsec = p.frameTimeMs;
            r = NGX_D3D11_EVALUATE_DLSS_EXT(s_d11ctx, s_ngxHandle, s_ngxParams, &e);
        }
        if (NVSDK_NGX_FAILED(r)) Note("DLSS evaluate failed 0x%08x", (unsigned)r);
    }
    else if (ok && (p.backend == BackendFSR3 || p.backend == BackendFSR4))
    {
        ffxDispatchDescUpscale d = {};
        d.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
        d.commandList = cl;
        d.color = ffxApiGetResourceDX12((ID3D12Resource*)p.color, FFX_API_RESOURCE_STATE_COMPUTE_READ);
        d.depth = ffxApiGetResourceDX12((ID3D12Resource*)p.depth, FFX_API_RESOURCE_STATE_COMPUTE_READ);
        d.motionVectors = ffxApiGetResourceDX12((ID3D12Resource*)p.motion, FFX_API_RESOURCE_STATE_COMPUTE_READ);
        d.output = ffxApiGetResourceDX12((ID3D12Resource*)p.output, FFX_API_RESOURCE_STATE_UNORDERED_ACCESS);
        d.jitterOffset = { p.jitterX, p.jitterY };
        d.motionVectorScale = { p.mvScaleX, p.mvScaleY };
        d.renderSize = { (uint32_t)p.inW, (uint32_t)p.inH };
        d.upscaleSize = { (uint32_t)p.outW, (uint32_t)p.outH };
        d.enableSharpening = p.sharpness > 0.f; d.sharpness = p.sharpness;
        d.frameTimeDelta = p.frameTimeMs; d.preExposure = 1.f; d.reset = p.reset != 0;
        d.cameraNear = p.cameraNear; d.cameraFar = p.cameraFar; d.cameraFovAngleVertical = p.fovY;
        d.viewSpaceToMetersFactor = 1.f;
        ffxReturnCode_t r = ffxDispatchFn(&s_ffx, &d.header);
        if (r != FFX_API_RETURN_OK) Note("FSR dispatch failed %u", r);
    }
    else if (ok && p.backend == BackendXeSS)
    {
        xVelScale(s_xess, p.mvScaleX, p.mvScaleY);
        xess_result_t r;
        if (d12)
        {
            xess_d3d12_execute_params_t e = {};
            e.pColorTexture = (ID3D12Resource*)p.color; e.pVelocityTexture = (ID3D12Resource*)p.motion;
            e.pDepthTexture = (ID3D12Resource*)p.depth; e.pOutputTexture = (ID3D12Resource*)p.output;
            e.jitterOffsetX = p.jitterX; e.jitterOffsetY = p.jitterY; e.exposureScale = 1.f;
            e.resetHistory = p.reset; e.inputWidth = p.inW; e.inputHeight = p.inH;
            r = xD12Exec(s_xess, cl, &e);
        }
        else
        {
            xess_d3d11_execute_params_t e = {};
            e.pColorTexture = (ID3D11Resource*)p.color; e.pVelocityTexture = (ID3D11Resource*)p.motion;
            e.pDepthTexture = (ID3D11Resource*)p.depth; e.pOutputTexture = (ID3D11Resource*)p.output;
            e.jitterOffsetX = p.jitterX; e.jitterOffsetY = p.jitterY; e.exposureScale = 1.f;
            e.resetHistory = p.reset; e.inputWidth = p.inW; e.inputHeight = p.inH;
            r = xD11Exec(s_xess, &e);
        }
        if (r != XESS_RESULT_SUCCESS) Note("XeSS execute failed %d", (int)r);
    }
    if (d12) D12End(p);
}

// ---- Unity events ----
enum { EventDetect = 1, EventEvaluate = 2, EventRelease = 3 };

static void UNITY_INTERFACE_API OnRenderEvent(int id, void* data)
{
    if (id == EventDetect) { if (!(s_avail & Ready)) Detect(); }
    else if (id == EventEvaluate && data && (s_avail & Ready)) Evaluate(*(const WUParams*)data);
    else if (id == EventRelease) ReleaseFeature();
}

static void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType type)
{
    if (type == kUnityGfxDeviceEventInitialize)
    {
        s_renderer = s_graphics->GetRenderer();
        if (s_renderer == kUnityGfxRendererD3D11)
        {
            IUnityGraphicsD3D11* g = s_unity->Get<IUnityGraphicsD3D11>();
            s_d11 = g ? g->GetDevice() : nullptr;
            if (s_d11) s_d11->GetImmediateContext(&s_d11ctx);
        }
        else if (s_renderer == kUnityGfxRendererD3D12)
        {
            s_u12 = s_unity->Get<IUnityGraphicsD3D12v7>();
            s_d12 = s_u12 ? s_u12->GetDevice() : nullptr;
            if (s_u12)
            {
                UnityD3D12PluginEventConfig cfg = {};
                cfg.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
                cfg.flags = kUnityD3D12EventConfigFlag_SyncWorkerThreads | kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState;
                cfg.ensureActiveRenderTextureIsBound = false;
                for (int e = EventDetect; e <= EventRelease; e++) s_u12->ConfigureEvent(e, &cfg);
            }
        }
    }
    else if (type == kUnityGfxDeviceEventShutdown)
    {
        ReleaseFeature();
        if (s_ngxParams) { s_renderer == kUnityGfxRendererD3D12 ? NVSDK_NGX_D3D12_DestroyParameters(s_ngxParams) : NVSDK_NGX_D3D11_DestroyParameters(s_ngxParams); s_ngxParams = nullptr; }
        if (s_ngxInit) { s_renderer == kUnityGfxRendererD3D12 ? NVSDK_NGX_D3D12_Shutdown1(s_d12) : NVSDK_NGX_D3D11_Shutdown1(s_d11); s_ngxInit = false; }
        s_renderer = kUnityGfxRendererNull;
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unity)
{
    s_unity = unity;
    s_graphics = unity->Get<IUnityGraphics>();
    s_graphics->RegisterDeviceEventCallback(OnDeviceEvent);
    OnDeviceEvent(kUnityGfxDeviceEventInitialize);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    if (s_graphics) s_graphics->UnregisterDeviceEventCallback(OnDeviceEvent);
}

// ---- C# ----
extern "C" UNITY_INTERFACE_EXPORT void WU_SetModFolder(const wchar_t* dir)
{
    s_modDir = dir;
    if (!s_modDir.empty() && s_modDir.back() != L'\\') s_modDir += L'\\';
}
extern "C" UNITY_INTERFACE_EXPORT int WU_Renderer() { return s_unity ? (int)s_renderer : -1; }
extern "C" UNITY_INTERFACE_EXPORT int WU_Available() { return s_avail; }
extern "C" UNITY_INTERFACE_EXPORT const char* WU_Message() { return s_message; }
extern "C" UNITY_INTERFACE_EXPORT UnityRenderingEventAndData WU_EventFunc() { return OnRenderEvent; }
